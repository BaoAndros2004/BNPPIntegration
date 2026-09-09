namespace BNPPIntegration.BNPP.Payments.International
{
    public sealed class InternationalRequest
    {
        public string MessageId { get; init; } = string.Empty;
        public DateTimeOffset CreationDateTime { get; init; }
        public InternationalParty? InitiatingParty { get; init; } = new();
        public IReadOnlyList<InternationalPaymentInformation> Payments { get; init; } = Array.Empty<InternationalPaymentInformation>();
    }

    public sealed class InternationalPaymentInformation
    {
        public string? PaymentInformationId { get; init; }
        public bool? BatchBooking { get; init; }
        public string? InstructionPriority { get; init; }
        public DateOnly RequestedExecutionDate { get; init; }
        public InternationalParty? Debtor { get; init; } = new();
        public InternationalAccount? DebtorAccount { get; init; } = new();
        public string? DebtorAgentBic { get; init; }
        public string? DebtorAgentName { get; init; }
        public string? DebtorAgentCountry { get; init; }
        public string? ChargeBearer { get; init; }
        public IReadOnlyList<InternationalTransaction> Transactions { get; init; } = Array.Empty<InternationalTransaction>();
    }

    public sealed class InternationalTransaction
    {
        public string? InstructionId { get; init; }
        public string EndToEndId { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public string? CreditorAgentBic { get; init; }
        public string? CreditorAgentName { get; init; }
        public InternationalPostalAddress? CreditorAgentPostalAddress { get; init; }
        public InternationalParty Creditor { get; init; } = new();
        public InternationalAccount CreditorAccount { get; init; } = new();
        public string? RemittanceInformation { get; init; }
    }

    public sealed class InternationalParty
    {
        public string Name { get; init; } = string.Empty;
        public string? BicOrBei { get; init; }
        public InternationalPostalAddress? PostalAddress { get; init; }
    }

    public sealed class InternationalPostalAddress
    {
        public string? StreetName { get; init; }
        public string? BuildingNumber { get; init; }
        public string? PostalCode { get; init; }
        public string? TownName { get; init; }
        public string? Country { get; init; }
        public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();
    }

    public sealed class InternationalAccount
    {
        public InternationalAccountIdentificationType IdentificationType { get; init; }
        public string Identification { get; init; } = string.Empty;
        public string? Currency { get; init; }
    }

    public enum InternationalAccountIdentificationType
    {
        Iban = 0,
        Other = 1
    }
}

