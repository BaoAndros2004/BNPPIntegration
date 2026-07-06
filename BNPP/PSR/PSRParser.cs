using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BNPPIntegration.BNPP.PSR.Models;

namespace BNPPIntegration.BNPP.PSR
{
    public sealed class PSRParser
    {
        private const string NamespaceName = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";

        public PSRParseResult Parse(string content, string? sourceName = null)
        {
            return Parse(new PSRRawMessage
            {
                SourceName = sourceName ?? string.Empty,
                Content = content
            });
        }

        public PSRParseResult Parse(PSRRawMessage rawMessage)
        {
            ArgumentNullException.ThrowIfNull(rawMessage);

            if (string.IsNullOrWhiteSpace(rawMessage.Content))
            {
                return PSRParseResult.Failure(new[] { "The PSR input is empty." });
            }

            XDocument document;

            try
            {
                using var stringReader = new StringReader(rawMessage.Content);
                using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                document = XDocument.Load(xmlReader, LoadOptions.SetLineInfo);
            }
            catch (XmlException exception)
            {
                return PSRParseResult.Failure(new[]
                {
                    $"The PSR input is not valid XML (line {exception.LineNumber}, position {exception.LinePosition})."
                });
            }

            var errors = new List<string>();
            var ns = XNamespace.Get(NamespaceName);

            if (document.Root?.Name != ns + "Document")
            {
                return PSRParseResult.Failure(new[]
                {
                    $"The root element must be Document in the {NamespaceName} namespace."
                });
            }

            var statusReport = GetSingleElement(document.Root, ns + "CstmrPmtStsRpt", "CstmrPmtStsRpt", true, errors);
            if (statusReport is null)
            {
                return PSRParseResult.Failure(errors);
            }

            var groupHeader = GetSingleElement(statusReport, ns + "GrpHdr", "GrpHdr", true, errors);
            var originalGroup = GetSingleElement(statusReport, ns + "OrgnlGrpInfAndSts", "OrgnlGrpInfAndSts", true, errors);
            if (groupHeader is null || originalGroup is null)
            {
                return PSRParseResult.Failure(errors);
            }

            var messageId = GetRequiredValue(groupHeader, ns + "MsgId", "GrpHdr/MsgId", errors);
            var creationDateTime = ParseDateTime(
                GetRequiredValue(groupHeader, ns + "CreDtTm", "GrpHdr/CreDtTm", errors),
                "GrpHdr/CreDtTm",
                errors);
            var initiatingPartyBic = ParseInitiatingPartyBic(groupHeader, ns, errors);

            var originalMessageId = GetRequiredValue(
                originalGroup,
                ns + "OrgnlMsgId",
                "OrgnlGrpInfAndSts/OrgnlMsgId",
                errors);
            var originalMessageNameId = GetRequiredValue(
                originalGroup,
                ns + "OrgnlMsgNmId",
                "OrgnlGrpInfAndSts/OrgnlMsgNmId",
                errors);
            var originalCreationDateTime = ParseDateTime(
                GetOptionalValue(originalGroup, ns + "OrgnlCreDtTm", "OrgnlGrpInfAndSts/OrgnlCreDtTm", errors),
                "OrgnlGrpInfAndSts/OrgnlCreDtTm",
                errors);
            var originalNumberOfTransactions = ParseInteger(
                GetOptionalValue(originalGroup, ns + "OrgnlNbOfTxs", "OrgnlGrpInfAndSts/OrgnlNbOfTxs", errors),
                "OrgnlGrpInfAndSts/OrgnlNbOfTxs",
                errors);
            var originalControlSum = ParseDecimal(
                GetOptionalValue(originalGroup, ns + "OrgnlCtrlSum", "OrgnlGrpInfAndSts/OrgnlCtrlSum", errors),
                "OrgnlGrpInfAndSts/OrgnlCtrlSum",
                17,
                errors);
            var groupStatus = GetRequiredValue(originalGroup, ns + "GrpSts", "OrgnlGrpInfAndSts/GrpSts", errors);
            var groupReasons = ParseStatusReasons(originalGroup, ns, "OrgnlGrpInfAndSts", errors);
            var groupSummaries = ParseTransactionStatusSummaries(originalGroup, ns, "OrgnlGrpInfAndSts", errors);
            var paymentInformation = statusReport
                .Elements(ns + "OrgnlPmtInfAndSts")
                .Select((element, index) => ParsePaymentInformation(element, ns, index, errors))
                .ToArray();

            if (errors.Count > 0)
            {
                return PSRParseResult.Failure(errors);
            }

            return PSRParseResult.Success(new PSRReport
            {
                SourceName = rawMessage.SourceName,
                MessageId = messageId!,
                CreationDateTime = creationDateTime,
                InitiatingPartyBic = initiatingPartyBic,
                OriginalMessageId = originalMessageId!,
                OriginalMessageNameId = originalMessageNameId!,
                OriginalCreationDateTime = originalCreationDateTime,
                OriginalNumberOfTransactions = originalNumberOfTransactions,
                OriginalControlSum = originalControlSum,
                GroupStatus = groupStatus!,
                StatusReasons = groupReasons,
                TransactionStatusSummaries = groupSummaries,
                PaymentInformation = paymentInformation
            });
        }

        private static PSRPaymentInformation ParsePaymentInformation(
            XElement element,
            XNamespace ns,
            int index,
            ICollection<string> errors)
        {
            var path = $"OrgnlPmtInfAndSts[{index}]";
            var paymentId = GetRequiredValue(element, ns + "OrgnlPmtInfId", $"{path}/OrgnlPmtInfId", errors);
            var transactionCount = ParseInteger(
                GetOptionalValue(element, ns + "OrgnlNbOfTxs", $"{path}/OrgnlNbOfTxs", errors),
                $"{path}/OrgnlNbOfTxs",
                errors);
            var controlSum = ParseDecimal(
                GetOptionalValue(element, ns + "OrgnlCtrlSum", $"{path}/OrgnlCtrlSum", errors),
                $"{path}/OrgnlCtrlSum",
                17,
                errors);
            var status = GetRequiredValue(element, ns + "PmtInfSts", $"{path}/PmtInfSts", errors);

            return new PSRPaymentInformation
            {
                OriginalPaymentInformationId = paymentId ?? string.Empty,
                OriginalNumberOfTransactions = transactionCount,
                OriginalControlSum = controlSum,
                PaymentInformationStatus = status ?? string.Empty,
                StatusReasons = ParseStatusReasons(element, ns, path, errors),
                TransactionStatusSummaries = ParseTransactionStatusSummaries(element, ns, path, errors),
                Transactions = element
                    .Elements(ns + "TxInfAndSts")
                    .Select((transaction, transactionIndex) => ParseTransaction(
                        transaction,
                        ns,
                        $"{path}/TxInfAndSts[{transactionIndex}]",
                        errors))
                    .ToArray()
            };
        }

        private static PSRTransaction ParseTransaction(
            XElement element,
            XNamespace ns,
            string path,
            ICollection<string> errors)
        {
            var originalTransactionReferenceElement = GetSingleElement(
                element,
                ns + "OrgnlTxRef",
                $"{path}/OrgnlTxRef",
                false,
                errors);

            return new PSRTransaction
            {
                StatusId = GetOptionalValue(element, ns + "StsId", $"{path}/StsId", errors),
                OriginalInstructionId = GetOptionalValue(
                    element,
                    ns + "OrgnlInstrId",
                    $"{path}/OrgnlInstrId",
                    errors),
                OriginalEndToEndId = GetOptionalValue(
                    element,
                    ns + "OrgnlEndToEndId",
                    $"{path}/OrgnlEndToEndId",
                    errors),
                TransactionStatus = GetRequiredValue(element, ns + "TxSts", $"{path}/TxSts", errors) ?? string.Empty,
                StatusReasons = ParseStatusReasons(element, ns, path, errors),
                AcceptanceDateTime = ParseDateTime(
                    GetOptionalValue(element, ns + "AccptncDtTm", $"{path}/AccptncDtTm", errors),
                    $"{path}/AccptncDtTm",
                    errors),
                AccountServicerReference = GetOptionalValue(
                    element,
                    ns + "AcctSvcrRef",
                    $"{path}/AcctSvcrRef",
                    errors),
                ClearingSystemReference = GetOptionalValue(
                    element,
                    ns + "ClrSysRef",
                    $"{path}/ClrSysRef",
                    errors),
                OriginalTransactionReference = originalTransactionReferenceElement is null
                    ? null
                    : ParseOriginalTransactionReference(originalTransactionReferenceElement, ns, $"{path}/OrgnlTxRef", errors)
            };
        }

        private static PSROriginalTransactionReference ParseOriginalTransactionReference(
            XElement element,
            XNamespace ns,
            string path,
            ICollection<string> errors)
        {
            var amountContainer = GetSingleElement(element, ns + "Amt", $"{path}/Amt", false, errors);
            var instructedAmountElement = amountContainer is null
                ? null
                : GetSingleElement(amountContainer, ns + "InstdAmt", $"{path}/Amt/InstdAmt", true, errors);
            var amount = ParseDecimal(instructedAmountElement?.Value.Trim(), $"{path}/Amt/InstdAmt", 5, errors);
            var currency = instructedAmountElement?.Attribute("Ccy")?.Value.Trim();
            if (instructedAmountElement is not null && string.IsNullOrWhiteSpace(currency))
            {
                errors.Add($"The required attribute {path}/Amt/InstdAmt@Ccy is missing or empty.");
            }

            return new PSROriginalTransactionReference
            {
                InstructedAmount = amount,
                Currency = NullIfWhiteSpace(currency),
                RequestedExecutionDate = ParseDate(
                    GetOptionalValue(element, ns + "ReqdExctnDt", $"{path}/ReqdExctnDt", errors),
                    $"{path}/ReqdExctnDt",
                    errors),
                DebtorAccount = ParseAccount(element, ns, "DbtrAcct", path, errors),
                CreditorAccount = ParseAccount(element, ns, "CdtrAcct", path, errors),
                UnstructuredRemittanceInformation = ParseRemittanceInformation(element, ns, path, errors)
            };
        }

        private static PSRAccountIdentification? ParseAccount(
            XElement transactionReference,
            XNamespace ns,
            string elementName,
            string path,
            ICollection<string> errors)
        {
            var accountPath = $"{path}/{elementName}";
            var account = GetSingleElement(transactionReference, ns + elementName, accountPath, false, errors);
            if (account is null)
            {
                return null;
            }

            var identification = GetSingleElement(account, ns + "Id", $"{accountPath}/Id", true, errors);
            if (identification is null)
            {
                return null;
            }

            var iban = GetOptionalValue(identification, ns + "IBAN", $"{accountPath}/Id/IBAN", errors);
            var other = GetSingleElement(identification, ns + "Othr", $"{accountPath}/Id/Othr", false, errors);
            var otherId = other is null
                ? null
                : GetRequiredValue(other, ns + "Id", $"{accountPath}/Id/Othr/Id", errors);

            if (iban is not null && otherId is not null)
            {
                errors.Add($"{accountPath}/Id must contain either IBAN or Othr, not both.");
            }

            if (iban is null && otherId is null)
            {
                errors.Add($"{accountPath}/Id must contain an IBAN or other account identifier.");
                return null;
            }

            return new PSRAccountIdentification
            {
                Type = iban is null ? "Other" : "IBAN",
                Value = iban ?? otherId!
            };
        }

        private static IReadOnlyList<string> ParseRemittanceInformation(
            XElement transactionReference,
            XNamespace ns,
            string path,
            ICollection<string> errors)
        {
            var remittance = GetSingleElement(transactionReference, ns + "RmtInf", $"{path}/RmtInf", false, errors);
            return remittance?.Elements(ns + "Ustrd").Select(element => element.Value.Trim()).ToArray()
                ?? Array.Empty<string>();
        }

        private static IReadOnlyList<PSRStatusReason> ParseStatusReasons(
            XElement parent,
            XNamespace ns,
            string path,
            ICollection<string> errors)
        {
            var reasons = new List<PSRStatusReason>();
            var reasonIndex = 0;

            foreach (var statusReason in parent.Elements(ns + "StsRsnInf"))
            {
                var reasonPath = $"{path}/StsRsnInf[{reasonIndex++}]";
                var reason = GetSingleElement(statusReason, ns + "Rsn", $"{reasonPath}/Rsn", false, errors);
                var code = reason is null
                    ? null
                    : GetRequiredValue(reason, ns + "Cd", $"{reasonPath}/Rsn/Cd", errors);

                reasons.Add(new PSRStatusReason
                {
                    Code = code,
                    AdditionalInformation = statusReason
                        .Elements(ns + "AddtlInf")
                        .Select(element => element.Value.Trim())
                        .ToArray()
                });
            }

            return reasons;
        }

        private static IReadOnlyList<PSRTransactionStatusSummary> ParseTransactionStatusSummaries(
            XElement parent,
            XNamespace ns,
            string path,
            ICollection<string> errors)
        {
            var summaries = new List<PSRTransactionStatusSummary>();
            var index = 0;

            foreach (var summaryElement in parent.Elements(ns + "NbOfTxsPerSts"))
            {
                var summaryPath = $"{path}/NbOfTxsPerSts[{index++}]";
                var count = ParseInteger(
                    GetRequiredValue(summaryElement, ns + "DtldNbOfTxs", $"{summaryPath}/DtldNbOfTxs", errors),
                    $"{summaryPath}/DtldNbOfTxs",
                    errors);
                var status = GetRequiredValue(summaryElement, ns + "DtldSts", $"{summaryPath}/DtldSts", errors);
                var controlSum = ParseDecimal(
                    GetOptionalValue(summaryElement, ns + "DtldCtrlSum", $"{summaryPath}/DtldCtrlSum", errors),
                    $"{summaryPath}/DtldCtrlSum",
                    17,
                    errors);

                summaries.Add(new PSRTransactionStatusSummary
                {
                    Status = status ?? string.Empty,
                    NumberOfTransactions = count ?? 0,
                    ControlSum = controlSum
                });
            }

            return summaries;
        }

        private static string? ParseInitiatingPartyBic(XElement groupHeader, XNamespace ns, ICollection<string> errors)
        {
            var initiatingParty = GetSingleElement(groupHeader, ns + "InitgPty", "GrpHdr/InitgPty", false, errors);
            if (initiatingParty is null)
            {
                return null;
            }

            var identification = GetSingleElement(initiatingParty, ns + "Id", "GrpHdr/InitgPty/Id", true, errors);
            var organisationIdentification = identification is null
                ? null
                : GetSingleElement(identification, ns + "OrgId", "GrpHdr/InitgPty/Id/OrgId", true, errors);

            return organisationIdentification is null
                ? null
                : GetRequiredValue(
                    organisationIdentification,
                    ns + "BICOrBEI",
                    "GrpHdr/InitgPty/Id/OrgId/BICOrBEI",
                    errors);
        }

        private static DateTime? ParseDateTime(string? value, string path, ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            try
            {
                if (!value.Contains('T', StringComparison.Ordinal))
                {
                    throw new FormatException();
                }

                return XmlConvert.ToDateTime(value, XmlDateTimeSerializationMode.RoundtripKind);
            }
            catch (FormatException)
            {
                errors.Add($"{path} must be a valid ISO 8601 date and time.");
                return null;
            }
        }

        private static DateOnly? ParseDate(string? value, string path, ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            errors.Add($"{path} must be a valid date in yyyy-MM-dd format.");
            return null;
        }

        private static long? ParseInteger(string? value, string path, ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            if (value.Length is < 1 or > 15
                || value.Any(character => !char.IsAsciiDigit(character))
                || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                errors.Add($"{path} must contain between 1 and 15 digits.");
                return null;
            }

            return number;
        }

        private static decimal? ParseDecimal(
            string? value,
            string path,
            int maximumFractionalDigits,
            ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            var unsignedValue = value.TrimStart('+', '-');
            var parts = unsignedValue.Split('.');
            var validLexicalValue = parts.Length <= 2
                && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
            var totalDigits = parts.Sum(part => part.Length);
            var fractionalDigits = parts.Length == 2 ? parts[1].Length : 0;

            if (!validLexicalValue
                || totalDigits > 18
                || fractionalDigits > maximumFractionalDigits
                || !decimal.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                errors.Add($"{path} must be a decimal with at most 18 total and {maximumFractionalDigits} fractional digits.");
                return null;
            }

            return amount;
        }

        private static XElement? GetSingleElement(
            XElement parent,
            XName name,
            string path,
            bool required,
            ICollection<string> errors)
        {
            var elements = parent.Elements(name).ToArray();
            if (elements.Length > 1)
            {
                errors.Add($"{path} must occur only once.");
            }

            if (required && elements.Length == 0)
            {
                errors.Add($"The required element {path} is missing.");
            }

            return elements.FirstOrDefault();
        }

        private static string? GetRequiredValue(XElement parent, XName name, string path, ICollection<string> errors)
        {
            var element = GetSingleElement(parent, name, path, true, errors);
            var value = NullIfWhiteSpace(element?.Value);
            if (element is not null && value is null)
            {
                errors.Add($"The required element {path} is empty.");
            }

            return value;
        }

        private static string? GetOptionalValue(XElement parent, XName name, string path, ICollection<string> errors)
        {
            var element = GetSingleElement(parent, name, path, false, errors);
            if (element is null)
            {
                return null;
            }

            var value = NullIfWhiteSpace(element.Value);
            if (value is null)
            {
                errors.Add($"{path} must not be empty when present.");
            }

            return value;
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
