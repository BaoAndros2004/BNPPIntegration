namespace BNPPIntegration.BNPP.PSR.Models
{
    public sealed class PSRRawMessage
    {
        public string SourceName { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;
    }

    public sealed class PSRReport
    {
        public string SourceName { get; init; } = string.Empty;

        public string MessageId { get; init; } = string.Empty;

        public DateTime? CreationDateTime { get; init; }

        public string? InitiatingPartyBic { get; init; }

        public string OriginalMessageId { get; init; } = string.Empty;

        public string OriginalMessageNameId { get; init; } = string.Empty;

        public DateTime? OriginalCreationDateTime { get; init; }

        public long? OriginalNumberOfTransactions { get; init; }

        public decimal? OriginalControlSum { get; init; }

        public string GroupStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRStatusReason> StatusReasons { get; init; } = Array.Empty<PSRStatusReason>();

        public IReadOnlyList<PSRTransactionStatusSummary> TransactionStatusSummaries { get; init; }
            = Array.Empty<PSRTransactionStatusSummary>();

        public IReadOnlyList<PSRPaymentInformation> PaymentInformation { get; init; }
            = Array.Empty<PSRPaymentInformation>();
    }

    public sealed class PSRPaymentInformation
    {
        public string OriginalPaymentInformationId { get; init; } = string.Empty;

        public long? OriginalNumberOfTransactions { get; init; }

        public decimal? OriginalControlSum { get; init; }

        public string PaymentInformationStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRStatusReason> StatusReasons { get; init; } = Array.Empty<PSRStatusReason>();

        public IReadOnlyList<PSRTransactionStatusSummary> TransactionStatusSummaries { get; init; }
            = Array.Empty<PSRTransactionStatusSummary>();

        public IReadOnlyList<PSRTransaction> Transactions { get; init; } = Array.Empty<PSRTransaction>();
    }

    public sealed class PSRTransaction
    {
        public string? StatusId { get; init; }

        public string? OriginalInstructionId { get; init; }

        public string? OriginalEndToEndId { get; init; }

        public string TransactionStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRStatusReason> StatusReasons { get; init; } = Array.Empty<PSRStatusReason>();

        public DateTime? AcceptanceDateTime { get; init; }

        public string? AccountServicerReference { get; init; }

        public string? ClearingSystemReference { get; init; }

        public PSROriginalTransactionReference? OriginalTransactionReference { get; init; }
    }

    public sealed class PSROriginalTransactionReference
    {
        public decimal? InstructedAmount { get; init; }

        public string? Currency { get; init; }

        public DateOnly? RequestedExecutionDate { get; init; }

        public PSRAccountIdentification? DebtorAccount { get; init; }

        public PSRAccountIdentification? CreditorAccount { get; init; }

        public IReadOnlyList<string> UnstructuredRemittanceInformation { get; init; } = Array.Empty<string>();
    }

    public sealed class PSRAccountIdentification
    {
        public string Type { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;
    }

    public sealed class PSRStatusReason
    {
        public string? Code { get; init; }

        public IReadOnlyList<string> AdditionalInformation { get; init; } = Array.Empty<string>();
    }

    public sealed class PSRTransactionStatusSummary
    {
        public string Status { get; init; } = string.Empty;

        public long NumberOfTransactions { get; init; }

        public decimal? ControlSum { get; init; }
    }

    public sealed class PSRParseResult
    {
        private PSRParseResult(PSRReport? report, IReadOnlyList<string> errors)
        {
            Report = report;
            Errors = errors;
        }

        public PSRReport? Report { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool IsSuccess => Report is not null && Errors.Count == 0;

        public static PSRParseResult Success(PSRReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new PSRParseResult(report, Array.Empty<string>());
        }

        public static PSRParseResult Failure(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            return new PSRParseResult(null, errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray());
        }
    }

    public sealed class PSRValidationResult
    {
        public PSRValidationResult(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
        }

        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;
    }

    public sealed class PSRExportData
    {
        public string MessageId { get; init; } = string.Empty;

        public string CreationDateTime { get; init; } = string.Empty;

        public string? InitiatingPartyBic { get; init; }

        public string OriginalMessageId { get; init; } = string.Empty;

        public string OriginalMessageNameId { get; init; } = string.Empty;

        public string? OriginalCreationDateTime { get; init; }

        public string? OriginalNumberOfTransactions { get; init; }

        public string? OriginalControlSum { get; init; }

        public string GroupStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRExportStatusReason> StatusReasons { get; init; }
            = Array.Empty<PSRExportStatusReason>();

        public IReadOnlyList<PSRExportTransactionStatusSummary> TransactionStatusSummaries { get; init; }
            = Array.Empty<PSRExportTransactionStatusSummary>();

        public IReadOnlyList<PSRExportPaymentInformation> PaymentInformation { get; init; }
            = Array.Empty<PSRExportPaymentInformation>();
    }

    public sealed class PSRExportPaymentInformation
    {
        public string OriginalPaymentInformationId { get; init; } = string.Empty;

        public string? OriginalNumberOfTransactions { get; init; }

        public string? OriginalControlSum { get; init; }

        public string PaymentInformationStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRExportStatusReason> StatusReasons { get; init; }
            = Array.Empty<PSRExportStatusReason>();

        public IReadOnlyList<PSRExportTransactionStatusSummary> TransactionStatusSummaries { get; init; }
            = Array.Empty<PSRExportTransactionStatusSummary>();

        public IReadOnlyList<PSRExportTransaction> Transactions { get; init; } = Array.Empty<PSRExportTransaction>();
    }

    public sealed class PSRExportTransaction
    {
        public string? StatusId { get; init; }

        public string? OriginalInstructionId { get; init; }

        public string? OriginalEndToEndId { get; init; }

        public string TransactionStatus { get; init; } = string.Empty;

        public IReadOnlyList<PSRExportStatusReason> StatusReasons { get; init; }
            = Array.Empty<PSRExportStatusReason>();

        public string? AcceptanceDateTime { get; init; }

        public string? AccountServicerReference { get; init; }

        public string? ClearingSystemReference { get; init; }

        public PSRExportOriginalTransactionReference? OriginalTransactionReference { get; init; }
    }

    public sealed class PSRExportOriginalTransactionReference
    {
        public string? InstructedAmount { get; init; }

        public string? Currency { get; init; }

        public string? RequestedExecutionDate { get; init; }

        public PSRAccountIdentification? DebtorAccount { get; init; }

        public PSRAccountIdentification? CreditorAccount { get; init; }

        public IReadOnlyList<string> UnstructuredRemittanceInformation { get; init; } = Array.Empty<string>();
    }

    public sealed class PSRExportStatusReason
    {
        public string? Code { get; init; }

        public IReadOnlyList<string> AdditionalInformation { get; init; } = Array.Empty<string>();
    }

    public sealed class PSRExportTransactionStatusSummary
    {
        public string Status { get; init; } = string.Empty;

        public string NumberOfTransactions { get; init; } = string.Empty;

        public string? ControlSum { get; init; }
    }
}
