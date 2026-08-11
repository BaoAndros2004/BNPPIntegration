using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.MT942.Models;

namespace BNPPIntegration.BNPP.MT942
{
    public sealed partial class MT942Parser
    {
        private static readonly HashSet<string> SupportedTags = new(StringComparer.Ordinal)
        {
            "20", "21", "25", "28C", "34F", "13D", "61", "86", "90D", "90C"
        };

        public MT942ParseResult Parse(string content, string? sourceName = null)
        {
            return Parse(new MT942RawMessage
            {
                SourceName = sourceName ?? string.Empty,
                Content = content
            });
        }

        public MT942ParseResult Parse(MT942RawMessage rawMessage)
        {
            ArgumentNullException.ThrowIfNull(rawMessage);
            if (string.IsNullOrWhiteSpace(rawMessage.Content))
            {
                return MT942ParseResult.Failure(new[] { "The MT942 input is empty." });
            }

            var errors = new List<string>();
            var rawStatements = ParseSwiftMessages(rawMessage.Content, errors);
            var statements = rawStatements
                .Select((message, index) => ParseStatement(message, index, errors))
                .ToArray();

            if (errors.Count > 0)
            {
                return MT942ParseResult.Failure(errors);
            }

            return MT942ParseResult.Success(new MT942Report
            {
                SourceName = rawMessage.SourceName,
                Statements = statements
            });
        }

        private static IReadOnlyList<RawSwiftMessage> ParseSwiftMessages(string content, ICollection<string> errors)
        {
            var messages = new List<RawSwiftMessage>();
            var position = 0;

            while (true)
            {
                SkipWhitespace(content, ref position);
                if (position >= content.Length)
                {
                    break;
                }

                var statementNumber = messages.Count + 1;
                if (!TryReadBlock(content, ref position, "{1:", "}", statementNumber, "basic header", out var basic, errors)
                    || !TryReadBlock(content, ref position, "{2:", "}", statementNumber, "application header", out var application, errors)
                    || !TryReadBlock(content, ref position, "{4:", "-}", statementNumber, "text", out var text, errors)
                    || !TryReadBlock(content, ref position, "{5:", "}", statementNumber, "trailer", out var trailer, errors))
                {
                    break;
                }

                messages.Add(new RawSwiftMessage(basic, application, text, trailer));
            }

            if (messages.Count == 0 && errors.Count == 0)
            {
                errors.Add("The MT942 input does not contain a SWIFT message.");
            }

            return messages;
        }

        private static bool TryReadBlock(
            string content,
            ref int position,
            string prefix,
            string suffix,
            int statementNumber,
            string blockName,
            out string value,
            ICollection<string> errors)
        {
            SkipWhitespace(content, ref position);
            value = string.Empty;
            if (!content.AsSpan(position).StartsWith(prefix, StringComparison.Ordinal))
            {
                errors.Add($"Statement {statementNumber} is missing the {blockName} block '{prefix}'.");
                return false;
            }

            var valueStart = position + prefix.Length;
            var valueEnd = content.IndexOf(suffix, valueStart, StringComparison.Ordinal);
            if (valueEnd < 0)
            {
                errors.Add($"Statement {statementNumber} has an unterminated {blockName} block.");
                return false;
            }

            value = content[valueStart..valueEnd];
            position = valueEnd + suffix.Length;
            return true;
        }

        private static MT942Statement ParseStatement(
            RawSwiftMessage message,
            int statementIndex,
            ICollection<string> errors)
        {
            var path = $"Statements[{statementIndex}]";
            var fields = ParseTextFields(message.TextBlock, path, errors);
            var basicHeader = ParseBasicHeader(message.BasicHeader, path, errors);
            var applicationHeader = ParseApplicationHeader(message.ApplicationHeader, path, errors);
            var transactionReference = GetRequiredField(fields, "20", path, errors)?.Value.Trim() ?? string.Empty;
            var relatedReference = GetOptionalField(fields, "21", path, errors)?.Value.Trim();
            var account = GetRequiredField(fields, "25", path, errors)?.Value.Trim() ?? string.Empty;
            var sequence = ParseStatementSequence(GetRequiredField(fields, "28C", path, errors)?.Value, path, errors);

            var floorFields = fields.Where(field => field.Tag == "34F").ToArray();
            if (floorFields.Length == 0)
            {
                errors.Add($"{path} is missing required tag :34F:.");
            }
            else if (floorFields.Length > 2)
            {
                errors.Add($"{path} tag :34F: can occur at most twice.");
            }

            var floorLimits = floorFields
                .Select((field, index) => ParseFloorLimit(field.Value, $"{path}:34F[{index}]", errors))
                .ToArray();
            var dateTimeField = GetRequiredField(fields, "13D", path, errors);
            var reportDateTime = dateTimeField is null
                ? null
                : ParseReportDateTime(dateTimeField.Value, $"{path}:13D", errors);
            var transactions = ParseTransactions(fields, dateTimeField, path, errors);
            var debitSummaryField = GetOptionalField(fields, "90D", path, errors);
            var creditSummaryField = GetOptionalField(fields, "90C", path, errors);

            ValidateFieldOrder(fields, path, errors);
            ValidateOrphanInformationFields(fields, path, errors);

            if (!string.IsNullOrWhiteSpace(message.Trailer))
            {
                errors.Add($"{path} trailer block must be empty for the BNPP MT942 format.");
            }

            return new MT942Statement
            {
                BasicHeader = basicHeader,
                ApplicationHeader = applicationHeader,
                TransactionReference = transactionReference,
                RelatedReference = NullIfWhiteSpace(relatedReference),
                AccountIdentification = account,
                StatementNumber = sequence.StatementNumber,
                SequenceNumber = sequence.SequenceNumber,
                FloorLimits = floorLimits,
                ReportDateTime = reportDateTime,
                Transactions = transactions,
                DebitSummary = debitSummaryField is null
                    ? null
                    : ParseEntrySummary("90D", debitSummaryField.Value, $"{path}:90D", errors),
                CreditSummary = creditSummaryField is null
                    ? null
                    : ParseEntrySummary("90C", creditSummaryField.Value, $"{path}:90C", errors),
                Trailer = message.Trailer
            };
        }

        private static MT942BasicHeader ParseBasicHeader(string value, string path, ICollection<string> errors)
        {
            var match = BasicHeaderPattern().Match(value);
            if (!match.Success)
            {
                errors.Add($"{path} basic header must contain application id, service id, 12-character bank address, session and sequence numbers.");
                return new MT942BasicHeader();
            }

            return new MT942BasicHeader
            {
                ApplicationIdentifier = match.Groups["application"].Value,
                ServiceIdentifier = match.Groups["service"].Value,
                BankAddress = match.Groups["bank"].Value,
                SessionNumber = match.Groups["session"].Value,
                SequenceNumber = match.Groups["sequence"].Value
            };
        }

        private static MT942ApplicationHeader ParseApplicationHeader(
            string value,
            string path,
            ICollection<string> errors)
        {
            if (value.Length < 4)
            {
                errors.Add($"{path} application header is too short.");
                return new MT942ApplicationHeader { RawValue = value };
            }

            var direction = value[..1];
            var messageType = value.Substring(1, 3);
            string bankAddress;
            string priority;

            if (direction == "I")
            {
                if (value.Length is not (17 or 21))
                {
                    errors.Add($"{path} input application header must have 17 or 21 characters.");
                    return new MT942ApplicationHeader { Direction = direction, MessageType = messageType, RawValue = value };
                }

                bankAddress = value.Substring(4, 12);
                priority = value.Substring(16, 1);
                if (value.Length == 21 && value[17..].Any(character => !char.IsAsciiDigit(character)))
                {
                    errors.Add($"{path} input application header delivery monitoring and obsolescence values must be numeric.");
                }
            }
            else if (direction == "O")
            {
                if (value.Length != 47)
                {
                    errors.Add($"{path} output application header must have 47 characters.");
                    return new MT942ApplicationHeader { Direction = direction, MessageType = messageType, RawValue = value };
                }

                bankAddress = value.Substring(14, 12);
                priority = value[^1..];
            }
            else
            {
                errors.Add($"{path} application header direction must be I or O.");
                return new MT942ApplicationHeader { Direction = direction, MessageType = messageType, RawValue = value };
            }

            return new MT942ApplicationHeader
            {
                Direction = direction,
                MessageType = messageType,
                BankAddress = bankAddress,
                Priority = priority,
                RawValue = value
            };
        }

        private static IReadOnlyList<RawField> ParseTextFields(
            string textBlock,
            string path,
            ICollection<string> errors)
        {
            var fields = new List<RawField>();
            var lines = NormalizeLines(textBlock).Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var match = FieldPattern().Match(line);
                if (match.Success)
                {
                    var tag = match.Groups["tag"].Value;
                    if (!SupportedTags.Contains(tag))
                    {
                        errors.Add($"{path} contains unsupported tag :{tag}: on text-block line {lineIndex + 1}.");
                    }

                    fields.Add(new RawField(tag, match.Groups["value"].Value, fields.Count));
                    continue;
                }

                if (line.StartsWith(':'))
                {
                    errors.Add($"{path} contains a malformed MT942 tag on text-block line {lineIndex + 1}.");
                    continue;
                }

                if (fields.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        errors.Add($"{path} contains text before the first MT942 tag on text-block line {lineIndex + 1}.");
                    }
                }
                else
                {
                    fields[^1].AppendContinuation(line);
                }
            }

            return fields;
        }

        private static MT942FloorLimit ParseFloorLimit(string value, string path, ICollection<string> errors)
        {
            var normalized = value.Trim();
            if (normalized.Length < 5)
            {
                errors.Add($"{path} must contain currency and amount.");
                return new MT942FloorLimit();
            }

            var currency = normalized[..3];
            var position = 3;
            string? indicator = null;
            if (position < normalized.Length && normalized[position] is 'C' or 'D')
            {
                indicator = normalized.Substring(position++, 1);
            }

            return new MT942FloorLimit
            {
                Currency = currency,
                CreditDebitIndicator = indicator,
                Amount = ParseSwiftAmount(normalized[position..], path, errors) ?? 0
            };
        }

        private static DateTimeOffset? ParseReportDateTime(string value, string path, ICollection<string> errors)
        {
            var normalized = value.Trim();
            if (normalized.Length != 15
                || normalized[..10].Any(character => !char.IsAsciiDigit(character))
                || normalized[10] is not ('+' or '-')
                || normalized[11..].Any(character => !char.IsAsciiDigit(character)))
            {
                errors.Add($"{path} must use YYMMDDHHMM+HHMM or YYMMDDHHMM-HHMM format.");
                return null;
            }

            var date = ParseSwiftDate(normalized[..6], path, errors);
            var hour = int.Parse(normalized.Substring(6, 2), CultureInfo.InvariantCulture);
            var minute = int.Parse(normalized.Substring(8, 2), CultureInfo.InvariantCulture);
            try
            {
                var offsetHour = int.Parse(normalized.Substring(11, 2), CultureInfo.InvariantCulture);
                var offsetMinute = int.Parse(normalized.Substring(13, 2), CultureInfo.InvariantCulture);
                if (offsetMinute > 59 || offsetHour > 14 || (offsetHour == 14 && offsetMinute != 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                var offset = new TimeSpan(offsetHour, offsetMinute, 0);
                if (normalized[10] == '-')
                {
                    offset = -offset;
                }

                return date is null
                    ? null
                    : new DateTimeOffset(date.Value.Year, date.Value.Month, date.Value.Day, hour, minute, 0, offset);
            }
            catch (ArgumentException)
            {
                errors.Add($"{path} contains an invalid time or UTC offset.");
                return null;
            }
        }

        private static MT942EntrySummary ParseEntrySummary(
            string entryType,
            string value,
            string path,
            ICollection<string> errors)
        {
            var normalized = value.Trim();
            if (normalized.Length < 10
                || normalized[..5].Any(character => !char.IsAsciiDigit(character)))
            {
                errors.Add($"{path} must contain a five-digit entry count, currency and amount.");
                return new MT942EntrySummary { EntryType = entryType };
            }

            return new MT942EntrySummary
            {
                EntryType = entryType,
                NumberOfEntries = int.Parse(normalized[..5], CultureInfo.InvariantCulture),
                Currency = normalized.Substring(5, 3),
                Amount = ParseSwiftAmount(normalized[8..], path, errors) ?? 0
            };
        }

        private static IReadOnlyList<MT942Transaction> ParseTransactions(
            IReadOnlyList<RawField> fields,
            RawField? dateTimeField,
            string path,
            ICollection<string> errors)
        {
            var transactions = new List<MT942Transaction>();
            var minimumPosition = dateTimeField?.Position ?? -1;

            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.Tag != "61")
                {
                    continue;
                }

                if (field.Position <= minimumPosition)
                {
                    errors.Add($"{path}:61[{transactions.Count}] must occur after :13D:.");
                }

                RawField? information = null;
                if (index + 1 < fields.Count && fields[index + 1].Tag == "86")
                {
                    information = fields[++index];
                }

                transactions.Add(ParseTransaction(
                    field.Value,
                    information?.Value,
                    $"{path}:61[{transactions.Count}]",
                    errors));
            }

            return transactions;
        }

        private static MT942Transaction ParseTransaction(
            string value,
            string? information,
            string path,
            ICollection<string> errors)
        {
            var lines = NormalizeLines(value).Split('\n');
            var statementLine = lines[0].TrimEnd();
            var supplementaryDetails = lines.Length > 1
                ? NullIfWhiteSpace(string.Join("\n", lines.Skip(1)).TrimEnd())
                : null;

            if (statementLine.Length < 12)
            {
                errors.Add($"{path} is too short.");
                return new MT942Transaction
                {
                    SupplementaryDetails = supplementaryDetails,
                    Details = information is null ? null : ParseTransactionDetails(information, path, errors)
                };
            }

            var position = 0;
            var valueDate = ParseSwiftDate(statementLine[..6], $"{path} value date", errors);
            position += 6;
            DateOnly? entryDate = null;
            if (statementLine.Length >= position + 4
                && statementLine.AsSpan(position, 4).ToArray().All(char.IsAsciiDigit))
            {
                entryDate = ParseEntryDate(statementLine.Substring(position, 4), valueDate, path, errors);
                position += 4;
            }

            var indicator = ParseTransactionIndicator(statementLine, ref position, path, errors);
            string? fundsCode = null;
            if (position < statementLine.Length && char.IsAsciiLetter(statementLine[position]))
            {
                fundsCode = statementLine.Substring(position++, 1);
            }

            var typePosition = statementLine.IndexOf('N', position);
            if (typePosition < 0 || typePosition + 4 > statementLine.Length)
            {
                errors.Add($"{path} must contain an N-prefixed three-character transaction type code.");
                return new MT942Transaction
                {
                    ValueDate = valueDate,
                    EntryDate = entryDate,
                    CreditDebitIndicator = indicator,
                    FundsCode = fundsCode,
                    SupplementaryDetails = supplementaryDetails,
                    Details = information is null ? null : ParseTransactionDetails(information, path, errors)
                };
            }

            var amount = ParseSwiftAmount(statementLine[position..typePosition], $"{path} amount", errors);
            var typeCode = statementLine.Substring(typePosition + 1, 3);
            var references = statementLine[(typePosition + 4)..];
            var separator = references.IndexOf("//", StringComparison.Ordinal);
            var customerReference = (separator < 0 ? references : references[..separator]).Trim();
            var bankReference = separator < 0 ? null : NullIfWhiteSpace(references[(separator + 2)..]);
            if (customerReference.Length == 0)
            {
                errors.Add($"{path} customer reference is missing.");
            }

            return new MT942Transaction
            {
                ValueDate = valueDate,
                EntryDate = entryDate,
                CreditDebitIndicator = indicator,
                FundsCode = fundsCode,
                Amount = amount ?? 0,
                TransactionTypeCode = typeCode,
                CustomerReference = customerReference,
                BankReference = bankReference,
                SupplementaryDetails = supplementaryDetails,
                Details = information is null ? null : ParseTransactionDetails(information, path, errors)
            };
        }

        private static string ParseTransactionIndicator(
            string value,
            ref int position,
            string path,
            ICollection<string> errors)
        {
            foreach (var indicator in new[] { "RD", "RC", "ED", "EC" })
            {
                if (value.AsSpan(position).StartsWith(indicator, StringComparison.Ordinal))
                {
                    position += 2;
                    return indicator;
                }
            }

            if (position < value.Length && value[position] is 'D' or 'C')
            {
                return value.Substring(position++, 1);
            }

            errors.Add($"{path} must contain D, C, RD, RC, ED or EC after its dates.");
            return string.Empty;
        }

        private static MT942TransactionDetails ParseTransactionDetails(
            string value,
            string path,
            ICollection<string> errors)
        {
            var raw = value.TrimEnd();
            var compact = CompactInformation(value);
            string? typeCode = null;
            string? description = null;
            var typeMarker = compact.IndexOf("/TYPE/", StringComparison.Ordinal);
            if (typeMarker >= 0)
            {
                var typeStart = typeMarker + 6;
                var typeEnd = compact.IndexOf('/', typeStart);
                if (typeEnd < 0)
                {
                    typeCode = NullIfWhiteSpace(compact[typeStart..]);
                }
                else
                {
                    typeCode = NullIfWhiteSpace(compact[typeStart..typeEnd]);
                    var descriptionEnd = FindNextMarker(compact, typeEnd, "/BENM/", "/REMI/", "/EXCH/", "/OCMT/");
                    description = NullIfWhiteSpace(compact[(typeEnd + 1)..descriptionEnd].Trim('/'));
                }
            }

            var exchangeText = ExtractSegment(compact, "/EXCH/", "/OCMT/");
            decimal? exchangeRate = null;
            if (exchangeText is not null && !TryParseFlexibleAmount(exchangeText, out exchangeRate))
            {
                errors.Add($"{path}:86 /EXCH/ must contain a valid exchange rate.");
            }

            var originalText = ExtractSegment(compact, "/OCMT/");
            string? originalCurrency = null;
            decimal? originalAmount = null;
            if (originalText is not null)
            {
                if (originalText.Length < 4)
                {
                    errors.Add($"{path}:86 /OCMT/ must contain a currency and amount.");
                }
                else
                {
                    originalCurrency = originalText[..3];
                    if (!TryParseFlexibleAmount(originalText[3..], out originalAmount))
                    {
                        errors.Add($"{path}:86 /OCMT/ must contain a valid amount after its currency.");
                    }
                }
            }

            return new MT942TransactionDetails
            {
                RawInformation = raw,
                TypeCode = typeCode,
                Description = description,
                BeneficiaryInformation = ExtractSegment(compact, "/BENM/", "/REMI/", "/EXCH/", "/OCMT/"),
                RemittanceInformation = ExtractSegment(compact, "/REMI/", "/EXCH/", "/OCMT/"),
                ExchangeRate = exchangeRate,
                OriginalAmountCurrency = originalCurrency,
                OriginalAmount = originalAmount
            };
        }

        private static StatementSequence ParseStatementSequence(
            string? value,
            string path,
            ICollection<string> errors)
        {
            if (value is null)
            {
                return new StatementSequence(0, null);
            }

            var match = StatementSequencePattern().Match(value.Trim());
            if (!match.Success)
            {
                errors.Add($"{path}:28C must contain a 1 to 5 digit statement number and optional '/sequence' number.");
                return new StatementSequence(0, null);
            }

            return new StatementSequence(
                int.Parse(match.Groups["statement"].Value, CultureInfo.InvariantCulture),
                match.Groups["sequence"].Success
                    ? int.Parse(match.Groups["sequence"].Value, CultureInfo.InvariantCulture)
                    : null);
        }

        private static DateOnly? ParseSwiftDate(string value, string path, ICollection<string> errors)
        {
            if (value.Length != 6 || value.Any(character => !char.IsAsciiDigit(character)))
            {
                errors.Add($"{path} must contain a valid YYMMDD date.");
                return null;
            }

            var shortYear = int.Parse(value[..2], CultureInfo.InvariantCulture);
            var year = shortYear >= 70 ? 1900 + shortYear : 2000 + shortYear;
            try
            {
                return new DateOnly(
                    year,
                    int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture),
                    int.Parse(value.Substring(4, 2), CultureInfo.InvariantCulture));
            }
            catch (ArgumentOutOfRangeException)
            {
                errors.Add($"{path} must contain a valid YYMMDD date.");
                return null;
            }
        }

        private static DateOnly? ParseEntryDate(
            string value,
            DateOnly? valueDate,
            string path,
            ICollection<string> errors)
        {
            if (valueDate is null || value.Length != 4 || value.Any(character => !char.IsAsciiDigit(character)))
            {
                errors.Add($"{path} entry date must contain a valid MMDD date.");
                return null;
            }

            try
            {
                var result = new DateOnly(
                    valueDate.Value.Year,
                    int.Parse(value[..2], CultureInfo.InvariantCulture),
                    int.Parse(value[2..], CultureInfo.InvariantCulture));
                if (result > valueDate.Value.AddMonths(6)) result = result.AddYears(-1);
                else if (result < valueDate.Value.AddMonths(-6)) result = result.AddYears(1);
                return result;
            }
            catch (ArgumentOutOfRangeException)
            {
                errors.Add($"{path} entry date must contain a valid MMDD date.");
                return null;
            }
        }

        private static decimal? ParseSwiftAmount(string value, string path, ICollection<string> errors)
        {
            var normalized = value.Trim();
            var comma = normalized.IndexOf(',');
            if (normalized.Length is < 2 or > 15
                || comma <= 0
                || comma != normalized.LastIndexOf(',')
                || normalized[..comma].Any(character => !char.IsAsciiDigit(character))
                || normalized[(comma + 1)..].Any(character => !char.IsAsciiDigit(character))
                || !decimal.TryParse(
                    normalized.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                errors.Add($"{path} must be a maximum 15-character non-negative amount using a decimal comma.");
                return null;
            }

            return amount;
        }

        private static bool TryParseFlexibleAmount(string value, out decimal? amount)
        {
            if (decimal.TryParse(
                value.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
            {
                amount = parsed;
                return true;
            }

            amount = null;
            return false;
        }

        private static void ValidateFieldOrder(IReadOnlyList<RawField> fields, string path, ICollection<string> errors)
        {
            var orderedTags = new[] { "20", "25", "28C", "34F", "13D" };
            var previous = -1;
            foreach (var tag in orderedTags)
            {
                var field = fields.FirstOrDefault(candidate => candidate.Tag == tag);
                if (field is not null && field.Position <= previous)
                {
                    errors.Add($"{path}:{tag} is out of order.");
                }

                previous = field?.Position ?? previous;
            }

            var firstSummary = fields.FirstOrDefault(field => field.Tag is "90D" or "90C");
            if (firstSummary is not null && fields.Any(field => field.Tag == "61" && field.Position > firstSummary.Position))
            {
                errors.Add($"{path} entry summary fields must occur after all :61: transactions.");
            }
        }

        private static void ValidateOrphanInformationFields(
            IReadOnlyList<RawField> fields,
            string path,
            ICollection<string> errors)
        {
            for (var index = 0; index < fields.Count; index++)
            {
                if (fields[index].Tag == "86" && (index == 0 || fields[index - 1].Tag != "61"))
                {
                    errors.Add($"{path}:86 at field position {index + 1} must immediately follow a :61: transaction.");
                }
            }
        }

        private static RawField? GetRequiredField(
            IReadOnlyList<RawField> fields,
            string tag,
            string path,
            ICollection<string> errors)
        {
            var matches = fields.Where(field => field.Tag == tag).ToArray();
            if (matches.Length == 0)
            {
                errors.Add($"{path} is missing required tag :{tag}:.");
                return null;
            }

            if (matches.Length > 1) errors.Add($"{path} tag :{tag}: must occur only once.");
            if (string.IsNullOrWhiteSpace(matches[0].Value)) errors.Add($"{path} tag :{tag}: must not be empty.");
            return matches[0];
        }

        private static RawField? GetOptionalField(
            IReadOnlyList<RawField> fields,
            string tag,
            string path,
            ICollection<string> errors)
        {
            var matches = fields.Where(field => field.Tag == tag).ToArray();
            if (matches.Length > 1) errors.Add($"{path} tag :{tag}: must occur at most once.");
            if (matches.Length > 0 && string.IsNullOrWhiteSpace(matches[0].Value))
                errors.Add($"{path} tag :{tag}: must not be empty when present.");
            return matches.FirstOrDefault();
        }

        private static string? ExtractSegment(string value, string marker, params string[] nextMarkers)
        {
            var markerPosition = value.IndexOf(marker, StringComparison.Ordinal);
            if (markerPosition < 0) return null;
            var start = markerPosition + marker.Length;
            var end = FindNextMarker(value, start, nextMarkers);
            return NullIfWhiteSpace(value[start..end].Trim('/'));
        }

        private static int FindNextMarker(string value, int start, params string[] markers)
        {
            var result = value.Length;
            foreach (var marker in markers)
            {
                var position = value.IndexOf(marker, start, StringComparison.Ordinal);
                if (position >= 0 && position < result) result = position;
            }

            return result;
        }

        private static string NormalizeLines(string value)
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        }

        private static string CompactInformation(string value)
        {
            var lines = NormalizeLines(value).Split('\n');
            var result = new StringBuilder();

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                result.Append(line.TrimEnd());

                if (index + 1 < lines.Length
                    && line.Length > 0
                    && char.IsWhiteSpace(line[^1])
                    && !lines[index + 1].TrimStart().StartsWith('/'))
                {
                    result.Append(' ');
                }
            }

            return result.ToString().Trim();
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void SkipWhitespace(string value, ref int position)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position])) position++;
        }

        [GeneratedRegex("^(?<application>[A-Z])(?<service>[0-9]{2})(?<bank>[A-Z0-9]{12})(?<session>[0-9]{4})(?<sequence>[0-9]{6})$")]
        private static partial Regex BasicHeaderPattern();

        [GeneratedRegex("^:(?<tag>[0-9]{2}[A-Z]?):(?<value>.*)$")]
        private static partial Regex FieldPattern();

        [GeneratedRegex("^(?<statement>[0-9]{1,5})(?:/(?<sequence>[0-9]{1,5}))?$")]
        private static partial Regex StatementSequencePattern();

        private sealed record RawSwiftMessage(string BasicHeader, string ApplicationHeader, string TextBlock, string Trailer);

        private sealed record StatementSequence(int StatementNumber, int? SequenceNumber);

        private sealed class RawField
        {
            private string _value;

            public RawField(string tag, string value, int position)
            {
                Tag = tag;
                _value = value;
                Position = position;
            }

            public string Tag { get; }

            public string Value => _value;

            public int Position { get; }

            public void AppendContinuation(string line)
            {
                _value = string.Concat(_value, "\n", line);
            }
        }
    }
}
