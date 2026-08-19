using BNPPIntegration.BNPP.BankReports.FSR;
using BNPPIntegration.BNPP.BankReports.MT940;
using BNPPIntegration.BNPP.BankReports.MT942;
using BNPPIntegration.BNPP.BankReports.PSR;
using BNPPIntegration.Infrastructure;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BNPPIntegration.Workers
{
    public sealed class BankReportWorker : BackgroundService
    {
        private readonly ILogger<BankReportWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        public BankReportWorker(
            ILogger<BankReportWorker> logger,
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var bankReportDirectory = Path.Combine(AppContext.BaseDirectory, "bank-reports");
            var intervalMinutes = _configuration.GetValue<int>("BackgroundProcessing:BankReportIntervalMinutes");
            if (intervalMinutes <= 0)
                throw new InvalidOperationException("BackgroundProcessing:BankReportIntervalMinutes must be greater than 0.");

            var processingInterval = TimeSpan.FromMinutes(intervalMinutes);
            
            Directory.CreateDirectory(bankReportDirectory);

            _logger.LogInformation(
                "Bank report worker started. Input directory: {BankReportDirectory}. Processing interval: {IntervalMinutes} minute(s)",
                bankReportDirectory,
                intervalMinutes);

            // Run immediately when the host starts. The configured interval is
            // the wait between subsequent runs, not an initial startup delay.
            await RunProcessingCycleAsync(
                bankReportDirectory,
                stoppingToken);

            using var timer = new PeriodicTimer(processingInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunProcessingCycleAsync(
                    bankReportDirectory,
                    stoppingToken);
            }
        }

        private async Task RunProcessingCycleAsync(
            string bankReportDirectory,
            CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Bank report processing cycle started.");
                await ProcessFilesAsync(bankReportDirectory, stoppingToken);
                _logger.LogInformation("Bank report processing cycle completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing bank report files.");
            }
        }

        private async Task ProcessFilesAsync(string bankReportDirectory, CancellationToken stoppingToken)
        {
            var files = Directory.GetFiles(bankReportDirectory);
            if (files.Length == 0)
                return;

            using var scope = _scopeFactory.CreateScope();
            var wmsApiClient = scope.ServiceProvider.GetRequiredService<WmsApiClient>();
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
                    switch (DetectReportType(content))
                    {
                        case BankReportType.Fsr:
                            var fsrResult = fsrParser.Parse(content, fileName);
                            if (fsrResult.IsSuccess && fsrResult.Report != null)
                            {
                                await wmsApiClient.SaveFsrToUd26Async(fsrResult.Report, stoppingToken);
                                success = true;
                            }
                            break;

                        case BankReportType.Psr:
                            var psrResult = psrParser.Parse(content, fileName);
                            if (psrResult.IsSuccess && psrResult.Report != null)
                            {
                                await wmsApiClient.SavePsrToUd26Async(psrResult.Report, stoppingToken);
                                success = true;
                            }
                            break;

                        case BankReportType.Mt940:
                            var mt940Result = mt940Parser.Parse(content, fileName);
                            if (mt940Result.IsSuccess && mt940Result.Report != null)
                            {
                                await wmsApiClient.SaveMt940ToUd26Async(mt940Result.Report, stoppingToken);
                                success = true;
                            }
                            break;

                        case BankReportType.Mt942:
                            var mt942Result = mt942Parser.Parse(content, fileName);
                            if (mt942Result.IsSuccess && mt942Result.Report != null)
                            {
                                await wmsApiClient.SaveMt942ToUd26Async(mt942Result.Report, stoppingToken);
                                success = true;
                            }
                            break;

                        default:
                            _logger.LogWarning("Could not determine report type from the content of {FileName}", fileName);
                            break;
                    }
                    
                    if (success)
                    {
                        _logger.LogInformation("Successfully processed file {FileName}", fileName);
                        File.Delete(file);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse file {FileName}", fileName);
                        _logger.LogWarning("File {FileName} was retained for retry.", fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file {FileName}", fileName);
                    _logger.LogWarning("File {FileName} was retained for retry.", fileName);
                }
            }
        }

        private static BankReportType DetectReportType(string content)
        {
            if (content.Contains("pain.002.001.03", StringComparison.OrdinalIgnoreCase))
            {
                var document = XDocument.Parse(content);
                var statusReport = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "CstmrPmtStsRpt");
                if (statusReport is null)
                {
                    return BankReportType.Unknown;
                }

                if (statusReport.Descendants().Any(element =>
                        element.Name.LocalName is "OrgnlPmtInfAndSts" or "TxInfAndSts"))
                {
                    return BankReportType.Psr;
                }

                var messageId = statusReport.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "GrpHdr")?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "MsgId")?
                    .Value;

                return messageId?.StartsWith("PSR", StringComparison.OrdinalIgnoreCase) == true
                    ? BankReportType.Psr
                    : BankReportType.Fsr;
            }

            if (!content.Contains("{1:", StringComparison.Ordinal))
            {
                return BankReportType.Unknown;
            }

            var tags = Regex.Matches(content, @"(?m)^:(\d{2}[A-Z]?):")
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tags.Overlaps(new[] { "34F", "13D", "90D", "90C" }))
            {
                return BankReportType.Mt942;
            }

            if (tags.Overlaps(new[] { "60F", "60M", "62F", "62M", "64", "65" }))
            {
                return BankReportType.Mt940;
            }

            return BankReportType.Unknown;
        }

        private enum BankReportType
        {
            Unknown,
            Fsr,
            Psr,
            Mt940,
            Mt942
        }
    }
}
