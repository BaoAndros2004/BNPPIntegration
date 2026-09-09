namespace BNPPIntegration.BNPP.Payments.Domestic
{
    public sealed class DomesticRequest
    {
        public string MessageId { get; init; } = string.Empty;
        public DateTimeOffset CreationDateTime { get; init; }
        public DomesticParty? InitiatingParty { get; init; } = new();
        public IReadOnlyList<DomesticPaymentInformation> Payments { get; init; } = Array.Empty<DomesticPaymentInformation>();
    }

    public sealed class DomesticPaymentInformation
    {
        public string? PaymentInformationId { get; init; }
        public bool? BatchBooking { get; init; }
        public string? InstructionPriority { get; init; }
        public DateOnly RequestedExecutionDate { get; init; }
        public DomesticParty? Debtor { get; init; } = new();
        public DomesticAccount? DebtorAccount { get; init; } = new();
        public string? DebtorAgentBic { get; init; }
        public string? DebtorAgentName { get; init; }
        public string? DebtorAgentCountry { get; init; }
        public string? ChargeBearer { get; init; }
        public IReadOnlyList<DomesticTransaction> Transactions { get; init; } = Array.Empty<DomesticTransaction>();
    }

    public sealed class DomesticTransaction
    {
        public string? InstructionId { get; init; }
        public string EndToEndId { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public string? CreditorAgentMemberId { get; init; }
        public string? CreditorAgentName { get; init; }
        public DomesticPostalAddress? CreditorAgentPostalAddress { get; init; }
        public DomesticParty Creditor { get; init; } = new();
        public DomesticAccount CreditorAccount { get; init; } = new();
        public string? RemittanceInformation { get; init; }
    }

    public sealed class DomesticParty
    {
        public string Name { get; init; } = string.Empty;
        public string? BicOrBei { get; init; }
        public DomesticPostalAddress? PostalAddress { get; init; }
    }

    public sealed class DomesticPostalAddress
    {
        public string? PostalCode { get; init; }
        public string? TownName { get; init; }
        public string? Country { get; init; }
        public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();
    }

    public sealed class DomesticAccount
    {
        public DomesticAccountIdentificationType IdentificationType { get; init; }
        public string Identification { get; init; } = string.Empty;
        public string? Currency { get; init; }
    }

    public enum DomesticAccountIdentificationType
    {
        Iban = 0,
        Other = 1
    }
}


