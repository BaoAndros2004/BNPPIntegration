using BNPPIntegration.BNPP;
using BNPPIntegration.BNPP.Configuration;
using BNPPIntegration.BNPP.FSR;
using BNPPIntegration.BNPP.MT940;
using BNPPIntegration.BNPP.MT942;
using BNPPIntegration.BNPP.PSR;
using Microsoft.Extensions.Options;

namespace BNPPIntegration.Workers
{
    public sealed class InboundProcessingWorker : BackgroundService
    {
        private readonly ILogger<InboundProcessingWorker> _logger;
        private readonly IOptions<ProcessingStorageOptions> _storageOptions;
        private readonly IServiceScopeFactory _scopeFactory;

        public InboundProcessingWorker(
            ILogger<InboundProcessingWorker> logger,
            IOptions<ProcessingStorageOptions> storageOptions,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _storageOptions = storageOptions;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var inboundRoot = string.IsNullOrWhiteSpace(_storageOptions.Value.InboundRootDirectory) ? @"D:\BNPPIntegration\inbound" : _storageOptions.Value.InboundRootDirectory;
            var inboundDirectory = Path.GetFullPath(inboundRoot);
            var processedDirectory = Path.Combine(inboundDirectory, "Processed");
            var errorDirectory = Path.Combine(inboundDirectory, "Error");
            
            Directory.CreateDirectory(inboundDirectory);
            Directory.CreateDirectory(processedDirectory);
            Directory.CreateDirectory(errorDirectory);

            _logger.LogInformation("Inbound worker started. Storage root: {InboundDirectory}", inboundDirectory);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessFilesAsync(inboundDirectory, processedDirectory, errorDirectory, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing inbound files.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task ProcessFilesAsync(string inboundDir, string processedDir, string errorDir, CancellationToken stoppingToken)
        {
            var files = Directory.GetFiles(inboundDir);
            if (files.Length == 0)
                return;

            using var scope = _scopeFactory.CreateScope();
            var bnppService = scope.ServiceProvider.GetRequiredService<BNPPService>();
            var fsrParser = scope.ServiceProvider.GetRequiredService<FSRParser>();
            var psrParser = scope.ServiceProvider.GetRequiredService<PSRParser>();
            var mt940Parser = scope.ServiceProvider.GetRequiredService<MT940Parser>();
            var mt942Parser = scope.ServiceProvider.GetRequiredService<MT942Parser>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var content = await File.ReadAllTextAsync(file, stoppingToken);
                bool success = false;

                try
                {
                    if (content.Contains("pain.002.001.03", StringComparison.OrdinalIgnoreCase))
                    {
                        var fsrResult = fsrParser.Parse(content, fileName);
                        if (fsrResult.IsSuccess && fsrResult.Report != null)
                        {
                            await bnppService.SaveFsrToUd26Async(fsrResult.Report, stoppingToken);
                            success = true;
                        }
                        else
                        {
                            var psrResult = psrParser.Parse(content, fileName);
                            if (psrResult.IsSuccess && psrResult.Report != null)
                            {
                                await bnppService.SavePsrToUd26Async(psrResult.Report, stoppingToken);
                                success = true;
                            }
                        }
                    }
                    else if (content.Contains("{1:", StringComparison.OrdinalIgnoreCase))
                    {
                        var mt940Result = mt940Parser.Parse(content, fileName);
                        if (mt940Result.IsSuccess && mt940Result.Report != null)
                        {
                            await bnppService.SaveMt940ToUd26Async(mt940Result.Report, stoppingToken);
                            success = true;
                        }
                        else
                        {
                            var mt942Result = mt942Parser.Parse(content, fileName);
                            if (mt942Result.IsSuccess && mt942Result.Report != null)
                            {
                                await bnppService.SaveMt942ToUd26Async(mt942Result.Report, stoppingToken);
                                success = true;
                            }
                        }
                    }
                    
                    if (success)
                    {
                        _logger.LogInformation("Successfully processed file {FileName}", fileName);
                        File.Move(file, Path.Combine(processedDir, fileName), overwrite: true);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse file {FileName}", fileName);
                        File.Move(file, Path.Combine(errorDir, fileName), overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file {FileName}", fileName);
                    File.Move(file, Path.Combine(errorDir, fileName), overwrite: true);
                }
            }
        }
    }
}
