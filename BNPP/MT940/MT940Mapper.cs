using System.Globalization;
using BNPPIntegration.BNPP.MT940.Models;

namespace BNPPIntegration.BNPP.MT940
{
    public sealed class MT940Mapper
    {
        public MT940ExportData Map(MT940Report report)
        {
            ArgumentNullException.ThrowIfNull(report);

            return new MT940ExportData
            {
                Statements = report.Statements.Select(MapStatement).ToArray()
            };
        }

        private static MT940ExportStatement MapStatement(MT940Statement statement)
        {
            var currency = statement.OpeningBalance.Currency.Trim().ToUpperInvariant();

            return new MT940ExportStatement
            {
                SenderBankAddress = statement.BasicHeader.BankAddress.Trim().ToUpperInvariant(),
                ReceiverBankAddress = statement.ApplicationHeader.BankAddress.Trim().ToUpperInvariant(),
                TransactionReference = statement.TransactionReference.Trim(),
                RelatedReference = NormalizeText(statement.RelatedReference),
                AccountIdentification = statement.AccountIdentification.Trim(),
                StatementNumber = statement.StatementNumber.ToString("D5", CultureInfo.InvariantCulture),
                SequenceNumber = statement.SequenceNumber?.ToString("D5", CultureInfo.InvariantCulture),
                OpeningBalance = MapBalance(statement.OpeningBalance),
                Transactions = statement.Transactions.Select(transaction => MapTransaction(transaction, currency)).ToArray(),
                ClosingBalance = MapBalance(statement.ClosingBalance),
                ClosingAvailableBalance = statement.ClosingAvailableBalance is null
                    ? null
                    : MapBalance(statement.ClosingAvailableBalance),
                ForwardAvailableBalances = statement.ForwardAvailableBalances.Select(MapBalance).ToArray(),
                InformationToAccountOwner = statement.InformationToAccountOwner
                    .Select(information => information.TrimEnd())
                    .ToArray()
            };
        }

        private static MT940ExportBalance MapBalance(MT940Balance balance)
        {
            return new MT940ExportBalance
            {
                BalanceType = balance.BalanceType.Trim().ToUpperInvariant(),
                CreditDebitIndicator = balance.CreditDebitIndicator.Trim().ToUpperInvariant(),
                Date = FormatDate(balance.Date),
                Currency = balance.Currency.Trim().ToUpperInvariant(),
                Amount = FormatAmount(balance.Amount)
            };
        }

        private static MT940ExportTransaction MapTransaction(MT940Transaction transaction, string currency)
        {
            return new MT940ExportTransaction
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
                Details = transaction.Details is null ? null : MapTransactionDetails(transaction.Details)
            };
        }

        private static MT940ExportTransactionDetails MapTransactionDetails(MT940TransactionDetails details)
        {
            return new MT940ExportTransactionDetails
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
