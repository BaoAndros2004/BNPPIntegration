using System.Globalization;
using BNPPIntegration.BNPP.MT942.Models;

namespace BNPPIntegration.BNPP.MT942
{
    public sealed class MT942Mapper
    {
        public MT942ExportData Map(MT942Report report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new MT942ExportData
            {
                Statements = report.Statements.Select(MapStatement).ToArray()
            };
        }

        private static MT942ExportStatement MapStatement(MT942Statement statement)
        {
            var currency = statement.FloorLimits.FirstOrDefault()?.Currency.Trim().ToUpperInvariant() ?? string.Empty;
            return new MT942ExportStatement
            {
                SenderBankAddress = statement.BasicHeader.BankAddress.Trim().ToUpperInvariant(),
                ReceiverBankAddress = statement.ApplicationHeader.BankAddress.Trim().ToUpperInvariant(),
                TransactionReference = statement.TransactionReference.Trim(),
                RelatedReference = NormalizeText(statement.RelatedReference),
                AccountIdentification = statement.AccountIdentification.Trim(),
                StatementNumber = statement.StatementNumber.ToString("D5", CultureInfo.InvariantCulture),
                SequenceNumber = statement.SequenceNumber?.ToString("D5", CultureInfo.InvariantCulture),
                FloorLimits = statement.FloorLimits.Select(MapFloorLimit).ToArray(),
                ReportDateTime = statement.ReportDateTime?.ToString(
                    "yyyy-MM-dd'T'HH:mm:sszzz",
                    CultureInfo.InvariantCulture) ?? string.Empty,
                Transactions = statement.Transactions.Select(transaction => MapTransaction(transaction, currency)).ToArray(),
                DebitSummary = statement.DebitSummary is null ? null : MapSummary(statement.DebitSummary),
                CreditSummary = statement.CreditSummary is null ? null : MapSummary(statement.CreditSummary)
            };
        }

        private static MT942ExportFloorLimit MapFloorLimit(MT942FloorLimit floorLimit)
        {
            return new MT942ExportFloorLimit
            {
                Currency = floorLimit.Currency.Trim().ToUpperInvariant(),
                CreditDebitIndicator = NormalizeUppercase(floorLimit.CreditDebitIndicator),
                Amount = FormatAmount(floorLimit.Amount)
            };
        }

        private static MT942ExportEntrySummary MapSummary(MT942EntrySummary summary)
        {
            return new MT942ExportEntrySummary
            {
                EntryType = summary.EntryType.Trim().ToUpperInvariant(),
                NumberOfEntries = summary.NumberOfEntries.ToString("D5", CultureInfo.InvariantCulture),
                Currency = summary.Currency.Trim().ToUpperInvariant(),
                Amount = FormatAmount(summary.Amount)
            };
        }

        private static MT942ExportTransaction MapTransaction(MT942Transaction transaction, string currency)
        {
            return new MT942ExportTransaction
            {
                ValueDate = FormatDate(transaction.ValueDate),
                EntryDate = transaction.EntryDate is null ? null : FormatDate(transaction.EntryDate),
                CreditDebitIndicator = transaction.CreditDebitIndicator.Trim().ToUpperInvariant(),
                FundsCode = NormalizeUppercase(transaction.FundsCode),
                Amount = FormatAmount(transaction.Amount),
                Currency = currency,
                TransactionTypeCode = transaction.TransactionTypeCode.Trim().ToUpperInvariant(),
                CustomerReference = transaction.CustomerReference.Trim(),
                BankReference = NormalizeText(transaction.BankReference),
                SupplementaryDetails = NormalizeText(transaction.SupplementaryDetails),
                Details = transaction.Details is null ? null : MapDetails(transaction.Details)
            };
        }

        private static MT942ExportTransactionDetails MapDetails(MT942TransactionDetails details)
        {
            return new MT942ExportTransactionDetails
            {
                RawInformation = details.RawInformation.TrimEnd(),
                TypeCode = NormalizeUppercase(details.TypeCode),
                Description = NormalizeText(details.Description),
                BeneficiaryInformation = NormalizeText(details.BeneficiaryInformation),
                RemittanceInformation = NormalizeText(details.RemittanceInformation),
                ExchangeRate = details.ExchangeRate is null ? null : FormatAmount(details.ExchangeRate.Value),
                OriginalAmountCurrency = NormalizeUppercase(details.OriginalAmountCurrency),
                OriginalAmount = details.OriginalAmount is null ? null : FormatAmount(details.OriginalAmount.Value)
            };
        }

        private static string FormatDate(DateOnly? value)
        {
            return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string FormatAmount(decimal value)
        {
            return value.ToString("0.############################", CultureInfo.InvariantCulture);
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
