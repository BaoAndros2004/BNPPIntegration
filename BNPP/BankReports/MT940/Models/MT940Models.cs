namespace BNPPIntegration.BNPP.BankReports.MT940.Models
{
    public sealed class MT940RawMessage
    {
        public string SourceName { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;
    }

    public sealed class MT940Report
    {
        public string SourceName { get; init; } = string.Empty;

        public IReadOnlyList<MT940Statement> Statements { get; init; } = Array.Empty<MT940Statement>();
    }

    public sealed class MT940Statement
    {
        public MT940BasicHeader BasicHeader { get; init; } = new();

        public MT940ApplicationHeader ApplicationHeader { get; init; } = new();

        public string TransactionReference { get; init; } = string.Empty;

        public string? RelatedReference { get; init; }

        public string AccountIdentification { get; init; } = string.Empty;

        public int StatementNumber { get; init; }

        public int? SequenceNumber { get; init; }

        public MT940Balance OpeningBalance { get; init; } = new();

        public IReadOnlyList<MT940Transaction> Transactions { get; init; } = Array.Empty<MT940Transaction>();

        public MT940Balance ClosingBalance { get; init; } = new();

        public MT940Balance? ClosingAvailableBalance { get; init; }

        public IReadOnlyList<MT940Balance> ForwardAvailableBalances { get; init; } = Array.Empty<MT940Balance>();

        public IReadOnlyList<string> InformationToAccountOwner { get; init; } = Array.Empty<string>();

        public string Trailer { get; init; } = string.Empty;
    }

    public sealed class MT940BasicHeader
    {
        public string ApplicationIdentifier { get; init; } = string.Empty;

        public string ServiceIdentifier { get; init; } = string.Empty;

        public string BankAddress { get; init; } = string.Empty;

        public string SessionNumber { get; init; } = string.Empty;

        public string SequenceNumber { get; init; } = string.Empty;
    }

    public sealed class MT940ApplicationHeader
    {
        public string Direction { get; init; } = string.Empty;

        public string MessageType { get; init; } = string.Empty;

        public string BankAddress { get; init; } = string.Empty;

        public string Priority { get; init; } = string.Empty;

        public string RawValue { get; init; } = string.Empty;
    }

    public sealed class MT940Balance
    {
        public string BalanceType { get; init; } = string.Empty;

        public string CreditDebitIndicator { get; init; } = string.Empty;

        public DateOnly? Date { get; init; }

        public string Currency { get; init; } = string.Empty;

        public decimal Amount { get; init; }
    }

    public sealed class MT940Transaction
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

        public MT940TransactionDetails? Details { get; init; }
    }

    public sealed class MT940TransactionDetails
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

    public sealed class MT940ParseResult
    {
        private MT940ParseResult(MT940Report? report, IReadOnlyList<string> errors)
        {
            Report = report;
            Errors = errors;
        }

        public MT940Report? Report { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool IsSuccess => Report is not null && Errors.Count == 0;

        public static MT940ParseResult Success(MT940Report report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new MT940ParseResult(report, Array.Empty<string>());
        }

        public static MT940ParseResult Failure(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            return new MT940ParseResult(null, errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray());
        }
    }

    public sealed class MT940ValidationResult
    {
        public MT940ValidationResult(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
        }

        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;
    }

    public sealed class MT940ExportData
    {
        public IReadOnlyList<MT940ExportStatement> Statements { get; init; } = Array.Empty<MT940ExportStatement>();
    }

    public sealed class MT940ExportStatement
    {
        public string SenderBankAddress { get; init; } = string.Empty;

        public string ReceiverBankAddress { get; init; } = string.Empty;

        public string TransactionReference { get; init; } = string.Empty;

        public string? RelatedReference { get; init; }

        public string AccountIdentification { get; init; } = string.Empty;

        public string StatementNumber { get; init; } = string.Empty;

        public string? SequenceNumber { get; init; }

        public MT940ExportBalance OpeningBalance { get; init; } = new();

        public IReadOnlyList<MT940ExportTransaction> Transactions { get; init; } = Array.Empty<MT940ExportTransaction>();

        public MT940ExportBalance ClosingBalance { get; init; } = new();

        public MT940ExportBalance? ClosingAvailableBalance { get; init; }

        public IReadOnlyList<MT940ExportBalance> ForwardAvailableBalances { get; init; }
            = Array.Empty<MT940ExportBalance>();

        public IReadOnlyList<string> InformationToAccountOwner { get; init; } = Array.Empty<string>();
    }

    public sealed class MT940ExportBalance
    {
        public string BalanceType { get; init; } = string.Empty;

        public string CreditDebitIndicator { get; init; } = string.Empty;

        public string Date { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public string Amount { get; init; } = string.Empty;
    }

    public sealed class MT940ExportTransaction
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

        public MT940ExportTransactionDetails? Details { get; init; }
    }

    public sealed class MT940ExportTransactionDetails
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
