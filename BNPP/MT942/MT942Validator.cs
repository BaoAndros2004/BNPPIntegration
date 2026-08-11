using System.Globalization;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.MT942.Models;

namespace BNPPIntegration.BNPP.MT942
{
    public sealed partial class MT942Validator
    {
        private static readonly HashSet<string> DebitIndicators = new(StringComparer.Ordinal)
        {
            "D", "RC", "ED"
        };

        private static readonly HashSet<string> CreditIndicators = new(StringComparer.Ordinal)
        {
            "C", "RD", "EC"
        };

        public MT942ValidationResult Validate(MT942Report? report)
        {
            var errors = new List<string>();
            if (report is null)
            {
                errors.Add("An MT942 report is required.");
                return new MT942ValidationResult(errors);
            }

            if (report.Statements.Count == 0)
            {
                errors.Add("The MT942 report must contain at least one statement.");
                return new MT942ValidationResult(errors);
            }

            var statementKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < report.Statements.Count; index++)
            {
                var statement = report.Statements[index];
                var path = $"Statements[{index}]";
                ValidateStatement(statement, path, errors);

                var key = string.Join(
                    "\u001f",
                    statement.AccountIdentification.Trim(),
                    statement.StatementNumber.ToString(CultureInfo.InvariantCulture),
                    statement.SequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                if (!statementKeys.Add(key))
                {
                    errors.Add($"{path} duplicates an earlier account, statement and sequence combination.");
                }
            }

            return new MT942ValidationResult(errors);
        }

        private static void ValidateStatement(
            MT942Statement statement,
            string path,
            ICollection<string> errors)
        {
            ValidateBasicHeader(statement.BasicHeader, path, errors);
            ValidateApplicationHeader(statement.ApplicationHeader, path, errors);
            ValidateRequiredText(statement.TransactionReference, $"{path}.TransactionReference", 16, errors);
            ValidateOptionalText(statement.RelatedReference, $"{path}.RelatedReference", 16, errors);
            ValidateRequiredText(statement.AccountIdentification, $"{path}.AccountIdentification", 35, errors);

            if (statement.AccountIdentification.Any(character => character is '\r' or '\n'))
            {
                errors.Add($"{path}.AccountIdentification must be on one line.");
            }

            if (statement.StatementNumber is < 1 or > 99999)
            {
                errors.Add($"{path}.StatementNumber must be between 1 and 99999.");
            }

            if (statement.SequenceNumber is < 1 or > 99999)
            {
                errors.Add($"{path}.SequenceNumber must be between 1 and 99999 when present.");
            }

            ValidateFloorLimits(statement.FloorLimits, path, errors);
            if (statement.ReportDateTime is null)
            {
                errors.Add($"{path}.ReportDateTime is required.");
            }

            ValidateTransactions(statement.Transactions, path, errors);
            ValidateSummary(statement.DebitSummary, "90D", DebitIndicators, statement, path, errors);
            ValidateSummary(statement.CreditSummary, "90C", CreditIndicators, statement, path, errors);
        }

        private static void ValidateBasicHeader(
            MT942BasicHeader header,
            string statementPath,
            ICollection<string> errors)
        {
            var path = $"{statementPath}.BasicHeader";
            if (header.ApplicationIdentifier != "F") errors.Add($"{path}.ApplicationIdentifier must be F.");
            if (header.ServiceIdentifier != "01") errors.Add($"{path}.ServiceIdentifier must be 01.");
            ValidateBankAddress(header.BankAddress, $"{path}.BankAddress", errors);
            if (!FourDigitPattern().IsMatch(header.SessionNumber))
                errors.Add($"{path}.SessionNumber must contain four digits.");
            if (!SixDigitPattern().IsMatch(header.SequenceNumber))
                errors.Add($"{path}.SequenceNumber must contain six digits.");
        }

        private static void ValidateApplicationHeader(
            MT942ApplicationHeader header,
            string statementPath,
            ICollection<string> errors)
        {
            var path = $"{statementPath}.ApplicationHeader";
            if (header.Direction is not ("I" or "O")) errors.Add($"{path}.Direction must be I or O.");
            if (header.MessageType != "942") errors.Add($"{path}.MessageType must be 942.");
            ValidateBankAddress(header.BankAddress, $"{path}.BankAddress", errors);
            if (header.Priority is not ("S" or "N" or "U")) errors.Add($"{path}.Priority must be S, N or U.");
        }

        private static void ValidateBankAddress(string value, string path, ICollection<string> errors)
        {
            if (!BankAddressPattern().IsMatch(value))
            {
                errors.Add($"{path} must be a valid 12-character SWIFT logical terminal address.");
            }
        }

        private static void ValidateFloorLimits(
            IReadOnlyList<MT942FloorLimit> floorLimits,
            string statementPath,
            ICollection<string> errors)
        {
            var path = $"{statementPath}.FloorLimits";
            if (floorLimits.Count is < 1 or > 2)
            {
                errors.Add($"{path} must contain one or two floor limits.");
                return;
            }

            var indicators = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < floorLimits.Count; index++)
            {
                var limit = floorLimits[index];
                var limitPath = $"{path}[{index}]";
                if (!CurrencyPattern().IsMatch(limit.Currency))
                    errors.Add($"{limitPath}.Currency must be a three-letter uppercase ISO currency code.");
                if (limit.CreditDebitIndicator is not null && limit.CreditDebitIndicator is not ("C" or "D"))
                    errors.Add($"{limitPath}.CreditDebitIndicator must be C or D when present.");
                ValidateAmount(limit.Amount, $"{limitPath}.Amount", errors);

                if (limit.CreditDebitIndicator is not null && !indicators.Add(limit.CreditDebitIndicator))
                    errors.Add($"{limitPath}.CreditDebitIndicator duplicates an earlier floor limit.");
            }

            if (floorLimits.Select(limit => limit.Currency).Distinct(StringComparer.Ordinal).Skip(1).Any())
                errors.Add($"{path} must use one currency.");
            if (floorLimits.Count == 2 && floorLimits.Any(limit => limit.CreditDebitIndicator is null))
                errors.Add($"{path} must identify debit and credit when two floor limits are present.");
        }

        private static void ValidateTransactions(
            IReadOnlyList<MT942Transaction> transactions,
            string statementPath,
            ICollection<string> errors)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < transactions.Count; index++)
            {
                var transaction = transactions[index];
                var path = $"{statementPath}.Transactions[{index}]";
                if (transaction.ValueDate is null) errors.Add($"{path}.ValueDate is required.");
                if (!DebitIndicators.Contains(transaction.CreditDebitIndicator)
                    && !CreditIndicators.Contains(transaction.CreditDebitIndicator))
                    errors.Add($"{path}.CreditDebitIndicator must be C, D, RC, RD, EC or ED.");
                if (transaction.FundsCode is not null
                    && (transaction.FundsCode.Length != 1 || !char.IsAsciiLetter(transaction.FundsCode[0])))
                    errors.Add($"{path}.FundsCode must be one letter when present.");
                ValidateAmount(transaction.Amount, $"{path}.Amount", errors);
                if (!TransactionTypePattern().IsMatch(transaction.TransactionTypeCode))
                    errors.Add($"{path}.TransactionTypeCode must contain three uppercase letters or digits.");
                ValidateRequiredText(transaction.CustomerReference, $"{path}.CustomerReference", 16, errors);
                ValidateOptionalText(transaction.BankReference, $"{path}.BankReference", 16, errors);
                ValidateOptionalText(transaction.SupplementaryDetails, $"{path}.SupplementaryDetails", 34, errors);
                if (transaction.Details is not null) ValidateDetails(transaction.Details, path, errors);

                var key = string.Join(
                    "\u001f",
                    transaction.ValueDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
                    transaction.CreditDebitIndicator,
                    transaction.Amount.ToString(CultureInfo.InvariantCulture),
                    transaction.CustomerReference,
                    transaction.BankReference ?? string.Empty);
                if (!keys.Add(key)) errors.Add($"{path} duplicates an earlier transaction in the statement.");
            }
        }

        private static void ValidateDetails(
            MT942TransactionDetails details,
            string transactionPath,
            ICollection<string> errors)
        {
            var path = $"{transactionPath}.Details";
            var lines = details.RawInformation.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            if (lines.Length > 6 || lines.Any(line => line.Length > 65))
                errors.Add($"{path}.RawInformation cannot exceed six lines of 65 characters.");
            ValidateOptionalText(details.TypeCode, $"{path}.TypeCode", 16, errors);
            if (details.ExchangeRate < 0) errors.Add($"{path}.ExchangeRate cannot be negative.");
            if (details.OriginalAmountCurrency is null != (details.OriginalAmount is null))
                errors.Add($"{path}.OriginalAmountCurrency and OriginalAmount must be supplied together.");
            if (details.OriginalAmountCurrency is not null && !CurrencyPattern().IsMatch(details.OriginalAmountCurrency))
                errors.Add($"{path}.OriginalAmountCurrency must be a three-letter uppercase ISO currency code.");
            if (details.OriginalAmount < 0) errors.Add($"{path}.OriginalAmount cannot be negative.");
        }

        private static void ValidateSummary(
            MT942EntrySummary? summary,
            string expectedType,
            IReadOnlySet<string> indicators,
            MT942Statement statement,
            string statementPath,
            ICollection<string> errors)
        {
            if (summary is null) return;
            var path = $"{statementPath}.{(expectedType == "90D" ? "DebitSummary" : "CreditSummary")}";
            if (summary.EntryType != expectedType) errors.Add($"{path}.EntryType must be {expectedType}.");
            if (summary.NumberOfEntries < 0 || summary.NumberOfEntries > 99999)
                errors.Add($"{path}.NumberOfEntries must be between 0 and 99999.");
            if (!CurrencyPattern().IsMatch(summary.Currency))
                errors.Add($"{path}.Currency must be a three-letter uppercase ISO currency code.");
            ValidateAmount(summary.Amount, $"{path}.Amount", errors);

            var matchingTransactions = statement.Transactions
                .Where(transaction => indicators.Contains(transaction.CreditDebitIndicator))
                .ToArray();
            if (summary.NumberOfEntries != matchingTransactions.Length)
                errors.Add($"{path}.NumberOfEntries does not match the reported transactions.");
            if (summary.Amount != matchingTransactions.Sum(transaction => transaction.Amount))
                errors.Add($"{path}.Amount does not match the reported transactions.");

            var statementCurrency = statement.FloorLimits.FirstOrDefault()?.Currency;
            if (statementCurrency is not null && summary.Currency != statementCurrency)
                errors.Add($"{path}.Currency must match the floor-limit currency.");
        }

        private static void ValidateAmount(decimal amount, string path, ICollection<string> errors)
        {
            if (amount < 0)
            {
                errors.Add($"{path} cannot be negative; use the debit/credit indicator for its sign.");
                return;
            }

            if (amount.ToString(CultureInfo.InvariantCulture).Count(char.IsAsciiDigit) + 1 > 15)
                errors.Add($"{path} cannot exceed the MT942 15-character amount field.");
        }

        private static void ValidateRequiredText(
            string? value,
            string path,
            int maximumLength,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{path} is required.");
            else if (value.Length > maximumLength) errors.Add($"{path} cannot exceed {maximumLength} characters.");
        }

        private static void ValidateOptionalText(
            string? value,
            string path,
            int maximumLength,
            ICollection<string> errors)
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength))
                errors.Add($"{path} must contain between 1 and {maximumLength} characters when present.");
        }

        [GeneratedRegex("^[A-Z]{6}[A-Z0-9]{2}[A-Z0-9][A-Z0-9]{3}$")]
        private static partial Regex BankAddressPattern();

        [GeneratedRegex("^[A-Z]{3}$")]
        private static partial Regex CurrencyPattern();

        [GeneratedRegex("^[A-Z0-9]{3}$")]
        private static partial Regex TransactionTypePattern();

        [GeneratedRegex("^[0-9]{4}$")]
        private static partial Regex FourDigitPattern();

        [GeneratedRegex("^[0-9]{6}$")]
        private static partial Regex SixDigitPattern();
    }
}
