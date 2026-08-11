using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BNPPIntegration.BNPP.Configuration;
using Microsoft.Extensions.Options;
using BNPPIntegration.BNPP.FSR;
using BNPPIntegration.BNPP.FSR.Models;
using BNPPIntegration.BNPP.MT940;
using BNPPIntegration.BNPP.MT940.Models;
using BNPPIntegration.BNPP.MT942;
using BNPPIntegration.BNPP.MT942.Models;
using BNPPIntegration.BNPP.PSR;
using BNPPIntegration.BNPP.PSR.Models;

namespace BNPPIntegration.BNPP
{
    public sealed class BNPPService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly FSRMapper _fsrMapper;
        private readonly PSRMapper _psrMapper;
        private readonly MT940Mapper _mt940Mapper;
        private readonly MT942Mapper _mt942Mapper;
        private readonly IOptions<WmsIntegrationOptions> _wmsOptions;

        public BNPPService(
            HttpClient httpClient,
            FSRMapper fsrMapper,
            PSRMapper psrMapper,
            MT940Mapper mt940Mapper,
            MT942Mapper mt942Mapper,
            IOptions<WmsIntegrationOptions> wmsOptions)
        {
            _httpClient = httpClient;
            _fsrMapper = fsrMapper;
            _psrMapper = psrMapper;
            _mt940Mapper = mt940Mapper;
            _mt942Mapper = mt942Mapper;
            _wmsOptions = wmsOptions;
        }

        public Task SaveFsrToUd26Async(FSRReport report, CancellationToken cancellationToken = default)
        {
            var exportData = _fsrMapper.Map(report);
            return SaveInboundReportAsync(new BnppInboundReportRequest
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
            return SaveInboundReportAsync(new BnppInboundReportRequest
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
            return SaveInboundReportAsync(new BnppInboundReportRequest
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
            return SaveInboundReportAsync(new BnppInboundReportRequest
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

        private async Task SaveInboundReportAsync(
            BnppInboundReportRequest request,
            CancellationToken cancellationToken)
        {
            var wmsApiBaseUrl = _wmsOptions.Value.BaseUrl;
            if (string.IsNullOrWhiteSpace(wmsApiBaseUrl))
            {
                throw new ArgumentException("WMS API base URL is required.", nameof(wmsApiBaseUrl));
            }

            var endpoint = new Uri(new Uri(wmsApiBaseUrl.TrimEnd('/') + "/"), "Bnpp/SaveInboundReport");
            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
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

        private sealed class BnppInboundReportRequest
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
