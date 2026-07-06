using System.Globalization;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.PSR.Models;

namespace BNPPIntegration.BNPP.PSR
{
    public sealed partial class PSRValidator
    {
        private static readonly HashSet<string> SupportedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "ACCP",
            "ACSC",
            "ACSP",
            "ACTC",
            "ACWC",
            "PART",
            "PDNG",
            "RCVD",
            "RJCT"
        };

        public PSRValidationResult Validate(PSRReport? report)
        {
            var errors = new List<string>();
            if (report is null)
            {
                errors.Add("A PSR report is required.");
                return new PSRValidationResult(errors);
            }

            ValidateRequiredText(report.MessageId, "MessageId", 35, errors);
            ValidateRequiredText(report.OriginalMessageId, "OriginalMessageId", 35, errors);
            ValidateRequiredText(report.OriginalMessageNameId, "OriginalMessageNameId", 35, errors);
            ValidateStatus(report.GroupStatus, "GroupStatus", errors);

            if (report.CreationDateTime is null)
            {
                errors.Add("CreationDateTime is required and must be a valid ISO 8601 date and time.");
            }

            if (report.InitiatingPartyBic is not null && !BicPattern().IsMatch(report.InitiatingPartyBic))
            {
                errors.Add("InitiatingPartyBic must be a valid 8 or 11 character BIC.");
            }

            ValidateCount(report.OriginalNumberOfTransactions, "OriginalNumberOfTransactions", errors);
            ValidateDecimal(report.OriginalControlSum, "OriginalControlSum", 17, errors);
            ValidateReasons(report.StatusReasons, "StatusReasons", errors);
            ValidateRejectedStatus(report.GroupStatus, report.StatusReasons, "GroupStatus", errors);
            ValidateSummaries(
                report.TransactionStatusSummaries,
                report.OriginalNumberOfTransactions,
                "TransactionStatusSummaries",
                errors);
            ValidatePaymentInformation(report.PaymentInformation, errors);

            return new PSRValidationResult(errors);
        }

        private static void ValidatePaymentInformation(
            IReadOnlyList<PSRPaymentInformation> paymentInformation,
            ICollection<string> errors)
        {
            var paymentIds = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < paymentInformation.Count; index++)
            {
                var payment = paymentInformation[index];
                var path = $"PaymentInformation[{index}]";

                ValidateRequiredText(
                    payment.OriginalPaymentInformationId,
                    $"{path}.OriginalPaymentInformationId",
                    35,
                    errors);
                ValidateStatus(payment.PaymentInformationStatus, $"{path}.PaymentInformationStatus", errors);
                ValidateCount(payment.OriginalNumberOfTransactions, $"{path}.OriginalNumberOfTransactions", errors);
                ValidateDecimal(payment.OriginalControlSum, $"{path}.OriginalControlSum", 17, errors);
                ValidateReasons(payment.StatusReasons, $"{path}.StatusReasons", errors);
                ValidateRejectedStatus(
                    payment.PaymentInformationStatus,
                    payment.StatusReasons,
                    $"{path}.PaymentInformationStatus",
                    errors);
                ValidateSummaries(
                    payment.TransactionStatusSummaries,
                    payment.OriginalNumberOfTransactions,
                    $"{path}.TransactionStatusSummaries",
                    errors);
                ValidateTransactions(payment.Transactions, path, errors);

                if (!string.IsNullOrWhiteSpace(payment.OriginalPaymentInformationId)
                    && !paymentIds.Add(payment.OriginalPaymentInformationId))
                {
                    errors.Add($"{path}.OriginalPaymentInformationId duplicates an earlier payment information block.");
                }
            }
        }

        private static void ValidateTransactions(
            IReadOnlyList<PSRTransaction> transactions,
            string paymentPath,
            ICollection<string> errors)
        {
            var transactionKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < transactions.Count; index++)
            {
                var transaction = transactions[index];
                var path = $"{paymentPath}.Transactions[{index}]";

                ValidateOptionalText(transaction.StatusId, $"{path}.StatusId", 35, errors);
                ValidateOptionalText(transaction.OriginalInstructionId, $"{path}.OriginalInstructionId", 35, errors);
                ValidateOptionalText(transaction.OriginalEndToEndId, $"{path}.OriginalEndToEndId", 35, errors);
                ValidateOptionalText(
                    transaction.AccountServicerReference,
                    $"{path}.AccountServicerReference",
                    35,
                    errors);
                ValidateOptionalText(
                    transaction.ClearingSystemReference,
                    $"{path}.ClearingSystemReference",
                    35,
                    errors);
                ValidateStatus(transaction.TransactionStatus, $"{path}.TransactionStatus", errors);
                ValidateReasons(transaction.StatusReasons, $"{path}.StatusReasons", errors);
                ValidateRejectedStatus(
                    transaction.TransactionStatus,
                    transaction.StatusReasons,
                    $"{path}.TransactionStatus",
                    errors);

                if (string.IsNullOrWhiteSpace(transaction.OriginalInstructionId)
                    && string.IsNullOrWhiteSpace(transaction.OriginalEndToEndId))
                {
                    errors.Add($"{path} must contain an original instruction or end-to-end identifier.");
                }

                var transactionKey = string.Join(
                    "\u001f",
                    transaction.OriginalInstructionId?.Trim() ?? string.Empty,
                    transaction.OriginalEndToEndId?.Trim() ?? string.Empty);
                if (!transactionKeys.Add(transactionKey))
                {
                    errors.Add($"{path} duplicates an earlier transaction in the same payment information block.");
                }

                if (transaction.OriginalTransactionReference is not null)
                {
                    ValidateOriginalTransactionReference(transaction.OriginalTransactionReference, path, errors);
                }
            }
        }

        private static void ValidateOriginalTransactionReference(
            PSROriginalTransactionReference transactionReference,
            string transactionPath,
            ICollection<string> errors)
        {
            var path = $"{transactionPath}.OriginalTransactionReference";

            if (transactionReference.InstructedAmount is null && transactionReference.Currency is not null)
            {
                errors.Add($"{path}.InstructedAmount is required when Currency is present.");
            }

            if (transactionReference.InstructedAmount is not null && transactionReference.Currency is null)
            {
                errors.Add($"{path}.Currency is required when InstructedAmount is present.");
            }

            if (transactionReference.InstructedAmount < 0)
            {
                errors.Add($"{path}.InstructedAmount cannot be negative.");
            }

            ValidateDecimal(transactionReference.InstructedAmount, $"{path}.InstructedAmount", 5, errors);

            if (transactionReference.Currency is not null && !CurrencyPattern().IsMatch(transactionReference.Currency))
            {
                errors.Add($"{path}.Currency must be a three-letter uppercase ISO currency code.");
            }

            ValidateAccount(transactionReference.DebtorAccount, $"{path}.DebtorAccount", errors);
            ValidateAccount(transactionReference.CreditorAccount, $"{path}.CreditorAccount", errors);

            for (var index = 0; index < transactionReference.UnstructuredRemittanceInformation.Count; index++)
            {
                var information = transactionReference.UnstructuredRemittanceInformation[index];
                if (string.IsNullOrWhiteSpace(information) || information.Length > 140)
                {
                    errors.Add($"{path}.UnstructuredRemittanceInformation[{index}] must contain between 1 and 140 characters.");
                }
            }
        }

        private static void ValidateAccount(
            PSRAccountIdentification? account,
            string path,
            ICollection<string> errors)
        {
            if (account is null)
            {
                return;
            }

            if (account.Type.Equals("IBAN", StringComparison.OrdinalIgnoreCase))
            {
                if (!IbanPattern().IsMatch(account.Value))
                {
                    errors.Add($"{path} must contain a valid 15 to 34 character IBAN.");
                }

                return;
            }

            if (!account.Type.Equals("Other", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{path}.Type must be IBAN or Other.");
            }

            if (string.IsNullOrWhiteSpace(account.Value) || account.Value.Length > 34)
            {
                errors.Add($"{path}.Value must contain between 1 and 34 characters.");
            }
        }

        private static void ValidateSummaries(
            IReadOnlyList<PSRTransactionStatusSummary> summaries,
            long? expectedTransactionCount,
            string path,
            ICollection<string> errors)
        {
            var statuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < summaries.Count; index++)
            {
                var summary = summaries[index];
                var summaryPath = $"{path}[{index}]";
                ValidateStatus(summary.Status, $"{summaryPath}.Status", errors);
                ValidateCount(summary.NumberOfTransactions, $"{summaryPath}.NumberOfTransactions", errors);
                ValidateDecimal(summary.ControlSum, $"{summaryPath}.ControlSum", 17, errors);

                if (!string.IsNullOrWhiteSpace(summary.Status) && !statuses.Add(summary.Status))
                {
                    errors.Add($"{summaryPath}.Status duplicates an earlier transaction status summary.");
                }
            }

            if (expectedTransactionCount is not null
                && summaries.Count > 0
                && summaries.Sum(summary => (decimal)summary.NumberOfTransactions) != expectedTransactionCount)
            {
                errors.Add($"{path} transaction counts do not equal the declared original number of transactions.");
            }
        }

        private static void ValidateReasons(
            IReadOnlyList<PSRStatusReason> reasons,
            string path,
            ICollection<string> errors)
        {
            var reasonKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < reasons.Count; index++)
            {
                var reason = reasons[index];
                var reasonPath = $"{path}[{index}]";

                if (reason.Code is not null && !ReasonCodePattern().IsMatch(reason.Code))
                {
                    errors.Add($"{reasonPath}.Code must contain between 1 and 4 uppercase letters or digits.");
                }

                for (var informationIndex = 0; informationIndex < reason.AdditionalInformation.Count; informationIndex++)
                {
                    var information = reason.AdditionalInformation[informationIndex];
                    if (string.IsNullOrWhiteSpace(information) || information.Length > 105)
                    {
                        errors.Add($"{reasonPath}.AdditionalInformation[{informationIndex}] must contain between 1 and 105 characters.");
                    }
                }

                var key = string.Join(
                    "\u001f",
                    new[] { reason.Code?.Trim().ToUpperInvariant() ?? string.Empty }
                        .Concat(reason.AdditionalInformation.Select(value => value.Trim())));
                if (!reasonKeys.Add(key))
                {
                    errors.Add($"{reasonPath} duplicates an earlier status reason.");
                }
            }
        }

        private static void ValidateRejectedStatus(
            string status,
            IReadOnlyList<PSRStatusReason> reasons,
            string path,
            ICollection<string> errors)
        {
            if (string.Equals(status, "RJCT", StringComparison.OrdinalIgnoreCase)
                && !reasons.Any(reason => !string.IsNullOrWhiteSpace(reason.Code)))
            {
                errors.Add($"{path} is RJCT and must include at least one status reason code.");
            }
        }

        private static void ValidateStatus(string? status, string path, ICollection<string> errors)
        {
            ValidateRequiredText(status, path, 4, errors);
            if (!string.IsNullOrWhiteSpace(status) && !SupportedStatuses.Contains(status))
            {
                errors.Add($"{path} contains unsupported status '{status}'.");
            }
        }

        private static void ValidateCount(long? count, string path, ICollection<string> errors)
        {
            if (count < 0)
            {
                errors.Add($"{path} cannot be negative.");
            }

            if (count is not null && count.Value.ToString(CultureInfo.InvariantCulture).Length > 15)
            {
                errors.Add($"{path} cannot exceed 15 digits.");
            }
        }

        private static void ValidateDecimal(
            decimal? amount,
            string path,
            int maximumFractionalDigits,
            ICollection<string> errors)
        {
            if (amount is null)
            {
                return;
            }

            var text = amount.Value.ToString(CultureInfo.InvariantCulture).TrimStart('-');
            var parts = text.Split('.');
            var totalDigits = parts.Sum(part => part.Length);
            var fractionalDigits = parts.Length == 2 ? parts[1].Length : 0;

            if (totalDigits > 18 || fractionalDigits > maximumFractionalDigits)
            {
                errors.Add($"{path} cannot exceed 18 total or {maximumFractionalDigits} fractional digits.");
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

        [GeneratedRegex("^[A-Z]{6}[A-Z2-9][A-NP-Z0-9](?:[A-Z0-9]{3})?$")]
        private static partial Regex BicPattern();

        [GeneratedRegex("^[A-Z]{3}$")]
        private static partial Regex CurrencyPattern();

        [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")]
        private static partial Regex IbanPattern();

        [GeneratedRegex("^[A-Z0-9]{1,4}$")]
        private static partial Regex ReasonCodePattern();
    }
}
