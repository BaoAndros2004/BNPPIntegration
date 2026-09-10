namespace BNPPIntegration.BNPP.Payments.IntraCompany
{
    public sealed class IntraCompanyRequest
    {
        public string MessageId { get; init; } = string.Empty;
        public DateTimeOffset CreationDateTime { get; init; }
        public IntraCompanyParty? InitiatingParty { get; init; } = new();
        public IReadOnlyList<IntraCompanyPaymentInformation> Payments { get; init; } = Array.Empty<IntraCompanyPaymentInformation>();
    }

    public sealed class IntraCompanyPaymentInformation
    {
        public string? PaymentInformationId { get; init; }
        public bool? BatchBooking { get; init; }
        public string? InstructionPriority { get; init; }
        public DateOnly RequestedExecutionDate { get; init; }
        public IntraCompanyParty? Debtor { get; init; } = new();
        public IntraCompanyAccount? DebtorAccount { get; init; } = new();
        public string? DebtorAgentBic { get; init; }
        public string? DebtorAgentName { get; init; }
        public string? DebtorAgentCountry { get; init; }
        public IReadOnlyList<IntraCompanyTransaction> Transactions { get; init; } = Array.Empty<IntraCompanyTransaction>();
    }

    public sealed class IntraCompanyTransaction
    {
        public string? InstructionId { get; init; }
        public string EndToEndId { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public decimal? EquivalentAmount { get; init; }
        public string? TransferCurrency { get; init; }
        public string? ContractId { get; init; }
        public string? CreditorAgentBic { get; init; }
        public string? CreditorAgentName { get; init; }
        public IntraCompanyParty Creditor { get; init; } = new();
        public IntraCompanyAccount CreditorAccount { get; init; } = new();
        public string? RemittanceInformation { get; init; }
    }

    public sealed class IntraCompanyParty
    {
        public string Name { get; init; } = string.Empty;
        public string? BicOrBei { get; init; }
        public IntraCompanyPostalAddress? PostalAddress { get; init; }
    }

    public sealed class IntraCompanyPostalAddress
    {
        public string? PostalCode { get; init; }
        public string? TownName { get; init; }
        public string? Country { get; init; }
        public IReadOnlyList<string> AddressLines { get; init; } = Array.Empty<string>();
    }

    public sealed class IntraCompanyAccount
    {
        public IntraCompanyAccountIdentificationType IdentificationType { get; init; }
        public string Identification { get; init; } = string.Empty;
        public string? Currency { get; init; }
    }

    public enum IntraCompanyAccountIdentificationType
    {
        Other = 0,
        Iban = 1
    }
}

