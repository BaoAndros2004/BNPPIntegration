using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BNPPIntegration.BNPP.Payments.Domestic
{
    public sealed partial class DomesticGenerator
    {
        private readonly XNamespace _namespace;
        private readonly string _clearingSystemId;
        private readonly string _creditorAgentMemberId;
        private readonly string _companyName;
        private readonly string _companyAccount;
        private readonly string _defaultInstructionPriority;
        private readonly string _defaultCompanyAccountCurrency;
        private readonly string _defaultCompanyBankBic;
        private readonly string _defaultCompanyBankName;
        private readonly string _defaultCompanyCountry;
        private readonly string _paymentMethod;

        public DomesticGenerator(IConfiguration configuration)
        {
            _companyName = RequiredConfiguration(configuration, "Payments:CompanyName");
            _companyAccount = RequiredConfiguration(configuration, "Payments:CompanyAccount");
            _namespace = RequiredConfiguration(configuration, "Payments:XmlNamespace");
            _clearingSystemId = RequiredConfiguration(configuration, "Payments:ClearingSystemId");
            _creditorAgentMemberId = RequiredConfiguration(configuration, "Payments:CreditorAgentMemberId");
            
            _defaultInstructionPriority = RequiredConfiguration(configuration, "Payments:InstructionPriority");
            _defaultCompanyAccountCurrency = RequiredConfiguration(configuration, "Payments:CompanyAccountCurrency");
            _defaultCompanyBankBic = RequiredConfiguration(configuration, "Payments:CompanyBankBic");
            _defaultCompanyBankName = RequiredConfiguration(configuration, "Payments:CompanyBankName");
            _defaultCompanyCountry = RequiredConfiguration(configuration, "Payments:CompanyCountry");
            _paymentMethod = RequiredConfiguration(configuration, "Payments:PaymentMethod");
        }

        public string Generate(DomesticRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Validate(request);

            var count = request.Payments.Sum(payment => payment.Transactions.Count);
            var sum = request.Payments.Sum(payment => payment.Transactions.Sum(transaction => transaction.Amount));
            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(_namespace + "Document",
                    new XElement(_namespace + "CstmrCdtTrfInitn",
                        CreateGroupHeader(request, count, sum),
                        request.Payments.Select(CreatePaymentInformation))));

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine
            }))
            {
                document.Save(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public async Task<string> GenerateFileAsync(
            DomesticRequest request,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An output path is required.", nameof(outputPath));

            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var xml = Generate(request);
            await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(xml.AsMemory(), cancellationToken);
            return fullPath;
        }

        private XElement CreateGroupHeader(DomesticRequest request, int count, decimal sum) =>
            new(_namespace + "GrpHdr",
                Element("MsgId", request.MessageId.Trim()),
                Element("CreDtTm", request.CreationDateTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)),
                Element("NbOfTxs", count.ToString(CultureInfo.InvariantCulture)),
                Element("CtrlSum", FormatAmount(sum)),
                CreateParty("InitgPty", ResolveCompanyParty(request.InitiatingParty), true));

        private XElement CreatePaymentInformation(DomesticPaymentInformation payment)
        {
            var count = payment.Transactions.Count;
            var sum = payment.Transactions.Sum(transaction => transaction.Amount);
            var instructionPriority = string.IsNullOrWhiteSpace(payment.InstructionPriority) ? _defaultInstructionPriority : payment.InstructionPriority;
            var debtorAgentBic = string.IsNullOrWhiteSpace(payment.DebtorAgentBic) ? _defaultCompanyBankBic : payment.DebtorAgentBic;
            var debtorAgentName = string.IsNullOrWhiteSpace(payment.DebtorAgentName) ? _defaultCompanyBankName : payment.DebtorAgentName;
            var debtorAgentCountry = string.IsNullOrWhiteSpace(payment.DebtorAgentCountry) ? _defaultCompanyCountry : payment.DebtorAgentCountry;

            return new XElement(_namespace + "PmtInf",
                Element("PmtInfId", payment.PaymentInformationId?.Trim() ?? string.Empty),
                Element("PmtMtd", _paymentMethod),
                OptionalElement("BtchBookg", payment.BatchBooking?.ToString().ToLowerInvariant()),
                Element("NbOfTxs", count.ToString(CultureInfo.InvariantCulture)),
                Element("CtrlSum", FormatAmount(sum)),
                new XElement(_namespace + "PmtTpInf", Element("InstrPrty", NormalizeCode(instructionPriority))),
                Element("ReqdExctnDt", payment.RequestedExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                CreateParty("Dbtr", ResolveCompanyParty(payment.Debtor), defaultCountry: _defaultCompanyCountry),
                CreateAccount("DbtrAcct", ResolveCompanyAccount(payment.DebtorAccount), true),
                CreateBicInstitution("DbtrAgt", debtorAgentBic, debtorAgentName, debtorAgentCountry),
                OptionalElement("ChrgBr", payment.ChargeBearer?.ToUpperInvariant()),
                payment.Transactions.Select(CreateTransaction));
        }

        private XElement CreateTransaction(DomesticTransaction transaction) =>
            new(_namespace + "CdtTrfTxInf",
                new XElement(_namespace + "PmtId",
                    OptionalElement("InstrId", transaction.InstructionId),
                    Element("EndToEndId", transaction.EndToEndId.Trim())),
                new XElement(_namespace + "Amt",
                    new XElement(_namespace + "InstdAmt",
                        new XAttribute("Ccy", NormalizeCode(transaction.Currency)),
                        FormatAmount(transaction.Amount))),
                CreateClearingInstitution(transaction),
                CreateParty("Cdtr", transaction.Creditor, defaultCountry: "VN"),
                CreateAccount("CdtrAcct", transaction.CreditorAccount),
                string.IsNullOrWhiteSpace(transaction.RemittanceInformation)
                    ? null
                    : new XElement(_namespace + "RmtInf", Element("Ustrd", transaction.RemittanceInformation.Trim())));

        private XElement CreateClearingInstitution(DomesticTransaction transaction)
        {
            var address = transaction.CreditorAgentPostalAddress ?? new DomesticPostalAddress
            {
                Country = "VN",
                AddressLines = new[] { "Viet Nam" }
            };
            return new XElement(_namespace + "CdtrAgt",
                new XElement(_namespace + "FinInstnId",
                    new XElement(_namespace + "ClrSysMmbId",
                        new XElement(_namespace + "ClrSysId", Element("Prtry", _clearingSystemId)),
                        Element("MmbId", _creditorAgentMemberId)),
                    OptionalElement("Nm", transaction.CreditorAgentName),
                    CreatePostalAddress(address, "VN")));
        }

        private XElement CreateBicInstitution(string name, string bic, string? institutionName, string country) =>
            new(_namespace + name,
                new XElement(_namespace + "FinInstnId",
                    Element("BIC", NormalizeCode(bic)),
                    OptionalElement("Nm", institutionName),
                    CreatePostalAddress(new DomesticPostalAddress { Country = country })));

        private XElement CreateParty(
            string name,
            DomesticParty party,
            bool includeOrganisationIdentification = false,
            string? defaultCountry = null) =>
            new(_namespace + name,
                Element("Nm", party.Name.Trim()),
                CreatePostalAddress(party.PostalAddress, defaultCountry),
                includeOrganisationIdentification && !string.IsNullOrWhiteSpace(party.BicOrBei)
                    ? new XElement(_namespace + "Id", new XElement(_namespace + "OrgId", Element("BICOrBEI", NormalizeCode(party.BicOrBei))))
                    : null);

        private XElement? CreatePostalAddress(DomesticPostalAddress? address, string? defaultCountry = null)
        {
            if (address is null && string.IsNullOrWhiteSpace(defaultCountry)) return null;
            return new XElement(_namespace + "PstlAdr",
                OptionalElement("PstCd", address?.PostalCode),
                OptionalElement("TwnNm", address?.TownName),
                OptionalElement("Ctry", NormalizeCode(address?.Country ?? defaultCountry!)),
                address?.AddressLines.Select(line => Element("AdrLine", line.Trim())));
        }

        private XElement CreateAccount(string name, DomesticAccount account, bool appendCurrency = false)
        {
            var id = account.Identification.Trim();
            if (appendCurrency && !string.IsNullOrWhiteSpace(account.Currency))
            {
                var currency = NormalizeCode(account.Currency);
                if (!id.EndsWith(currency, StringComparison.OrdinalIgnoreCase)) id += currency;
            }

            return new XElement(_namespace + name,
                new XElement(_namespace + "Id",
                    new XElement(_namespace + "Othr",
                        Element("Id", id))));
        }

        private DomesticParty ResolveCompanyParty(DomesticParty? party) =>
            string.IsNullOrWhiteSpace(party?.Name)
                ? new DomesticParty { Name = _companyName, BicOrBei = party?.BicOrBei, PostalAddress = party?.PostalAddress }
                : party;

        private DomesticAccount ResolveCompanyAccount(DomesticAccount? account)
        {
            var accountId = !string.IsNullOrWhiteSpace(account?.Identification)
                ? account.Identification.Trim()
                : _companyAccount;

            var currency = !string.IsNullOrWhiteSpace(account?.Currency)
                ? account.Currency.Trim()
                : _defaultCompanyAccountCurrency;

            return new DomesticAccount
            {
                Identification = accountId,
                Currency = currency,
                IdentificationType = DomesticAccountIdentificationType.Other
            };
        }

        private void Validate(DomesticRequest request)
        {
            NumericRequired(request.MessageId, "MessageId", 35);
            Required(ResolveCompanyParty(request.InitiatingParty).Name, "InitiatingParty.Name", 140);
            if (request.CreationDateTime == default) throw new ArgumentException("CreationDateTime is required.");
            if (request.Payments.Count == 0) throw new ArgumentException("At least one payment is required.");

            var endToEndIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var payment in request.Payments)
            {
                var instructionPriority = string.IsNullOrWhiteSpace(payment.InstructionPriority) ? _defaultInstructionPriority : payment.InstructionPriority;
                var debtorAgentBic = string.IsNullOrWhiteSpace(payment.DebtorAgentBic) ? _defaultCompanyBankBic : payment.DebtorAgentBic;

                NumericRequired(payment.PaymentInformationId, "PaymentInformationId", 16);
                Required(ResolveCompanyParty(payment.Debtor).Name, "Debtor.Name", 140);
                ValidateAccount(ResolveCompanyAccount(payment.DebtorAccount), "DebtorAccount");
                Required(debtorAgentBic, "DebtorAgentBic", 11);
                
                if (!string.Equals(instructionPriority.Trim(), "NORM", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Domestic InstructionPriority must be NORM.");
                
                if (payment.RequestedExecutionDate == default) throw new ArgumentException("RequestedExecutionDate is required.");
                if (payment.Transactions.Count == 0) throw new ArgumentException("At least one transaction is required.");

                foreach (var transaction in payment.Transactions)
                {
                    NumericOptional(transaction.InstructionId, "InstructionId", 16);
                    NumericRequired(transaction.EndToEndId, "EndToEndId", 16);
                    if (!endToEndIds.Add(transaction.EndToEndId.Trim())) throw new ArgumentException("EndToEndId is duplicated.");
                    if (transaction.Amount <= 0) throw new ArgumentException("Amount must be greater than zero.");
                    if (!CurrencyPattern().IsMatch(NormalizeCode(transaction.Currency))) throw new ArgumentException("Currency is invalid.");
                    Required(transaction.CreditorAgentName, "CreditorAgentName", 140);
                    Required(transaction.Creditor.Name, "Creditor.Name", 140);
                    ValidateAccount(transaction.CreditorAccount, "CreditorAccount");
                    Optional(transaction.RemittanceInformation, "RemittanceInformation", 140);
                }
            }
        }

        private static void ValidateAccount(DomesticAccount account, string path)
        {
            Required(account.Identification, $"{path}.Identification", 34);
        }

        private static void Required(string? value, string path, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
                throw new ArgumentException($"{path} must contain between 1 and {maximumLength} characters.");
        }

        private static void Optional(string? value, string path, int maximumLength)
        {
            if (value is not null && value.Trim().Length > maximumLength)
                throw new ArgumentException($"{path} cannot exceed {maximumLength} characters.");
        }

        private static void NumericRequired(string? value, string path, int maximumLength)
        {
            Required(value, path, maximumLength);
            if (!NumericPattern().IsMatch(value!.Trim()))
                throw new ArgumentException($"{path} must contain digits only.");
        }

        private static void NumericOptional(string? value, string path, int maximumLength)
        {
            Optional(value, path, maximumLength);
            if (!string.IsNullOrWhiteSpace(value) && !NumericPattern().IsMatch(value.Trim()))
                throw new ArgumentException($"{path} must contain digits only.");
        }

        private static string RequiredConfiguration(IConfiguration configuration, string key)
        {
            var value = configuration[key];
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"{key} is required.")
                : value.Trim();
        }

        private XElement Element(string name, string value) => new(_namespace + name, value);
        private XElement? OptionalElement(string name, string? value) => string.IsNullOrWhiteSpace(value) ? null : Element(name, value.Trim());
        private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
        private static string RemoveWhitespace(string value) => new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
        private static string FormatAmount(decimal value) => value.ToString("0.#################", CultureInfo.InvariantCulture);

        [GeneratedRegex("^[A-Z]{3}$")]
        private static partial Regex CurrencyPattern();
        [GeneratedRegex("^[0-9]+$")]
        private static partial Regex NumericPattern();
    }
}

