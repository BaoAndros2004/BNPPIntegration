using System.Globalization;
using BNPPIntegration.BNPP.FSR.Models;

namespace BNPPIntegration.BNPP.FSR
{
    public sealed class FSRMapper
    {
        public FSRExportData Map(FSRReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            return new FSRExportData
            {
                MessageId = report.MessageId.Trim(),
                CreationDateTime = report.CreationDateTime?.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                    CultureInfo.InvariantCulture) ?? string.Empty,
                InitiatingPartyBic = NormalizeUppercase(report.InitiatingPartyBic),
                OriginalMessageId = report.OriginalMessageId.Trim(),
                OriginalMessageNameId = report.OriginalMessageNameId.Trim(),
                OriginalNumberOfTransactions = report.OriginalNumberOfTransactions?.ToString(CultureInfo.InvariantCulture),
                OriginalControlSum = report.OriginalControlSum?.ToString("0.#################", CultureInfo.InvariantCulture),
                GroupStatus = report.GroupStatus.Trim().ToUpperInvariant(),
                StatusReasons = report.StatusReasons.Select(MapStatusReason).ToArray()
            };
        }

        private static FSRExportStatusReason MapStatusReason(FSRStatusReason reason)
        {
            return new FSRExportStatusReason
            {
                Code = NormalizeUppercase(reason.Code),
                AdditionalInformation = reason.AdditionalInformation
                    .Select(information => information.Trim())
                    .ToArray()
            };
        }

        private static string? NormalizeUppercase(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }
    }
}
