using System.Globalization;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.MT940.Models;

namespace BNPPIntegration.BNPP.MT940
{
    public sealed partial class MT940Validator
    {
        public MT940ValidationResult Validate(MT940Report? report)
        {
            var errors = new List<string>();
            if (report is null)
            {
                errors.Add("An MT940 report is required.");
                return new MT940ValidationResult(errors);
            }

            if (report.Statements.Count == 0)
            {
                errors.Add("The MT940 report must contain at least one statement.");
                return new MT940ValidationResult(errors);
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

            return new MT940ValidationResult(errors);
        }

        private static void ValidateStatement(
            MT940Statement statement,
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

            ValidateBalance(statement.OpeningBalance, $"{path}.OpeningBalance", new[] { "60F", "60M" }, errors);
            ValidateBalance(statement.ClosingBalance, $"{path}.ClosingBalance", new[] { "62F", "62M" }, errors);

            if (statement.ClosingAvailableBalance is not null)
            {
                ValidateBalance(statement.ClosingAvailableBalance, $"{path}.ClosingAvailableBalance", new[] { "64" }, errors);
            }

            for (var index = 0; index < statement.ForwardAvailableBalances.Count; index++)
            {
                ValidateBalance(
                    statement.ForwardAvailableBalances[index],
                    $"{path}.ForwardAvailableBalances[{index}]",
                    new[] { "65" },
                    errors);
            }

            ValidateBalanceConsistency(statement, path, errors);
            ValidateTransactions(statement, path, errors);
            ValidateStatementInformation(statement.InformationToAccountOwner, path, errors);
        }

        private static void ValidateBasicHeader(
            MT940BasicHeader header,
            string statementPath,
            ICollection<string> errors)
        {
            var path = $"{statementPath}.BasicHeader";
            if (header.ApplicationIdentifier != "F")
            {
                errors.Add($"{path}.ApplicationIdentifier must be F.");
            }

            if (header.ServiceIdentifier != "01")
            {
                errors.Add($"{path}.ServiceIdentifier must be 01.");
            }

            ValidateBankAddress(header.BankAddress, $"{path}.BankAddress", errors);

            if (!FourDigitPattern().IsMatch(header.SessionNumber))
            {
                errors.Add($"{path}.SessionNumber must contain four digits.");
            }

            if (!SixDigitPattern().IsMatch(header.SequenceNumber))
            {
                errors.Add($"{path}.SequenceNumber must contain six digits.");
            }
        }

        private static void ValidateApplicationHeader(
            MT940ApplicationHeader header,
            string statementPath,
            ICollection<string> errors)
        {
            var path = $"{statementPath}.ApplicationHeader";
            if (header.Direction is not ("I" or "O"))
            {
                errors.Add($"{path}.Direction must be I or O.");
            }

            if (header.MessageType != "940")
            {
                errors.Add($"{path}.MessageType must be 940.");
            }

            ValidateBankAddress(header.BankAddress, $"{path}.BankAddress", errors);

            if (header.Priority is not ("S" or "N" or "U"))
            {
                errors.Add($"{path}.Priority must be S, N or U.");
            }
        }

        private static void ValidateBankAddress(string value, string path, ICollection<string> errors)
        {
            if (!BankAddressPattern().IsMatch(value))
            {
                errors.Add($"{path} must be a valid 12-character SWIFT logical terminal address.");
            }
        }

        private static void ValidateBalance(
            MT940Balance balance,
            string path,
            IReadOnlyCollection<string> expectedTypes,
            ICollection<string> errors)
        {
            if (!expectedTypes.Contains(balance.BalanceType))
            {
                errors.Add($"{path}.BalanceType must be {string.Join(" or ", expectedTypes)}.");
            }

            if (balance.CreditDebitIndicator is not ("C" or "D"))
            {
                errors.Add($"{path}.CreditDebitIndicator must be C or D.");
            }

            if (balance.Date is null)
            {
                errors.Add($"{path}.Date is required.");
            }

            if (!CurrencyPattern().IsMatch(balance.Currency))
            {
                errors.Add($"{path}.Currency must be a three-letter uppercase ISO currency code.");
            }

            ValidateAmount(balance.Amount, $"{path}.Amount", errors);
        }

        private static void ValidateTransactions(
            MT940Statement statement,
            string statementPath,
            ICollection<string> errors)
        {
            var transactionKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < statement.Transactions.Count; index++)
            {
                var transaction = statement.Transactions[index];
                var path = $"{statementPath}.Transactions[{index}]";

                if (transaction.ValueDate is null)
                {
                    errors.Add($"{path}.ValueDate is required.");
                }

                if (transaction.CreditDebitIndicator is not ("C" or "D" or "RC" or "RD"))
                {
                    errors.Add($"{path}.CreditDebitIndicator must be C, D, RC or RD.");
                }

                if (transaction.FundsCode is not null
                    && (transaction.FundsCode.Length != 1 || !char.IsAsciiLetter(transaction.FundsCode[0])))
                {
                    errors.Add($"{path}.FundsCode must be one letter when present.");
                }

                ValidateAmount(transaction.Amount, $"{path}.Amount", errors);

                if (!TransactionTypePattern().IsMatch(transaction.TransactionTypeCode))
                {
                    errors.Add($"{path}.TransactionTypeCode must contain three uppercase letters or digits.");
                }

                ValidateRequiredText(transaction.CustomerReference, $"{path}.CustomerReference", 16, errors);
                ValidateOptionalText(transaction.BankReference, $"{path}.BankReference", 16, errors);
                ValidateOptionalText(transaction.SupplementaryDetails, $"{path}.SupplementaryDetails", 34, errors);

                if (transaction.Details is not null)
                {
                    ValidateTransactionDetails(transaction.Details, path, errors);
                }

                var key = string.Join(
                    "\u001f",
                    transaction.ValueDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
                    transaction.CreditDebitIndicator,
                    transaction.Amount.ToString(CultureInfo.InvariantCulture),
                    transaction.CustomerReference,
                    transaction.BankReference ?? string.Empty);
                if (!transactionKeys.Add(key))
                {
                    errors.Add($"{path} duplicates an earlier transaction in the statement.");
                }
            }
        }

        private static void ValidateTransactionDetails(
            MT940TransactionDetails details,
            string transactionPath,
            ICollection<string> errors)
        {
            var path = $"{transactionPath}.Details";
            var lines = details.RawInformation
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            if (lines.Length > 6 || lines.Any(line => line.Length > 65))
            {
                errors.Add($"{path}.RawInformation cannot exceed six lines of 65 characters.");
            }

            ValidateOptionalText(details.TypeCode, $"{path}.TypeCode", 16, errors);

            if (details.ExchangeRate < 0)
            {
                errors.Add($"{path}.ExchangeRate cannot be negative.");
            }

            if (details.OriginalAmountCurrency is null != (details.OriginalAmount is null))
            {
                errors.Add($"{path}.OriginalAmountCurrency and OriginalAmount must be supplied together.");
            }

            if (details.OriginalAmountCurrency is not null && !CurrencyPattern().IsMatch(details.OriginalAmountCurrency))
            {
                errors.Add($"{path}.OriginalAmountCurrency must be a three-letter uppercase ISO currency code.");
            }

            if (details.OriginalAmount < 0)
            {
                errors.Add($"{path}.OriginalAmount cannot be negative.");
            }
        }

        private static void ValidateBalanceConsistency(
            MT940Statement statement,
            string path,
            ICollection<string> errors)
        {
            var balances = new[] { statement.OpeningBalance, statement.ClosingBalance }
                .Concat(statement.ClosingAvailableBalance is null
                    ? Array.Empty<MT940Balance>()
                    : new[] { statement.ClosingAvailableBalance })
                .Concat(statement.ForwardAvailableBalances)
                .ToArray();

            if (balances.Select(balance => balance.Currency).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                errors.Add($"{path} balances must use the same currency.");
            }

            if (statement.OpeningBalance.Date is not null
                && statement.ClosingBalance.Date is not null
                && statement.ClosingBalance.Date < statement.OpeningBalance.Date)
            {
                errors.Add($"{path}.ClosingBalance.Date cannot be before OpeningBalance.Date.");
            }

            var calculatedClosingBalance = SignedBalance(statement.OpeningBalance)
                + statement.Transactions.Sum(SignedTransactionAmount);
            if (calculatedClosingBalance != SignedBalance(statement.ClosingBalance))
            {
                errors.Add($"{path} opening balance plus statement transactions does not equal the closing balance.");
            }
        }

        private static decimal SignedBalance(MT940Balance balance)
        {
            return balance.CreditDebitIndicator == "D" ? -balance.Amount : balance.Amount;
        }

        private static decimal SignedTransactionAmount(MT940Transaction transaction)
        {
            return transaction.CreditDebitIndicator is "D" or "RC"
                ? -transaction.Amount
                : transaction.Amount;
        }

        private static void ValidateStatementInformation(
            IReadOnlyList<string> information,
            string statementPath,
            ICollection<string> errors)
        {
            for (var index = 0; index < information.Count; index++)
            {
                var lines = information[index]
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n');
                if (lines.Length > 6 || lines.Any(line => line.Length > 65))
                {
                    errors.Add($"{statementPath}.InformationToAccountOwner[{index}] cannot exceed six lines of 65 characters.");
                }
            }
        }

        private static void ValidateAmount(decimal amount, string path, ICollection<string> errors)
        {
            if (amount < 0)
            {
                errors.Add($"{path} cannot be negative; use the debit/credit indicator for its sign.");
                return;
            }

            var text = amount.ToString(CultureInfo.InvariantCulture);
            var digitCount = text.Count(char.IsAsciiDigit);
            if (digitCount + 1 > 15)
            {
                errors.Add($"{path} cannot exceed the MT940 15-character amount field.");
            }
        }

        private static void ValidateRequiredText(
            string? value,
            string path,
            int maximumLength,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path} is required.");
            }
            else if (value.Length > maximumLength)
            {
                errors.Add($"{path} cannot exceed {maximumLength} characters.");
            }
        }

        private static void ValidateOptionalText(
            string? value,
            string path,
            int maximumLength,
            ICollection<string> errors)
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength))
            {
                errors.Add($"{path} must contain between 1 and {maximumLength} characters when present.");
            }
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
