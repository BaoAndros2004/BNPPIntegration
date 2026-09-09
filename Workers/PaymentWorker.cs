using System.Text;
using System.Text.Json;
using BNPPIntegration.BNPP.Payments;
using BNPPIntegration.BNPP.Payments.Domestic;
using BNPPIntegration.BNPP.Payments.IntraCompany;
using BNPPIntegration.BNPP.Payments.International;
using BNPPIntegration.BNPP.Security;

namespace BNPPIntegration.Workers
{
    public sealed class PaymentWorker : BackgroundService
    {
        private readonly ILogger<PaymentWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly DomesticGenerator _domesticGenerator;
        private readonly IntraCompanyGenerator _intraCompanyGenerator;
        private readonly InternationalGenerator _internationalGenerator;
        private readonly PgpEncryptionService _pgpEncryptionService;
        private readonly SftpService _sftpService;

        public PaymentWorker(
            ILogger<PaymentWorker> logger,
            IConfiguration configuration,
            DomesticGenerator domesticGenerator,
            IntraCompanyGenerator intraCompanyGenerator,
            InternationalGenerator internationalGenerator,
            PgpEncryptionService pgpEncryptionService,
            SftpService sftpService)
        {
            _logger = logger;
            _configuration = configuration;
            _domesticGenerator = domesticGenerator;
            _intraCompanyGenerator = intraCompanyGenerator;
            _internationalGenerator = internationalGenerator;
            _pgpEncryptionService = pgpEncryptionService;
            _sftpService = sftpService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var paymentRoot = _configuration["ProcessingStorage:PaymentDirectory"] ?? "payments";
            var paymentDirectory = Path.IsPathRooted(paymentRoot)
                ? paymentRoot
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, paymentRoot));
            var retryIntervalMinutes = _configuration.GetValue<int>("BackgroundProcessing:PaymentRetryIntervalMinutes");
            if (retryIntervalMinutes <= 0)
                throw new InvalidOperationException("BackgroundProcessing:PaymentRetryIntervalMinutes must be greater than 0.");

            var exportRoot = _configuration["ProcessingStorage:ExportDirectory"];
            var outputXmlDirectory = !string.IsNullOrWhiteSpace(exportRoot)
                ? (Path.IsPathRooted(exportRoot)
                    ? exportRoot
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, exportRoot)))
                : paymentDirectory;

            Directory.CreateDirectory(paymentDirectory);
            Directory.CreateDirectory(outputXmlDirectory);

            _logger.LogInformation(
                "Payment worker started. Storage root: {PaymentDirectory}, Export root: {OutputXmlDirectory}. Waiting for API queue files.",
                paymentDirectory,
                outputXmlDirectory);

            using var processingSignal = new SemaphoreSlim(0, 1);
            void SignalProcessing()
            {
                try
                {
                    processingSignal.Release();
                }
                catch (SemaphoreFullException)
                {
                    // A scan is already pending; one scan processes every queued JSON file.
                }
            }

            using var watcher = new FileSystemWatcher(paymentDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, _) => SignalProcessing();
            watcher.Renamed += (_, _) => SignalProcessing();
            watcher.Error += (_, args) =>
            {
                _logger.LogError(args.GetException(), "Payment file watcher error; scheduling a recovery scan.");
                SignalProcessing();
            };

            async Task RunRetryTimerAsync()
            {
                using var retryTimer = new PeriodicTimer(TimeSpan.FromMinutes(retryIntervalMinutes));
                while (await retryTimer.WaitForNextTickAsync(stoppingToken))
                {
                    SignalProcessing();
                }
            }

            var retryTask = RunRetryTimerAsync();

            // Process files already queued before the worker started.
            await RunProcessingCycleAsync(
                paymentDirectory,
                outputXmlDirectory,
                stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await processingSignal.WaitAsync(stoppingToken);
                    await RunProcessingCycleAsync(
                        paymentDirectory,
                        outputXmlDirectory,
                        stoppingToken);
                }
            }
            finally
            {
                try
                {
                    await retryTask;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal service shutdown.
                }
            }
        }

        private async Task RunProcessingCycleAsync(
            string paymentDirectory,
            string outputXmlDirectory,
            CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Payment processing cycle started.");
                await ProcessPaymentFilesAsync(
                    paymentDirectory,
                    outputXmlDirectory,
                    stoppingToken);
                _logger.LogInformation("Payment processing cycle completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing payment files.");
            }
        }

        private async Task ProcessPaymentFilesAsync(string paymentDirectory, string outputXmlDir, CancellationToken stoppingToken)
        {
            var files = Directory.GetFiles(paymentDirectory, "*.json");
            if (files.Length == 0)
                return;

            var processedBatch = new List<(string JsonFilePath, string PgpFilePath)>();

            // -------------------------------------------------------------
            // Phase 1: Batch XML Generation & PGP Encryption
            // -------------------------------------------------------------
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var content = await ReadFileContentWithRetryAsync(file, stoppingToken);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    var paymentModel = JsonSerializer.Deserialize<PaymentModel>(content, options);
                    if (paymentModel != null && !string.IsNullOrWhiteSpace(paymentModel.PaymentType))
                    {
                        var xmlFileName = $"XMLISO_VNHCMHUNGPHAT_{DateTime.Now:ddMMyy_HHmmssfff}.xml";
                        var xmlFilePath = Path.Combine(outputXmlDir, xmlFileName);

                        if (string.Equals(paymentModel.PaymentType, "Domestic", StringComparison.OrdinalIgnoreCase))
                        {
                            var request = JsonSerializer.Deserialize<DomesticRequest>(paymentModel.Payment.GetRawText(), options);
                            await _domesticGenerator.GenerateFileAsync(request!, xmlFilePath, stoppingToken);
                        }
                        else if (string.Equals(paymentModel.PaymentType, "IntraCompany", StringComparison.OrdinalIgnoreCase))
                        {
                            var request = JsonSerializer.Deserialize<IntraCompanyRequest>(paymentModel.Payment.GetRawText(), options);
                            await _intraCompanyGenerator.GenerateFileAsync(request!, xmlFilePath, stoppingToken);
                        }
                        else if (string.Equals(paymentModel.PaymentType, "International", StringComparison.OrdinalIgnoreCase))
                        {
                            var request = JsonSerializer.Deserialize<InternationalRequest>(paymentModel.Payment.GetRawText(), options);
                            await _internationalGenerator.GenerateFileAsync(request!, xmlFilePath, stoppingToken);
                        }
                        else
                        {
                            _logger.LogWarning("Unknown PaymentType {PaymentType} in {FileName}", paymentModel.PaymentType, fileName);
                            continue;
                        }

                        // Encrypt
                        var pgpFilePath = await _pgpEncryptionService.EncryptAsync(xmlFilePath, stoppingToken);
                            
                        _logger.LogInformation("Successfully generated and encrypted XML {XmlFileName} from {FileName}", xmlFileName, fileName);
                        processedBatch.Add((file, pgpFilePath));

                        // Ensure distinct millisecond timestamps for consecutive files
                        await Task.Delay(15, stoppingToken);
                        continue;
                    }
                    
                    _logger.LogWarning("Failed to deserialize JSON or PaymentType missing in {FileName}; file was retained for retry.", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating payment file {FileName}", fileName);
                    _logger.LogWarning("File {FileName} was retained for retry.", fileName);
                }
            }

            if (processedBatch.Count == 0)
                return;

            // -------------------------------------------------------------
            // Phase 2: Batch SFTP Upload (Single Connection Session)
            // -------------------------------------------------------------
            if (_sftpService.IsEnabled)
            {
                try
                {
                    _logger.LogInformation("Uploading batch of {Count} payment file(s) to BNP SFTP in a single session...", processedBatch.Count);
                    var uploadedPgpPaths = await _sftpService.UploadPaymentFilesBatchAsync(
                        processedBatch.Select(x => x.PgpFilePath),
                        stoppingToken);

                    var uploadedSet = new HashSet<string>(uploadedPgpPaths, StringComparer.OrdinalIgnoreCase);

                    foreach (var item in processedBatch)
                    {
                        if (uploadedSet.Contains(item.PgpFilePath))
                        {
                            try
                            {
                                File.Delete(item.JsonFilePath);
                                _logger.LogInformation("Cleaned up source queue file {FileName}", Path.GetFileName(item.JsonFilePath));
                            }
                            catch (Exception delEx)
                            {
                                _logger.LogWarning(delEx, "Failed to delete processed queue file {FileName}", Path.GetFileName(item.JsonFilePath));
                            }
                        }
                        else
                        {
                            _logger.LogWarning("File {FileName} was not uploaded; retaining JSON for retry.", Path.GetFileName(item.JsonFilePath));
                        }
                    }
                }
                catch (Exception sftpEx)
                {
                    _logger.LogError(sftpEx, "Batch SFTP upload failed. Source JSON files will be retained for retry.");
                }
            }
            else
            {
                // SFTP disabled: all generated files are kept, source JSON files can be cleaned up
                foreach (var item in processedBatch)
                {
                    try
                    {
                        File.Delete(item.JsonFilePath);
                    }
                    catch (Exception delEx)
                    {
                        _logger.LogWarning(delEx, "Failed to delete queue file {FileName}", Path.GetFileName(item.JsonFilePath));
                    }
                }
            }
        }

        private static async Task<string> ReadFileContentWithRetryAsync(string filePath, CancellationToken cancellationToken, int maxAttempts = 5)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    return await reader.ReadToEndAsync(cancellationToken);
                }
                catch (IOException) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(200 * attempt, cancellationToken);
                }
            }

            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }
    }
}
