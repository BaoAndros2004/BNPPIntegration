namespace BNPPIntegration.BNPP.Pain001.Models
{
    public sealed class Pain001GenerationRequest
    {
        public string? OutputFileName { get; init; }
        public Pain001Request Payment { get; init; } = new();
    }

    public sealed class Pain001Request
    {
        public string MessageId { get; init; } = string.Empty;
        public DateTimeOffset CreationDateTime { get; init; }
        public Pain001Party InitiatingParty { get; init; } = new();
        public IReadOnlyList<Pain001PaymentInformation> Payments { get; init; } =
            Array.Empty<Pain001PaymentInformation>();
    }

    public sealed class Pain001PaymentInformation
    {
        public string PaymentInformationId { get; init; } = string.Empty;
        public bool? BatchBooking { get; init; }
        public string? ServiceLevelCode { get; init; }
        public DateOnly RequestedExecutionDate { get; init; }
        public Pain001Party Debtor { get; init; } = new();
        public Pain001Account DebtorAccount { get; init; } = new();
        public string DebtorAgentBic { get; init; } = string.Empty;
        public string ChargeBearer { get; init; } = "SHAR";
        public IReadOnlyList<Pain001Transaction> Transactions { get; init; } =
            Array.Empty<Pain001Transaction>();
    }

    public sealed class Pain001Transaction
    {
        public string? InstructionId { get; init; }
        public string EndToEndId { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public string? CreditorAgentBic { get; init; }
        public Pain001Party Creditor { get; init; } = new();
        public Pain001Account CreditorAccount { get; init; } = new();
        public string? RemittanceInformation { get; init; }
    }

    public sealed class Pain001Party
    {
        public string Name { get; init; } = string.Empty;
        public string? BicOrBei { get; init; }
        public Pain001PostalAddress? PostalAddress { get; init; }
    }

    public sealed class Pain001PostalAddress
    {
        public string? PostalCode { get; init; }
        public string? TownName { get; init; }
        public string? Country { get; init; }
        public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();
    }

    public sealed class Pain001Account
    {
        public Pain001AccountIdentificationType IdentificationType { get; init; }
        public string Identification { get; init; } = string.Empty;
        public string? Currency { get; init; }
    }

    public enum Pain001AccountIdentificationType
    {
        Iban = 0,
        Other = 1
    }
}
