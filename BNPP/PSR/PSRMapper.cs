using System.Globalization;
using BNPPIntegration.BNPP.PSR.Models;

namespace BNPPIntegration.BNPP.PSR
{
    public sealed class PSRMapper
    {
        public PSRExportData Map(PSRReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            return new PSRExportData
            {
                MessageId = report.MessageId.Trim(),
                CreationDateTime = FormatDateTime(report.CreationDateTime) ?? string.Empty,
                InitiatingPartyBic = NormalizeUppercase(report.InitiatingPartyBic),
                OriginalMessageId = report.OriginalMessageId.Trim(),
                OriginalMessageNameId = report.OriginalMessageNameId.Trim(),
                OriginalCreationDateTime = FormatDateTime(report.OriginalCreationDateTime),
                OriginalNumberOfTransactions = FormatInteger(report.OriginalNumberOfTransactions),
                OriginalControlSum = FormatDecimal(report.OriginalControlSum, "0.#################"),
                GroupStatus = report.GroupStatus.Trim().ToUpperInvariant(),
                StatusReasons = report.StatusReasons.Select(MapStatusReason).ToArray(),
                TransactionStatusSummaries = report.TransactionStatusSummaries.Select(MapStatusSummary).ToArray(),
                PaymentInformation = report.PaymentInformation.Select(MapPaymentInformation).ToArray()
            };
        }

        private static PSRExportPaymentInformation MapPaymentInformation(PSRPaymentInformation payment)
        {
            return new PSRExportPaymentInformation
            {
                OriginalPaymentInformationId = payment.OriginalPaymentInformationId.Trim(),
                OriginalNumberOfTransactions = FormatInteger(payment.OriginalNumberOfTransactions),
                OriginalControlSum = FormatDecimal(payment.OriginalControlSum, "0.#################"),
                PaymentInformationStatus = payment.PaymentInformationStatus.Trim().ToUpperInvariant(),
                StatusReasons = payment.StatusReasons.Select(MapStatusReason).ToArray(),
                TransactionStatusSummaries = payment.TransactionStatusSummaries.Select(MapStatusSummary).ToArray(),
                Transactions = payment.Transactions.Select(MapTransaction).ToArray()
            };
        }

        private static PSRExportTransaction MapTransaction(PSRTransaction transaction)
        {
            return new PSRExportTransaction
            {
                StatusId = NormalizeText(transaction.StatusId),
                OriginalInstructionId = NormalizeText(transaction.OriginalInstructionId),
                OriginalEndToEndId = NormalizeText(transaction.OriginalEndToEndId),
                TransactionStatus = transaction.TransactionStatus.Trim().ToUpperInvariant(),
                StatusReasons = transaction.StatusReasons.Select(MapStatusReason).ToArray(),
                AcceptanceDateTime = FormatDateTime(transaction.AcceptanceDateTime),
                AccountServicerReference = NormalizeText(transaction.AccountServicerReference),
                ClearingSystemReference = NormalizeText(transaction.ClearingSystemReference),
                OriginalTransactionReference = transaction.OriginalTransactionReference is null
                    ? null
                    : MapOriginalTransactionReference(transaction.OriginalTransactionReference)
            };
        }

        private static PSRExportOriginalTransactionReference MapOriginalTransactionReference(
            PSROriginalTransactionReference transactionReference)
        {
            return new PSRExportOriginalTransactionReference
            {
                InstructedAmount = FormatDecimal(transactionReference.InstructedAmount, "0.#####"),
                Currency = NormalizeUppercase(transactionReference.Currency),
                RequestedExecutionDate = transactionReference.RequestedExecutionDate?.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                DebtorAccount = MapAccount(transactionReference.DebtorAccount),
                CreditorAccount = MapAccount(transactionReference.CreditorAccount),
                UnstructuredRemittanceInformation = transactionReference.UnstructuredRemittanceInformation
                    .Select(information => information.Trim())
                    .ToArray()
            };
        }

        private static PSRAccountIdentification? MapAccount(PSRAccountIdentification? account)
        {
            if (account is null)
            {
                return null;
            }

            var isIban = account.Type.Equals("IBAN", StringComparison.OrdinalIgnoreCase);
            return new PSRAccountIdentification
            {
                Type = isIban ? "IBAN" : "Other",
                Value = isIban
                    ? new string(account.Value.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant()
                    : account.Value.Trim()
            };
        }

        private static PSRExportStatusReason MapStatusReason(PSRStatusReason reason)
        {
            return new PSRExportStatusReason
            {
                Code = NormalizeUppercase(reason.Code),
                AdditionalInformation = reason.AdditionalInformation
                    .Select(information => information.Trim())
                    .ToArray()
            };
        }

        private static PSRExportTransactionStatusSummary MapStatusSummary(PSRTransactionStatusSummary summary)
        {
            return new PSRExportTransactionStatusSummary
            {
                Status = summary.Status.Trim().ToUpperInvariant(),
                NumberOfTransactions = summary.NumberOfTransactions.ToString(CultureInfo.InvariantCulture),
                ControlSum = FormatDecimal(summary.ControlSum, "0.#################")
            };
        }

        private static string? FormatDateTime(DateTime? value)
        {
            return value?.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
        }

        private static string? FormatInteger(long? value)
        {
            return value?.ToString(CultureInfo.InvariantCulture);
        }

        private static string? FormatDecimal(decimal? value, string format)
        {
            return value?.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeUppercase(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }
    }
}
