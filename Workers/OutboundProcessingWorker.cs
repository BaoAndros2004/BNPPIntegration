using System.Text.Json;
using BNPPIntegration.BNPP.Configuration;
using BNPPIntegration.BNPP.Pain001;
using BNPPIntegration.BNPP.Pain001.Models;
using Microsoft.Extensions.Options;

namespace BNPPIntegration.Workers
{
    public sealed class OutboundProcessingWorker : BackgroundService
    {
        private readonly ILogger<OutboundProcessingWorker> _logger;
        private readonly IOptions<ProcessingStorageOptions> _storageOptions;
        private readonly Pain001XmlGenerator _xmlGenerator;

        public OutboundProcessingWorker(
            ILogger<OutboundProcessingWorker> logger,
            IOptions<ProcessingStorageOptions> storageOptions,
            Pain001XmlGenerator xmlGenerator)
        {
            _logger = logger;
            _storageOptions = storageOptions;
            _xmlGenerator = xmlGenerator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var outboundRoot = string.IsNullOrWhiteSpace(_storageOptions.Value.OutboundRootDirectory) ? @"D:\BNPPIntegration\outbound" : _storageOptions.Value.OutboundRootDirectory;
            var outboundDirectory = Path.GetFullPath(outboundRoot);
            var processedDirectory = Path.Combine(outboundDirectory, "Processed");
            var errorDirectory = Path.Combine(outboundDirectory, "Error");
            var outputXmlDirectory = Path.Combine(outboundDirectory, "Export");

            Directory.CreateDirectory(outboundDirectory);
            Directory.CreateDirectory(processedDirectory);
            Directory.CreateDirectory(errorDirectory);
            Directory.CreateDirectory(outputXmlDirectory);

            _logger.LogInformation("Outbound worker started. Storage root: {OutboundDirectory}", outboundDirectory);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboundFilesAsync(outboundDirectory, processedDirectory, errorDirectory, outputXmlDirectory, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing outbound files.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task ProcessOutboundFilesAsync(string outboundDir, string processedDir, string errorDir, string outputXmlDir, CancellationToken stoppingToken)
        {
            var files = Directory.GetFiles(outboundDir, "*.json");
            if (files.Length == 0)
                return;

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var content = await File.ReadAllTextAsync(file, stoppingToken);
                    
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    using var document = JsonDocument.Parse(content);
                    if (document.RootElement.TryGetProperty("payment", out var paymentElement))
                    {
                        var request = paymentElement.Deserialize<Pain001Request>(options);
                        if (request != null)
                        {
                            var xmlFileName = $"pain001_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.xml";
                            var xmlFilePath = Path.Combine(outputXmlDir, xmlFileName);

                            await _xmlGenerator.GenerateFileAsync(request, xmlFilePath, stoppingToken);
                            
                            _logger.LogInformation("Successfully generated XML {XmlFileName} from {FileName}", xmlFileName, fileName);
                            
                            // Auto-delete the JSON file after processing
                            File.Delete(file);
                            continue; // process next file
                        }
                    }
                    
                    // If we reach here, either "payment" property was missing or deserialization resulted in null.
                    _logger.LogWarning("Failed to deserialize JSON in {FileName}", fileName);
                    File.Move(file, Path.Combine(errorDir, fileName), overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing outbound file {FileName}", fileName);
                    File.Move(file, Path.Combine(errorDir, fileName), overwrite: true);
                }
            }
        }
    }
}
