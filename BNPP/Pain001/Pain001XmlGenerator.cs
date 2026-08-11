using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BNPPIntegration.BNPP.Pain001.Models;

namespace BNPPIntegration.BNPP.Pain001
{
    public sealed partial class Pain001XmlGenerator
    {
        public const string NamespaceName = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

        private static readonly XNamespace Ns = NamespaceName;

        public string Generate(Pain001Request request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Validate(request);

            var transactionCount = request.Payments.Sum(payment => payment.Transactions.Count);
            var controlSum = request.Payments.Sum(payment => payment.Transactions.Sum(transaction => transaction.Amount));

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Ns + "Document",
                    new XElement(Ns + "CstmrCdtTrfInitn",
                        CreateGroupHeader(request, transactionCount, controlSum),
                        request.Payments.Select(CreatePaymentInformation))));

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                OmitXmlDeclaration = false
            }))
            {
                document.Save(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public async Task<string> GenerateFileAsync(
            Pain001Request request,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var xml = Generate(request);
            await using var stream = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(xml.AsMemory(), cancellationToken);
            return fullPath;
        }

        private static XElement CreateGroupHeader(Pain001Request request, int count, decimal sum)
        {
            return new XElement(Ns + "GrpHdr",
                Element("MsgId", request.MessageId),
                Element("CreDtTm", request.CreationDateTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)),
                Element("NbOfTxs", count.ToString(CultureInfo.InvariantCulture)),
                Element("CtrlSum", FormatAmount(sum)),
                CreateParty("InitgPty", request.InitiatingParty, includeOrganisationIdentification: true));
        }

        private static XElement CreatePaymentInformation(Pain001PaymentInformation payment)
        {
            var count = payment.Transactions.Count;
            var sum = payment.Transactions.Sum(transaction => transaction.Amount);

            return new XElement(Ns + "PmtInf",
                Element("PmtInfId", payment.PaymentInformationId),
                Element("PmtMtd", "TRF"),
                OptionalElement("BtchBookg", payment.BatchBooking?.ToString().ToLowerInvariant()),
                Element("NbOfTxs", count.ToString(CultureInfo.InvariantCulture)),
                Element("CtrlSum", FormatAmount(sum)),
                string.IsNullOrWhiteSpace(payment.ServiceLevelCode)
                    ? null
                    : new XElement(Ns + "PmtTpInf",
                        new XElement(Ns + "SvcLvl", Element("Cd", NormalizeCode(payment.ServiceLevelCode)))),
                Element("ReqdExctnDt", payment.RequestedExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                CreateParty("Dbtr", payment.Debtor),
                CreateAccount("DbtrAcct", payment.DebtorAccount),
                CreateFinancialInstitution("DbtrAgt", payment.DebtorAgentBic),
                Element("ChrgBr", NormalizeCode(payment.ChargeBearer)),
                payment.Transactions.Select(CreateTransaction));
        }

        private static XElement CreateTransaction(Pain001Transaction transaction)
        {
            return new XElement(Ns + "CdtTrfTxInf",
                new XElement(Ns + "PmtId",
                    OptionalElement("InstrId", transaction.InstructionId),
                    Element("EndToEndId", transaction.EndToEndId)),
                new XElement(Ns + "Amt",
                    new XElement(Ns + "InstdAmt",
                        new XAttribute("Ccy", NormalizeCode(transaction.Currency)),
                        FormatAmount(transaction.Amount))),
                string.IsNullOrWhiteSpace(transaction.CreditorAgentBic)
                    ? null
                    : CreateFinancialInstitution("CdtrAgt", transaction.CreditorAgentBic),
                CreateParty("Cdtr", transaction.Creditor),
                CreateAccount("CdtrAcct", transaction.CreditorAccount),
                string.IsNullOrWhiteSpace(transaction.RemittanceInformation)
                    ? null
                    : new XElement(Ns + "RmtInf", Element("Ustrd", transaction.RemittanceInformation.Trim())));
        }

        private static XElement CreateParty(string elementName, Pain001Party party, bool includeOrganisationIdentification = false)
        {
            return new XElement(Ns + elementName,
                Element("Nm", party.Name.Trim()),
                CreatePostalAddress(party.PostalAddress),
                includeOrganisationIdentification && !string.IsNullOrWhiteSpace(party.BicOrBei)
                    ? new XElement(Ns + "Id",
                        new XElement(Ns + "OrgId", Element("BICOrBEI", NormalizeCode(party.BicOrBei))))
                    : null);
        }

        private static XElement? CreatePostalAddress(Pain001PostalAddress? address)
        {
            if (address is null)
            {
                return null;
            }

            return new XElement(Ns + "PstlAdr",
                OptionalElement("PstCd", address.PostalCode),
                OptionalElement("TwnNm", address.TownName),
                OptionalElement("Ctry", string.IsNullOrWhiteSpace(address.Country) ? null : NormalizeCode(address.Country)),
                address.AddressLines.Select(line => Element("AdrLine", line.Trim())));
        }

        private static XElement CreateAccount(string elementName, Pain001Account account)
        {
            var identification = account.IdentificationType == Pain001AccountIdentificationType.Iban
                ? Element("IBAN", RemoveWhitespace(account.Identification).ToUpperInvariant())
                : new XElement(Ns + "Othr", Element("Id", account.Identification.Trim()));

            return new XElement(Ns + elementName,
                new XElement(Ns + "Id", identification),
                OptionalElement("Ccy", string.IsNullOrWhiteSpace(account.Currency) ? null : NormalizeCode(account.Currency)));
        }

        private static XElement CreateFinancialInstitution(string elementName, string bic)
        {
            return new XElement(Ns + elementName,
                new XElement(Ns + "FinInstnId", Element("BIC", NormalizeCode(bic))));
        }

        private static XElement Element(string name, string value) => new(Ns + name, value);

        private static XElement? OptionalElement(string name, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : Element(name, value.Trim());

        private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

        private static string RemoveWhitespace(string value) =>
            new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

        private static string FormatAmount(decimal value) =>
            value.ToString("0.#################", CultureInfo.InvariantCulture);

        private static void Validate(Pain001Request request)
        {
            Required(request.MessageId, nameof(request.MessageId), 35);
            Required(request.InitiatingParty.Name, "InitiatingParty.Name", 140);
            OptionalBic(request.InitiatingParty.BicOrBei, "InitiatingParty.BicOrBei");

            if (request.CreationDateTime == default)
            {
                throw new ArgumentException("CreationDateTime is required.", nameof(request));
            }

            if (request.Payments.Count == 0)
            {
                throw new ArgumentException("At least one payment information group is required.", nameof(request));
            }

            var endToEndIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var payment in request.Payments)
            {
                Required(payment.PaymentInformationId, "PaymentInformationId", 35);
                Required(payment.Debtor.Name, "Debtor.Name", 140);
                ValidateAccount(payment.DebtorAccount, "DebtorAccount");
                Required(payment.DebtorAgentBic, "DebtorAgentBic", 11);
                OptionalBic(payment.DebtorAgentBic, "DebtorAgentBic");
                Required(payment.ChargeBearer, "ChargeBearer", 4);

                if (payment.RequestedExecutionDate == default)
                {
                    throw new ArgumentException("RequestedExecutionDate is required.", nameof(request));
                }

                if (payment.Transactions.Count == 0)
                {
                    throw new ArgumentException($"Payment '{payment.PaymentInformationId}' must contain at least one transaction.", nameof(request));
                }

                foreach (var transaction in payment.Transactions)
                {
                    Required(transaction.EndToEndId, "EndToEndId", 35);
                    if (!endToEndIds.Add(transaction.EndToEndId))
                    {
                        throw new ArgumentException($"EndToEndId '{transaction.EndToEndId}' is duplicated.", nameof(request));
                    }

                    if (transaction.Amount <= 0)
                    {
                        throw new ArgumentException($"Transaction '{transaction.EndToEndId}' amount must be greater than zero.", nameof(request));
                    }

                    if (!CurrencyPattern().IsMatch(NormalizeCode(transaction.Currency)))
                    {
                        throw new ArgumentException($"Transaction '{transaction.EndToEndId}' currency must contain three uppercase letters.", nameof(request));
                    }

                    OptionalBic(transaction.CreditorAgentBic, "CreditorAgentBic");
                    Required(transaction.Creditor.Name, "Creditor.Name", 140);
                    ValidateAccount(transaction.CreditorAccount, "CreditorAccount");
                    Optional(transaction.RemittanceInformation, "RemittanceInformation", 140);
                }
            }
        }

        private static void ValidateAccount(Pain001Account account, string path)
        {
            ArgumentNullException.ThrowIfNull(account);
            Required(account.Identification, $"{path}.Identification", 34);
            if (account.IdentificationType == Pain001AccountIdentificationType.Iban
                && !IbanPattern().IsMatch(RemoveWhitespace(account.Identification).ToUpperInvariant()))
            {
                throw new ArgumentException($"{path}.Identification is not a valid IBAN format.");
            }
        }

        private static void Required(string? value, string path, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
            {
                throw new ArgumentException($"{path} must contain between 1 and {maximumLength} characters.");
            }
        }

        private static void Optional(string? value, string path, int maximumLength)
        {
            if (value is not null && value.Trim().Length > maximumLength)
            {
                throw new ArgumentException($"{path} cannot exceed {maximumLength} characters.");
            }
        }

        private static void OptionalBic(string? value, string path)
        {
            if (!string.IsNullOrWhiteSpace(value) && !BicPattern().IsMatch(NormalizeCode(value)))
            {
                throw new ArgumentException($"{path} must be a valid 8 or 11 character BIC.");
            }
        }

        [GeneratedRegex("^[A-Z]{3}$")]
        private static partial Regex CurrencyPattern();

        [GeneratedRegex("^[A-Z]{6}[A-Z2-9][A-NP-Z0-9](?:[A-Z0-9]{3})?$")]
        private static partial Regex BicPattern();

        [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{1,30}$")]
        private static partial Regex IbanPattern();
    }
}
