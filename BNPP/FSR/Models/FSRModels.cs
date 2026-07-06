namespace BNPPIntegration.BNPP.FSR.Models
{
    public sealed class FSRRawMessage
    {
        public string SourceName { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;
    }

    public sealed class FSRReport
    {
        public string SourceName { get; init; } = string.Empty;

        public string MessageId { get; init; } = string.Empty;

        public DateTime? CreationDateTime { get; init; }

        public string? InitiatingPartyBic { get; init; }

        public string OriginalMessageId { get; init; } = string.Empty;

        public string OriginalMessageNameId { get; init; } = string.Empty;

        public long? OriginalNumberOfTransactions { get; init; }

        public decimal? OriginalControlSum { get; init; }

        public string GroupStatus { get; init; } = string.Empty;

        public IReadOnlyList<FSRStatusReason> StatusReasons { get; init; } = Array.Empty<FSRStatusReason>();
    }

    public sealed class FSRStatusReason
    {
        public string? Code { get; init; }

        public IReadOnlyList<string> AdditionalInformation { get; init; } = Array.Empty<string>();
    }

    public sealed class FSRParseResult
    {
        private FSRParseResult(FSRReport? report, IReadOnlyList<string> errors)
        {
            Report = report;
            Errors = errors;
        }

        public FSRReport? Report { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool IsSuccess => Report is not null && Errors.Count == 0;

        public static FSRParseResult Success(FSRReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new FSRParseResult(report, Array.Empty<string>());
        }

        public static FSRParseResult Failure(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            return new FSRParseResult(null, errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray());
        }
    }

    public sealed class FSRValidationResult
    {
        public FSRValidationResult(IEnumerable<string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
        }

        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;
    }

    public sealed class FSRExportData
    {
        public string MessageId { get; init; } = string.Empty;

        public string CreationDateTime { get; init; } = string.Empty;

        public string? InitiatingPartyBic { get; init; }

        public string OriginalMessageId { get; init; } = string.Empty;

        public string OriginalMessageNameId { get; init; } = string.Empty;

        public string? OriginalNumberOfTransactions { get; init; }

        public string? OriginalControlSum { get; init; }

        public string GroupStatus { get; init; } = string.Empty;

        public IReadOnlyList<FSRExportStatusReason> StatusReasons { get; init; } = Array.Empty<FSRExportStatusReason>();
    }

    public sealed class FSRExportStatusReason
    {
        public string? Code { get; init; }

        public IReadOnlyList<string> AdditionalInformation { get; init; } = Array.Empty<string>();
    }
}
