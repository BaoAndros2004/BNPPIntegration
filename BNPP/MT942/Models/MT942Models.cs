namespace BNPPIntegration.BNPP.MT942.Models
{
    public sealed class MT942RawMessage
    {
        public string SourceName { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;
    }

    public sealed class MT942Report
    {
        public string SourceName { get; init; } = string.Empty;

        public IReadOnlyList<MT942Statement> Statements { get; init; } = Array.Empty<MT942Statement>();
    }

    public sealed class MT942Statement
    {
        public MT942BasicHeader BasicHeader { get; init; } = new();

        public MT942ApplicationHeader ApplicationHeader { get; init; } = new();

        public string TransactionReference { get; init; } = string.Empty;

        public string? RelatedReference { get; init; }

        public string AccountIdentification { get; init; } = string.Empty;

        public int StatementNumber { get; init; }

        public int? SequenceNumber { get; init; }

        public IReadOnlyList<MT942FloorLimit> FloorLimits { get; init; } = Array.Empty<MT942FloorLimit>();

        public DateTimeOffset? ReportDateTime { get; init; }

        public IReadOnlyList<MT942Transaction> Transactions { get; init; } = Array.Empty<MT942Transaction>();

        public MT942EntrySummary? DebitSummary { get; init; }

        public MT942EntrySummary? CreditSummary { get; init; }

        public string Trailer { get; init; } = string.Empty;
    }

    public sealed class MT942BasicHeader
    {
        public string ApplicationIdentifier { get; init; } = string.Empty;

        public string ServiceIdentifier { get; init; } = string.Empty;

        public string BankAddress { get; init; } = string.Empty;

        public string SessionNumber { get; init; } = string.Empty;

        public string SequenceNumber { get; init; } = string.Empty;
    }

    public sealed class MT942ApplicationHeader
    {
        public string Direction { get; init; } = string.Empty;

        public string MessageType { get; init; } = string.Empty;

        public string BankAddress { get; init; } = string.Empty;

        public string Priority { get; init; } = string.Empty;

        public string RawValue { get; init; } = string.Empty;
    }

    public sealed class MT942FloorLimit
    {
        public string Currency { get; init; } = string.Empty;

        public string? CreditDebitIndicator { get; init; }

        public decimal Amount { get; init; }
    }

    public sealed class MT942EntrySummary
    {
        public string EntryType { get; init; } = string.Empty;

        public int NumberOfEntries { get; init; }

        public string Currency { get; init; } = string.Empty;

        public decimal Amount { get; init; }
    }

    public sealed class MT942Transaction
    {
        public DateOnly? ValueDate { get; init; }

        public DateOnly? EntryDate { get; init; }

        public string CreditDebitIndicator { get; init; } = string.Empty;

        public string? FundsCode { get; init; }

        public decimal Amount { get; init; }

        public string TransactionTypeCode { get; init; } = string.Empty;

        public string CustomerReference { get; init; } = string.Empty;

        public string? BankReference { get; init; }

        public string? SupplementaryDetails { get; init; }

        public MT942TransactionDetails? Details { get; init; }
    }

    public sealed class MT942TransactionDetails
    {
        public string RawInformation { get; init; } = string.Empty;

        public string? TypeCode { get; init; }

        public string? Description { get; init; }

        public string? BeneficiaryInformation { get; init; }

        public string? RemittanceInformation { get; init; }

        public decimal? ExchangeRate { get; init; }

        public string? OriginalAmountCurrency { get; init; }

        public decimal? OriginalAmount { get; init; }
    }

    public sealed class MT942ParseResult
    {
        private MT942ParseResult(MT942Report? report, IReadOnlyList<string> errors)
        {
            Report = report;
            Errors = errors;
        }

        public MT942Report? Report { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool IsSuccess => Report is not null && Errors.Count == 0;

        public static MT942ParseResult Success(MT942Report report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new MT942ParseResult(report, Array.Empty<string>());
        }

        public static MT942ParseResult Failure(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            return new MT942ParseResult(null, errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray());
        }
    }

    public sealed class MT942ValidationResult
    {
        public MT942ValidationResult(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
        }

        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;
    }

    public sealed class MT942ExportData
    {
        public IReadOnlyList<MT942ExportStatement> Statements { get; init; } = Array.Empty<MT942ExportStatement>();
    }

    public sealed class MT942ExportStatement
    {
        public string SenderBankAddress { get; init; } = string.Empty;

        public string ReceiverBankAddress { get; init; } = string.Empty;

        public string TransactionReference { get; init; } = string.Empty;

        public string? RelatedReference { get; init; }

        public string AccountIdentification { get; init; } = string.Empty;

        public string StatementNumber { get; init; } = string.Empty;

        public string? SequenceNumber { get; init; }

        public IReadOnlyList<MT942ExportFloorLimit> FloorLimits { get; init; } = Array.Empty<MT942ExportFloorLimit>();

        public string ReportDateTime { get; init; } = string.Empty;

        public IReadOnlyList<MT942ExportTransaction> Transactions { get; init; } = Array.Empty<MT942ExportTransaction>();

        public MT942ExportEntrySummary? DebitSummary { get; init; }

        public MT942ExportEntrySummary? CreditSummary { get; init; }
    }

    public sealed class MT942ExportFloorLimit
    {
        public string Currency { get; init; } = string.Empty;

        public string? CreditDebitIndicator { get; init; }

        public string Amount { get; init; } = string.Empty;
    }

    public sealed class MT942ExportEntrySummary
    {
        public string EntryType { get; init; } = string.Empty;

        public string NumberOfEntries { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public string Amount { get; init; } = string.Empty;
    }

    public sealed class MT942ExportTransaction
    {
        public string ValueDate { get; init; } = string.Empty;

        public string? EntryDate { get; init; }

        public string CreditDebitIndicator { get; init; } = string.Empty;

        public string? FundsCode { get; init; }

        public string Amount { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public string TransactionTypeCode { get; init; } = string.Empty;

        public string CustomerReference { get; init; } = string.Empty;

        public string? BankReference { get; init; }

        public string? SupplementaryDetails { get; init; }

        public MT942ExportTransactionDetails? Details { get; init; }
    }

    public sealed class MT942ExportTransactionDetails
    {
        public string RawInformation { get; init; } = string.Empty;

        public string? TypeCode { get; init; }

        public string? Description { get; init; }

        public string? BeneficiaryInformation { get; init; }

        public string? RemittanceInformation { get; init; }

        public string? ExchangeRate { get; init; }

        public string? OriginalAmountCurrency { get; init; }

        public string? OriginalAmount { get; init; }
    }
}
