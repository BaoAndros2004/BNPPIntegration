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
            var paymentRoot = _configuration["ProcessingStorage:PaymentDirectory"];
            if (string.IsNullOrWhiteSpace(paymentRoot))
                throw new InvalidOperationException("ProcessingStorage:PaymentDirectory is required.");

            var paymentDirectory = Path.GetFullPath(paymentRoot);
            var retryIntervalMinutes = _configuration.GetValue<int>("BackgroundProcessing:PaymentRetryIntervalMinutes");
            if (retryIntervalMinutes <= 0)
                throw new InvalidOperationException("BackgroundProcessing:PaymentRetryIntervalMinutes must be greater than 0.");

            var outputXmlDirectory = Path.Combine(paymentDirectory, "Export");

            Directory.CreateDirectory(paymentDirectory);
            Directory.CreateDirectory(outputXmlDirectory);

            _logger.LogInformation(
                "Payment worker started. Storage root: {PaymentDirectory}. Waiting for API queue files.",
                paymentDirectory);

            if (_sftpService.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    _logger.LogInformation("Testing SFTP connection to BNP Paribas on startup...");
                    await _sftpService.TestConnectionAsync(stoppingToken);
                }, stoppingToken);
            }

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

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var content = await File.ReadAllTextAsync(file, stoppingToken);
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
                            
                        // Tự động đẩy qua SFTP nếu cấu hình Sftp:Enabled = true
                        if (_sftpService.IsEnabled)
                        {
                            try
                            {
                                await _sftpService.UploadPaymentFileAsync(pgpFilePath, stoppingToken);
                            }
                            catch (Exception sftpEx)
                            {
                                _logger.LogError(sftpEx, "Failed to upload {PgpFileName} to BNP SFTP. File will be retained for retry.", Path.GetFileName(pgpFilePath));
                                throw; // Ném exception để giữ file JSON retry lại sau
                            }
                        }

                        // Auto-delete the JSON file after processing
                        File.Delete(file);
                        continue; // process next file
                    }
                    
                    _logger.LogWarning("Failed to deserialize JSON or PaymentType missing in {FileName}; file was retained for retry.", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing payment file {FileName}", fileName);
                    _logger.LogWarning("File {FileName} was retained for retry.", fileName);
                }
            }
        }
    }
}
