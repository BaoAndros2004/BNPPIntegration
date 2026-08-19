using System.Text.Json;
using BNPPIntegration.BNPP.Payments.Pain001;
using BNPPIntegration.BNPP.Payments.Pain001.Models;

namespace BNPPIntegration.Workers
{
    public sealed class PaymentWorker : BackgroundService
    {
        private readonly ILogger<PaymentWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly Pain001Generator _generator;

        public PaymentWorker(
            ILogger<PaymentWorker> logger,
            IConfiguration configuration,
            Pain001Generator generator)
        {
            _logger = logger;
            _configuration = configuration;
            _generator = generator;
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
                    
                    var generationRequest = JsonSerializer.Deserialize<Pain001GenerationRequest>(content, options);
                    if (generationRequest?.Payment != null)
                    {
                        var request = generationRequest.Payment;
                        var xmlFileName = GetOutputFileName(generationRequest);
                        var xmlFilePath = Path.Combine(outputXmlDir, xmlFileName);

                        await _generator.GenerateFileAsync(request, xmlFilePath, stoppingToken);
                            
                        _logger.LogInformation("Successfully generated XML {XmlFileName} from {FileName}", xmlFileName, fileName);
                            
                        // Auto-delete the JSON file after processing
                        File.Delete(file);
                        continue; // process next file
                    }
                    
                    // If we reach here, either "payment" property was missing or deserialization resulted in null.
                    _logger.LogWarning("Failed to deserialize JSON in {FileName}; file was retained for retry.", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing payment file {FileName}", fileName);
                    _logger.LogWarning("File {FileName} was retained for retry.", fileName);
                }
            }
        }

        private static string GetOutputFileName(Pain001GenerationRequest request)
        {
            var requestedFileName = string.IsNullOrWhiteSpace(request.OutputFileName)
                ? $"pain001_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.xml"
                : request.OutputFileName.Trim();
            var fileName = Path.GetFileName(requestedFileName);

            if (!fileName.Equals(requestedFileName, StringComparison.Ordinal)
                || !fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "OutputFileName must be a file name ending in .xml and must not contain a directory path.");
            }

            return fileName;
        }
    }
}
