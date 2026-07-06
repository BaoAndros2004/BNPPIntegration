using System.Globalization;
using System.Text.RegularExpressions;
using BNPPIntegration.BNPP.FSR.Models;

namespace BNPPIntegration.BNPP.FSR
{
    public sealed partial class FSRValidator
    {
        private static readonly HashSet<string> SupportedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "ACTC",
            "RJCT"
        };

        public FSRValidationResult Validate(FSRReport? report)
        {
            var errors = new List<string>();
            if (report is null)
            {
                errors.Add("An FSR report is required.");
                return new FSRValidationResult(errors);
            }

            ValidateRequiredText(report.MessageId, "MessageId", 35, errors);
            ValidateRequiredText(report.OriginalMessageId, "OriginalMessageId", 35, errors);
            ValidateRequiredText(report.OriginalMessageNameId, "OriginalMessageNameId", 35, errors);
            ValidateRequiredText(report.GroupStatus, "GroupStatus", 4, errors);

            if (report.CreationDateTime is null)
            {
                errors.Add("CreationDateTime is required and must be a valid ISO 8601 date and time.");
            }

            if (report.InitiatingPartyBic is not null && !BicPattern().IsMatch(report.InitiatingPartyBic))
            {
                errors.Add("InitiatingPartyBic must be a valid 8 or 11 character BIC.");
            }

            if (!string.IsNullOrWhiteSpace(report.GroupStatus) && !SupportedStatuses.Contains(report.GroupStatus))
            {
                errors.Add("GroupStatus must be ACTC or RJCT.");
            }

            if (report.OriginalNumberOfTransactions < 0)
            {
                errors.Add("OriginalNumberOfTransactions cannot be negative.");
            }

            if (report.OriginalNumberOfTransactions is not null
                && report.OriginalNumberOfTransactions.Value.ToString(CultureInfo.InvariantCulture).Length > 15)
            {
                errors.Add("OriginalNumberOfTransactions cannot exceed 15 digits.");
            }

            if (report.OriginalControlSum is not null)
            {
                ValidateControlSum(report.OriginalControlSum.Value, errors);
            }

            ValidateStatusReasons(report, errors);
            ValidateStatusConsistency(report, errors);

            return new FSRValidationResult(errors);
        }

        private static void ValidateStatusReasons(FSRReport report, ICollection<string> errors)
        {
            var reasonKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < report.StatusReasons.Count; index++)
            {
                var reason = report.StatusReasons[index];
                var path = $"StatusReasons[{index}]";

                if (reason.Code is not null && (reason.Code.Length is < 1 or > 4))
                {
                    errors.Add($"{path}.Code must contain between 1 and 4 characters.");
                }

                for (var informationIndex = 0; informationIndex < reason.AdditionalInformation.Count; informationIndex++)
                {
                    var information = reason.AdditionalInformation[informationIndex];
                    if (string.IsNullOrWhiteSpace(information) || information.Length > 105)
                    {
                        errors.Add($"{path}.AdditionalInformation[{informationIndex}] must contain between 1 and 105 characters.");
                    }
                }

                var reasonKey = string.Join(
                    "\u001f",
                    new[] { reason.Code?.Trim().ToUpperInvariant() ?? string.Empty }
                        .Concat(reason.AdditionalInformation.Select(value => value.Trim())));
                if (!reasonKeys.Add(reasonKey))
                {
                    errors.Add($"{path} duplicates an earlier status reason.");
                }
            }
        }

        private static void ValidateStatusConsistency(FSRReport report, ICollection<string> errors)
        {
            if (report.GroupStatus.Equals("RJCT", StringComparison.OrdinalIgnoreCase))
            {
                if (!report.StatusReasons.Any(reason => !string.IsNullOrWhiteSpace(reason.Code)))
                {
                    errors.Add("A rejected FSR report must include at least one status reason code.");
                }

                if (report.OriginalNumberOfTransactions is not null || report.OriginalControlSum is not null)
                {
                    errors.Add("Original transaction count and control sum must be omitted when GroupStatus is RJCT.");
                }
            }

            if (report.GroupStatus.Equals("ACTC", StringComparison.OrdinalIgnoreCase)
                && report.StatusReasons.Any(reason => !string.IsNullOrWhiteSpace(reason.Code)))
            {
                errors.Add("A technically accepted FSR report must not include a rejection reason code.");
            }
        }

        private static void ValidateControlSum(decimal amount, ICollection<string> errors)
        {
            var text = amount.ToString(CultureInfo.InvariantCulture).TrimStart('-');
            var parts = text.Split('.');
            var totalDigits = parts.Sum(part => part.Length);
            var fractionalDigits = parts.Length == 2 ? parts[1].Length : 0;

            if (totalDigits > 18 || fractionalDigits > 17)
            {
                errors.Add("OriginalControlSum cannot exceed 18 total or 17 fractional digits.");
            }
        }

        private static void ValidateRequiredText(
            string? value,
            string fieldName,
            int maximumLength,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{fieldName} is required.");
            }
            else if (value.Length > maximumLength)
            {
                errors.Add($"{fieldName} cannot exceed {maximumLength} characters.");
            }
        }

        [GeneratedRegex("^[A-Z]{6}[A-Z2-9][A-NP-Z0-9](?:[A-Z0-9]{3})?$")]
        private static partial Regex BicPattern();
    }
}
