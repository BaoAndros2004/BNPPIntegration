using System.Globalization;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.BankReports.MT940.Models;

namespace BNPPIntegration.BNPP.BankReports.MT940
{
    public sealed partial class MT940Parser
    {
        private static readonly HashSet<string> SupportedTags = new(StringComparer.Ordinal)
        {
            "20", "21", "25", "28C", "60F", "60M", "61", "86", "62F", "62M", "64", "65"
        };

        public MT940ParseResult Parse(string content, string? sourceName = null)
        {
            return Parse(new MT940RawMessage
            {
                SourceName = sourceName ?? string.Empty,
                Content = content
            });
        }

        public MT940ParseResult Parse(MT940RawMessage rawMessage)
        {
            ArgumentNullException.ThrowIfNull(rawMessage);

            if (string.IsNullOrWhiteSpace(rawMessage.Content))
            {
                return MT940ParseResult.Failure(new[] { "The MT940 input is empty." });
            }

            var errors = new List<string>();
            var rawStatements = ParseSwiftMessages(rawMessage.Content, errors);
            var statements = rawStatements
                .Select((message, index) => ParseStatement(message, index, errors))
                .ToArray();

            if (errors.Count > 0)
            {
                return MT940ParseResult.Failure(errors);
            }

            return MT940ParseResult.Success(new MT940Report
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
                errors.Add("The MT940 input does not contain a SWIFT message.");
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

        private static MT940Statement ParseStatement(
            RawSwiftMessage message,
            int statementIndex,
            ICollection<string> errors)
        {
            var path = $"Statements[{statementIndex}]";
            var basicHeader = ParseBasicHeader(message.BasicHeader, path, errors);
            var applicationHeader = ParseApplicationHeader(message.ApplicationHeader, path, errors);
            var fields = ParseTextFields(message.TextBlock, path, errors);

            var transactionReference = GetRequiredField(fields, "20", path, errors)?.Value.Trim() ?? string.Empty;
            var relatedReference = GetOptionalField(fields, "21", path, errors)?.Value.Trim();
            var accountIdentification = GetRequiredField(fields, "25", path, errors)?.Value.Trim() ?? string.Empty;
            var statementSequence = ParseStatementSequence(
                GetRequiredField(fields, "28C", path, errors)?.Value,
                path,
                errors);

            var openingField = GetRequiredChoiceField(fields, "60F", "60M", path, errors);
            var closingField = GetRequiredChoiceField(fields, "62F", "62M", path, errors);
            var openingBalance = openingField is null
                ? new MT940Balance()
                : ParseBalance(openingField.Tag, openingField.Value, $"{path}:{openingField.Tag}", errors);
            var closingBalance = closingField is null
                ? new MT940Balance()
                : ParseBalance(closingField.Tag, closingField.Value, $"{path}:{closingField.Tag}", errors);
            var closingAvailableField = GetOptionalField(fields, "64", path, errors);
            var closingAvailableBalance = closingAvailableField is null
                ? null
                : ParseBalance("64", closingAvailableField.Value, $"{path}:64", errors);
            var forwardAvailableBalances = fields
                .Where(field => field.Tag == "65")
                .Select((field, index) => ParseBalance("65", field.Value, $"{path}:65[{index}]", errors))
                .ToArray();

            ValidateFieldOrder(fields, openingField, closingField, path, errors);
            var transactions = ParseTransactions(fields, openingField, closingField, path, errors);
            var informationToAccountOwner = ParseStatementInformation(fields, closingField);

            if (!string.IsNullOrWhiteSpace(message.Trailer))
            {
                errors.Add($"{path} trailer block must be empty for the BNPP MT940 format.");
            }

            return new MT940Statement
            {
                BasicHeader = basicHeader,
                ApplicationHeader = applicationHeader,
                TransactionReference = transactionReference,
                RelatedReference = NullIfWhiteSpace(relatedReference),
                AccountIdentification = accountIdentification,
                StatementNumber = statementSequence.StatementNumber,
                SequenceNumber = statementSequence.SequenceNumber,
                OpeningBalance = openingBalance,
                Transactions = transactions,
                ClosingBalance = closingBalance,
                ClosingAvailableBalance = closingAvailableBalance,
                ForwardAvailableBalances = forwardAvailableBalances,
                InformationToAccountOwner = informationToAccountOwner,
                Trailer = message.Trailer
            };
        }

        private static MT940BasicHeader ParseBasicHeader(string value, string path, ICollection<string> errors)
        {
            var match = BasicHeaderPattern().Match(value);
            if (!match.Success)
            {
                errors.Add($"{path} basic header must contain application id, service id, 12-character bank address, session and sequence numbers.");
                return new MT940BasicHeader();
            }

            return new MT940BasicHeader
            {
                ApplicationIdentifier = match.Groups["application"].Value,
                ServiceIdentifier = match.Groups["service"].Value,
                BankAddress = match.Groups["bank"].Value,
                SessionNumber = match.Groups["session"].Value,
                SequenceNumber = match.Groups["sequence"].Value
            };
        }

        private static MT940ApplicationHeader ParseApplicationHeader(
            string value,
            string path,
            ICollection<string> errors)
        {
            if (value.Length < 4)
            {
                errors.Add($"{path} application header is too short.");
                return new MT940ApplicationHeader { RawValue = value };
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
                    return new MT940ApplicationHeader
                    {
                        Direction = direction,
                        MessageType = messageType,
                        RawValue = value
                    };
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
                    return new MT940ApplicationHeader
                    {
                        Direction = direction,
                        MessageType = messageType,
                        RawValue = value
                    };
                }

                bankAddress = value.Substring(14, 12);
                priority = value[^1..];
            }
            else
            {
                errors.Add($"{path} application header direction must be I or O.");
                return new MT940ApplicationHeader
                {
                    Direction = direction,
                    MessageType = messageType,
                    RawValue = value
                };
            }

            return new MT940ApplicationHeader
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
            var lines = textBlock.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

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
                    errors.Add($"{path} contains a malformed MT940 tag on text-block line {lineIndex + 1}.");
                    continue;
                }

                if (fields.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        errors.Add($"{path} contains text before the first MT940 tag on text-block line {lineIndex + 1}.");
                    }

                    continue;
                }

                fields[^1].AppendContinuation(line);
            }

            return fields;
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
            if (!match.Success
                || !int.TryParse(match.Groups["statement"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var statementNumber))
            {
                errors.Add($"{path}:28C must contain a 1 to 5 digit statement number and optional '/sequence' number.");
                return new StatementSequence(0, null);
            }

            int? sequenceNumber = null;
            if (match.Groups["sequence"].Success)
            {
                sequenceNumber = int.Parse(match.Groups["sequence"].Value, CultureInfo.InvariantCulture);
            }

            return new StatementSequence(statementNumber, sequenceNumber);
        }

        private static MT940Balance ParseBalance(
            string balanceType,
            string value,
            string path,
            ICollection<string> errors)
        {
            var normalizedValue = value.Trim();
            if (normalizedValue.Length < 11)
            {
                errors.Add($"{path} is too short to contain indicator, date, currency and amount.");
                return new MT940Balance { BalanceType = balanceType };
            }

            var indicator = normalizedValue[..1];
            var date = ParseSwiftDate(normalizedValue.Substring(1, 6), path, errors);
            var currency = normalizedValue.Substring(7, 3);
            var amount = ParseSwiftAmount(normalizedValue[10..], path, errors);

            return new MT940Balance
            {
                BalanceType = balanceType,
                CreditDebitIndicator = indicator,
                Date = date,
                Currency = currency,
                Amount = amount ?? 0
            };
        }

        private static IReadOnlyList<MT940Transaction> ParseTransactions(
            IReadOnlyList<RawField> fields,
            RawField? openingField,
            RawField? closingField,
            string path,
            ICollection<string> errors)
        {
            var transactions = new List<MT940Transaction>();
            var openingIndex = openingField?.Position ?? -1;
            var closingIndex = closingField?.Position ?? int.MaxValue;

            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                if (field.Tag != "61")
                {
                    continue;
                }

                if (field.Position <= openingIndex || field.Position >= closingIndex)
                {
                    errors.Add($"{path}:61[{transactions.Count}] must occur between the opening and closing balances.");
                }

                RawField? detailsField = null;
                if (fieldIndex + 1 < fields.Count && fields[fieldIndex + 1].Tag == "86")
                {
                    detailsField = fields[++fieldIndex];
                }

                transactions.Add(ParseTransaction(
                    field.Value,
                    detailsField?.Value,
                    $"{path}:61[{transactions.Count}]",
                    errors));
            }

            return transactions;
        }

        private static MT940Transaction ParseTransaction(
            string value,
            string? information,
            string path,
            ICollection<string> errors)
        {
            var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var statementLine = lines[0].TrimEnd();
            var supplementaryDetails = lines.Length > 1
                ? NullIfWhiteSpace(string.Join("\n", lines.Skip(1)).TrimEnd())
                : null;

            if (statementLine.Length < 12)
            {
                errors.Add($"{path} is too short.");
                return new MT940Transaction
                {
                    SupplementaryDetails = supplementaryDetails,
                    Details = information is null ? null : ParseTransactionDetails(information, path, errors)
                };
            }

            var position = 0;
            var valueDate = ParseSwiftDate(statementLine.Substring(position, 6), $"{path} value date", errors);
            position += 6;

            DateOnly? entryDate = null;
            if (statementLine.Length >= position + 4
                && statementLine.AsSpan(position, 4).ToArray().All(char.IsAsciiDigit))
            {
                entryDate = ParseEntryDate(statementLine.Substring(position, 4), valueDate, path, errors);
                position += 4;
            }

            string indicator;
            if (statementLine.AsSpan(position).StartsWith("RD", StringComparison.Ordinal)
                || statementLine.AsSpan(position).StartsWith("RC", StringComparison.Ordinal))
            {
                indicator = statementLine.Substring(position, 2);
                position += 2;
            }
            else if (position < statementLine.Length && statementLine[position] is 'D' or 'C')
            {
                indicator = statementLine.Substring(position, 1);
                position++;
            }
            else
            {
                errors.Add($"{path} must contain D, C, RD or RC after its dates.");
                indicator = string.Empty;
            }

            string? fundsCode = null;
            if (position < statementLine.Length && char.IsAsciiLetter(statementLine[position]))
            {
                fundsCode = statementLine.Substring(position, 1);
                position++;
            }

            var transactionTypePosition = statementLine.IndexOf('N', position);
            if (transactionTypePosition < 0 || transactionTypePosition + 4 > statementLine.Length)
            {
                errors.Add($"{path} must contain an N-prefixed three-character transaction type code.");
                return new MT940Transaction
                {
                    ValueDate = valueDate,
                    EntryDate = entryDate,
                    CreditDebitIndicator = indicator,
                    FundsCode = fundsCode,
                    SupplementaryDetails = supplementaryDetails,
                    Details = information is null ? null : ParseTransactionDetails(information, path, errors)
                };
            }

            var amount = ParseSwiftAmount(statementLine[position..transactionTypePosition], $"{path} amount", errors);
            var transactionTypeCode = statementLine.Substring(transactionTypePosition + 1, 3);
            var references = statementLine[(transactionTypePosition + 4)..];
            var referenceSeparator = references.IndexOf("//", StringComparison.Ordinal);
            var customerReference = (referenceSeparator < 0 ? references : references[..referenceSeparator]).Trim();
            var bankReference = referenceSeparator < 0
                ? null
                : NullIfWhiteSpace(references[(referenceSeparator + 2)..].Trim());

            if (customerReference.Length == 0)
            {
                errors.Add($"{path} customer reference is missing.");
            }

            return new MT940Transaction
            {
                ValueDate = valueDate,
                EntryDate = entryDate,
                CreditDebitIndicator = indicator,
                FundsCode = fundsCode,
                Amount = amount ?? 0,
                TransactionTypeCode = transactionTypeCode,
                CustomerReference = customerReference,
                BankReference = bankReference,
                SupplementaryDetails = supplementaryDetails,
                Details = information is null ? null : ParseTransactionDetails(information, path, errors)
            };
        }

        private static MT940TransactionDetails ParseTransactionDetails(
            string value,
            string path,
            ICollection<string> errors)
        {
            var rawInformation = value.TrimEnd();
            var compactInformation = string.Concat(
                value.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(line => line.TrimEnd())).Trim();

            string? typeCode = null;
            string? description = null;
            var typeMarker = compactInformation.IndexOf("/TYPE/", StringComparison.Ordinal);
            if (typeMarker >= 0)
            {
                var typeStart = typeMarker + "/TYPE/".Length;
                var typeEnd = compactInformation.IndexOf('/', typeStart);
                if (typeEnd < 0)
                {
                    typeCode = NullIfWhiteSpace(compactInformation[typeStart..]);
                }
                else
                {
                    typeCode = NullIfWhiteSpace(compactInformation[typeStart..typeEnd]);
                    var descriptionEnd = FindNextMarker(
                        compactInformation,
                        typeEnd,
                        "/BENM/", "/REMI/", "/EXCH/", "/OCMT/");
                    description = NullIfWhiteSpace(compactInformation[(typeEnd + 1)..descriptionEnd].Trim('/'));
                }
            }

            var exchangeRateText = ExtractSegment(compactInformation, "/EXCH/", "/OCMT/");
            decimal? exchangeRate = null;
            if (exchangeRateText is not null
                && !TryParseFlexibleAmount(exchangeRateText, out exchangeRate))
            {
                errors.Add($"{path}:86 /EXCH/ must contain a valid exchange rate.");
            }

            var originalAmountText = ExtractSegment(compactInformation, "/OCMT/");
            string? originalCurrency = null;
            decimal? originalAmount = null;
            if (originalAmountText is not null)
            {
                if (originalAmountText.Length < 4)
                {
                    errors.Add($"{path}:86 /OCMT/ must contain a currency and amount.");
                }
                else
                {
                    originalCurrency = originalAmountText[..3];
                    if (!TryParseFlexibleAmount(originalAmountText[3..], out originalAmount))
                    {
                        errors.Add($"{path}:86 /OCMT/ must contain a valid amount after its currency.");
                    }
                }
            }

            return new MT940TransactionDetails
            {
                RawInformation = rawInformation,
                TypeCode = typeCode,
                Description = description,
                BeneficiaryInformation = ExtractSegment(
                    compactInformation,
                    "/BENM/", "/REMI/", "/EXCH/", "/OCMT/"),
                RemittanceInformation = ExtractSegment(compactInformation, "/REMI/", "/EXCH/", "/OCMT/"),
                ExchangeRate = exchangeRate,
                OriginalAmountCurrency = originalCurrency,
                OriginalAmount = originalAmount
            };
        }

        private static IReadOnlyList<string> ParseStatementInformation(
            IReadOnlyList<RawField> fields,
            RawField? closingField)
        {
            var closingPosition = closingField?.Position ?? int.MaxValue;
            var transactionInformationPositions = new HashSet<int>(
                fields.Where((field, index) => field.Tag == "86" && index > 0 && fields[index - 1].Tag == "61")
                    .Select(field => field.Position));

            return fields
                .Where(field => field.Tag == "86"
                    && !transactionInformationPositions.Contains(field.Position)
                    && field.Position > closingPosition)
                .Select(field => field.Value.TrimEnd())
                .ToArray();
        }

        private static void ValidateFieldOrder(
            IReadOnlyList<RawField> fields,
            RawField? openingField,
            RawField? closingField,
            string path,
            ICollection<string> errors)
        {
            var requiredOrder = new[] { "20", "25", "28C" };
            var previousPosition = -1;
            foreach (var tag in requiredOrder)
            {
                var field = fields.FirstOrDefault(candidate => candidate.Tag == tag);
                if (field is not null && field.Position <= previousPosition)
                {
                    errors.Add($"{path}:{tag} is out of order.");
                }

                previousPosition = field?.Position ?? previousPosition;
            }

            if (openingField is not null && openingField.Position <= previousPosition)
            {
                errors.Add($"{path}:{openingField.Tag} must occur after :28C:.");
            }

            if (openingField is not null && closingField is not null && closingField.Position <= openingField.Position)
            {
                errors.Add($"{path}:{closingField.Tag} must occur after the opening balance.");
            }
        }

        private static RawField? GetRequiredField(
            IReadOnlyList<RawField> fields,
            string tag,
            string path,
            ICollection<string> errors)
        {
            var matchingFields = fields.Where(field => field.Tag == tag).ToArray();
            if (matchingFields.Length == 0)
            {
                errors.Add($"{path} is missing required tag :{tag}:.");
                return null;
            }

            if (matchingFields.Length > 1)
            {
                errors.Add($"{path} tag :{tag}: must occur only once.");
            }

            if (string.IsNullOrWhiteSpace(matchingFields[0].Value))
            {
                errors.Add($"{path} tag :{tag}: must not be empty.");
            }

            return matchingFields[0];
        }

        private static RawField? GetOptionalField(
            IReadOnlyList<RawField> fields,
            string tag,
            string path,
            ICollection<string> errors)
        {
            var matchingFields = fields.Where(field => field.Tag == tag).ToArray();
            if (matchingFields.Length > 1)
            {
                errors.Add($"{path} tag :{tag}: must occur at most once.");
            }

            if (matchingFields.Length > 0 && string.IsNullOrWhiteSpace(matchingFields[0].Value))
            {
                errors.Add($"{path} tag :{tag}: must not be empty when present.");
            }

            return matchingFields.FirstOrDefault();
        }

        private static RawField? GetRequiredChoiceField(
            IReadOnlyList<RawField> fields,
            string firstTag,
            string secondTag,
            string path,
            ICollection<string> errors)
        {
            var matchingFields = fields.Where(field => field.Tag == firstTag || field.Tag == secondTag).ToArray();
            if (matchingFields.Length == 0)
            {
                errors.Add($"{path} must contain either :{firstTag}: or :{secondTag}:.");
                return null;
            }

            if (matchingFields.Length > 1)
            {
                errors.Add($"{path} must contain only one :{firstTag}: or :{secondTag}: field.");
            }

            return matchingFields[0];
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
            var month = int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture);
            var day = int.Parse(value.Substring(4, 2), CultureInfo.InvariantCulture);

            try
            {
                return new DateOnly(year, month, day);
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

            var month = int.Parse(value[..2], CultureInfo.InvariantCulture);
            var day = int.Parse(value[2..], CultureInfo.InvariantCulture);

            try
            {
                var entryDate = new DateOnly(valueDate.Value.Year, month, day);
                if (entryDate > valueDate.Value.AddMonths(6))
                {
                    entryDate = entryDate.AddYears(-1);
                }
                else if (entryDate < valueDate.Value.AddMonths(-6))
                {
                    entryDate = entryDate.AddYears(1);
                }

                return entryDate;
            }
            catch (ArgumentOutOfRangeException)
            {
                errors.Add($"{path} entry date must contain a valid MMDD date.");
                return null;
            }
        }

        private static decimal? ParseSwiftAmount(string value, string path, ICollection<string> errors)
        {
            var normalizedValue = value.Trim();
            var commaIndex = normalizedValue.IndexOf(',');
            if (normalizedValue.Length is < 2 or > 15
                || commaIndex <= 0
                || commaIndex != normalizedValue.LastIndexOf(',')
                || normalizedValue[..commaIndex].Any(character => !char.IsAsciiDigit(character))
                || normalizedValue[(commaIndex + 1)..].Any(character => !char.IsAsciiDigit(character))
                || !decimal.TryParse(
                    normalizedValue.Replace(',', '.'),
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
            var normalizedValue = value.Trim().Replace(',', '.');
            if (decimal.TryParse(
                normalizedValue,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedAmount))
            {
                amount = parsedAmount;
                return true;
            }

            amount = null;
            return false;
        }

        private static string? ExtractSegment(string value, string marker, params string[] nextMarkers)
        {
            var markerPosition = value.IndexOf(marker, StringComparison.Ordinal);
            if (markerPosition < 0)
            {
                return null;
            }

            var valueStart = markerPosition + marker.Length;
            var valueEnd = FindNextMarker(value, valueStart, nextMarkers);
            return NullIfWhiteSpace(value[valueStart..valueEnd].Trim('/'));
        }

        private static int FindNextMarker(string value, int startIndex, params string[] markers)
        {
            var result = value.Length;
            foreach (var marker in markers)
            {
                var position = value.IndexOf(marker, startIndex, StringComparison.Ordinal);
                if (position >= 0 && position < result)
                {
                    result = position;
                }
            }

            return result;
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void SkipWhitespace(string value, ref int position)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position]))
            {
                position++;
            }
        }

        [GeneratedRegex("^(?<application>[A-Z])(?<service>[0-9]{2})(?<bank>[A-Z0-9]{12})(?<session>[0-9]{4})(?<sequence>[0-9]{6})$")]
        private static partial Regex BasicHeaderPattern();

        [GeneratedRegex("^:(?<tag>[0-9]{2}[A-Z]?):(?<value>.*)$")]
        private static partial Regex FieldPattern();

        [GeneratedRegex("^(?<statement>[0-9]{1,5})(?:/(?<sequence>[0-9]{1,5}))?$")]
        private static partial Regex StatementSequencePattern();

        private sealed record RawSwiftMessage(
            string BasicHeader,
            string ApplicationHeader,
            string TextBlock,
            string Trailer);

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
