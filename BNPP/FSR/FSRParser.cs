using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BNPPIntegration.BNPP.FSR.Models;

namespace BNPPIntegration.BNPP.FSR
{
    public sealed class FSRParser
    {
        private const string NamespaceName = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";

        public FSRParseResult Parse(string content, string? sourceName = null)
        {
            return Parse(new FSRRawMessage
            {
                SourceName = sourceName ?? string.Empty,
                Content = content
            });
        }

        public FSRParseResult Parse(FSRRawMessage rawMessage)
        {
            ArgumentNullException.ThrowIfNull(rawMessage);

            if (string.IsNullOrWhiteSpace(rawMessage.Content))
            {
                return FSRParseResult.Failure(new[] { "The FSR input is empty." });
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
                return FSRParseResult.Failure(new[]
                {
                    $"The FSR input is not valid XML (line {exception.LineNumber}, position {exception.LinePosition})."
                });
            }

            var errors = new List<string>();
            var ns = XNamespace.Get(NamespaceName);

            if (document.Root?.Name != ns + "Document")
            {
                return FSRParseResult.Failure(new[]
                {
                    $"The root element must be Document in the {NamespaceName} namespace."
                });
            }

            var statusReport = GetSingleElement(document.Root, ns + "CstmrPmtStsRpt", "CstmrPmtStsRpt", true, errors);
            if (statusReport is null)
            {
                return FSRParseResult.Failure(errors);
            }

            var groupHeader = GetSingleElement(statusReport, ns + "GrpHdr", "GrpHdr", true, errors);
            var originalGroup = GetSingleElement(statusReport, ns + "OrgnlGrpInfAndSts", "OrgnlGrpInfAndSts", true, errors);

            if (groupHeader is null || originalGroup is null)
            {
                return FSRParseResult.Failure(errors);
            }

            var messageId = GetRequiredValue(groupHeader, ns + "MsgId", "GrpHdr/MsgId", errors);
            var creationDateTimeText = GetRequiredValue(groupHeader, ns + "CreDtTm", "GrpHdr/CreDtTm", errors);
            var originalMessageId = GetRequiredValue(originalGroup, ns + "OrgnlMsgId", "OrgnlGrpInfAndSts/OrgnlMsgId", errors);
            var originalMessageNameId = GetRequiredValue(originalGroup, ns + "OrgnlMsgNmId", "OrgnlGrpInfAndSts/OrgnlMsgNmId", errors);
            var groupStatus = GetRequiredValue(originalGroup, ns + "GrpSts", "OrgnlGrpInfAndSts/GrpSts", errors);

            DateTime? creationDateTime = null;
            if (!string.IsNullOrEmpty(creationDateTimeText))
            {
                try
                {
                    if (!creationDateTimeText.Contains('T', StringComparison.Ordinal))
                    {
                        throw new FormatException();
                    }

                    creationDateTime = XmlConvert.ToDateTime(creationDateTimeText, XmlDateTimeSerializationMode.RoundtripKind);
                }
                catch (FormatException)
                {
                    errors.Add("GrpHdr/CreDtTm must be a valid ISO 8601 date and time.");
                }
            }

            var initiatingPartyBic = ParseInitiatingPartyBic(groupHeader, ns, errors);
            var transactionCountElement = GetSingleElement(
                originalGroup,
                ns + "OrgnlNbOfTxs",
                "OrgnlGrpInfAndSts/OrgnlNbOfTxs",
                false,
                errors);
            var controlSumElement = GetSingleElement(
                originalGroup,
                ns + "OrgnlCtrlSum",
                "OrgnlGrpInfAndSts/OrgnlCtrlSum",
                false,
                errors);
            var originalNumberOfTransactions = ParseTransactionCount(transactionCountElement?.Value, errors);
            var originalControlSum = ParseControlSum(controlSumElement?.Value, errors);
            var statusReasons = ParseStatusReasons(originalGroup, ns, errors);

            if (errors.Count > 0)
            {
                return FSRParseResult.Failure(errors);
            }

            return FSRParseResult.Success(new FSRReport
            {
                SourceName = rawMessage.SourceName,
                MessageId = messageId!,
                CreationDateTime = creationDateTime,
                InitiatingPartyBic = initiatingPartyBic,
                OriginalMessageId = originalMessageId!,
                OriginalMessageNameId = originalMessageNameId!,
                OriginalNumberOfTransactions = originalNumberOfTransactions,
                OriginalControlSum = originalControlSum,
                GroupStatus = groupStatus!,
                StatusReasons = statusReasons
            });
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

        private static IReadOnlyList<FSRStatusReason> ParseStatusReasons(
            XElement originalGroup,
            XNamespace ns,
            ICollection<string> errors)
        {
            var reasons = new List<FSRStatusReason>();

            foreach (var statusReason in originalGroup.Elements(ns + "StsRsnInf"))
            {
                var reasonElements = statusReason.Elements(ns + "Rsn").ToArray();
                if (reasonElements.Length > 1)
                {
                    errors.Add("StsRsnInf must not contain more than one Rsn element.");
                }

                var code = reasonElements.Length == 0
                    ? null
                    : GetRequiredValue(reasonElements[0], ns + "Cd", "StsRsnInf/Rsn/Cd", errors);

                reasons.Add(new FSRStatusReason
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

        private static long? ParseTransactionCount(string? value, ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            var normalizedValue = value.Trim();
            if (normalizedValue.Length is < 1 or > 15
                || normalizedValue.Any(character => !char.IsAsciiDigit(character))
                || !long.TryParse(normalizedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                errors.Add("OrgnlGrpInfAndSts/OrgnlNbOfTxs must contain between 1 and 15 digits.");
                return null;
            }

            return count;
        }

        private static decimal? ParseControlSum(string? value, ICollection<string> errors)
        {
            if (value is null)
            {
                return null;
            }

            var normalizedValue = value.Trim();
            var unsignedValue = normalizedValue.TrimStart('+', '-');
            var parts = unsignedValue.Split('.');
            var validLexicalValue = parts.Length <= 2
                && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
            var totalDigits = parts.Sum(part => part.Length);
            var fractionalDigits = parts.Length == 2 ? parts[1].Length : 0;

            if (!validLexicalValue
                || totalDigits > 18
                || fractionalDigits > 17
                || !decimal.TryParse(normalizedValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount))
            {
                errors.Add("OrgnlGrpInfAndSts/OrgnlCtrlSum must be a decimal with at most 18 total and 17 fractional digits.");
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

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
