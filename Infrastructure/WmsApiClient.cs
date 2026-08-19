using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BNPPIntegration.BNPP.BankReports.FSR;
using BNPPIntegration.BNPP.BankReports.FSR.Models;
using BNPPIntegration.BNPP.BankReports.MT940;
using BNPPIntegration.BNPP.BankReports.MT940.Models;
using BNPPIntegration.BNPP.BankReports.MT942;
using BNPPIntegration.BNPP.BankReports.MT942.Models;
using BNPPIntegration.BNPP.BankReports.PSR;
using BNPPIntegration.BNPP.BankReports.PSR.Models;

namespace BNPPIntegration.Infrastructure
{
    public sealed class WmsApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly FSRMapper _fsrMapper;
        private readonly PSRMapper _psrMapper;
        private readonly MT940Mapper _mt940Mapper;
        private readonly MT942Mapper _mt942Mapper;
        private readonly IConfiguration _configuration;

        public WmsApiClient(
            HttpClient httpClient,
            FSRMapper fsrMapper,
            PSRMapper psrMapper,
            MT940Mapper mt940Mapper,
            MT942Mapper mt942Mapper,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _fsrMapper = fsrMapper;
            _psrMapper = psrMapper;
            _mt940Mapper = mt940Mapper;
            _mt942Mapper = mt942Mapper;
            _configuration = configuration;
        }

        public Task SaveFsrToUd26Async(FSRReport report, CancellationToken cancellationToken = default)
        {
            var exportData = _fsrMapper.Map(report);
            return SaveBankReportAsync(new BankReportRequest
            {
                ReportType = "FSR",
                SourceName = report.SourceName,
                MessageId = exportData.MessageId,
                OriginalMessageId = exportData.OriginalMessageId,
                Status = exportData.GroupStatus,
                TransactionCount = ParseInt(exportData.OriginalNumberOfTransactions),
                Amount = ParseDecimal(exportData.OriginalControlSum),
                PayloadJson = JsonSerializer.Serialize(exportData, JsonOptions)
            }, cancellationToken);
        }

        public Task SavePsrToUd26Async(PSRReport report, CancellationToken cancellationToken = default)
        {
            var exportData = _psrMapper.Map(report);
            return SaveBankReportAsync(new BankReportRequest
            {
                ReportType = "PSR",
                SourceName = report.SourceName,
                MessageId = exportData.MessageId,
                OriginalMessageId = exportData.OriginalMessageId,
                Status = exportData.GroupStatus,
                TransactionCount = ParseInt(exportData.OriginalNumberOfTransactions),
                Amount = ParseDecimal(exportData.OriginalControlSum),
                PayloadJson = JsonSerializer.Serialize(exportData, JsonOptions)
            }, cancellationToken);
        }

        public Task SaveMt940ToUd26Async(MT940Report report, CancellationToken cancellationToken = default)
        {
            var exportData = _mt940Mapper.Map(report);
            var firstStatement = exportData.Statements.FirstOrDefault();
            return SaveBankReportAsync(new BankReportRequest
            {
                ReportType = "MT940",
                SourceName = report.SourceName,
                MessageId = firstStatement?.TransactionReference ?? "",
                OriginalMessageId = firstStatement?.RelatedReference ?? "",
                Status = "PARSED",
                TransactionCount = exportData.Statements.Sum(statement => statement.Transactions.Count),
                Amount = exportData.Statements.Sum(statement => ParseDecimal(statement.ClosingBalance.Amount)),
                Currency = firstStatement?.ClosingBalance.Currency ?? "",
                PayloadJson = JsonSerializer.Serialize(exportData, JsonOptions)
            }, cancellationToken);
        }

        public Task SaveMt942ToUd26Async(MT942Report report, CancellationToken cancellationToken = default)
        {
            var exportData = _mt942Mapper.Map(report);
            var firstStatement = exportData.Statements.FirstOrDefault();
            return SaveBankReportAsync(new BankReportRequest
            {
                ReportType = "MT942",
                SourceName = report.SourceName,
                MessageId = firstStatement?.TransactionReference ?? "",
                OriginalMessageId = firstStatement?.RelatedReference ?? "",
                Status = "PARSED",
                TransactionCount = exportData.Statements.Sum(statement => statement.Transactions.Count),
                Amount = exportData.Statements.Sum(statement => statement.Transactions.Sum(transaction => ParseDecimal(transaction.Amount))),
                Currency = firstStatement?.Transactions.FirstOrDefault()?.Currency ?? "",
                PayloadJson = JsonSerializer.Serialize(exportData, JsonOptions)
            }, cancellationToken);
        }

        private async Task SaveBankReportAsync(
            BankReportRequest request,
            CancellationToken cancellationToken)
        {
            var wmsApiBaseUrl = _configuration["Wms:BaseUrl"];
            if (string.IsNullOrWhiteSpace(wmsApiBaseUrl))
            {
                throw new ArgumentException("WMS API base URL is required.", nameof(wmsApiBaseUrl));
            }

            var endpoint = new Uri(new Uri(wmsApiBaseUrl.TrimEnd('/') + "/"), "Payment/SaveBankReport");
            var userName = _configuration["Wms:UserName"];
            var password = _configuration["Wms:Password"];
            var apiKey = _configuration["Wms:ApiKey"];
            if (string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrWhiteSpace(password)
                || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("WMS API credentials are required.");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{userName}:{password}")));
            httpRequest.Headers.Add("X-API-Key", apiKey);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private static decimal ParseDecimal(string? value)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private sealed class BankReportRequest
        {
            public string ReportType { get; init; } = string.Empty;
            public string SourceName { get; init; } = string.Empty;
            public string MessageId { get; init; } = string.Empty;
            public string OriginalMessageId { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public int TransactionCount { get; init; }
            public decimal Amount { get; init; }
            public string Currency { get; init; } = string.Empty;
            public string PayloadJson { get; init; } = string.Empty;
        }
    }
}
