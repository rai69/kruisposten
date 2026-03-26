using System.Globalization;
using System.Text;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Notifications;

public static class NotificationMessageBuilder
{
    public static string? Build(MatchResult result, decimal currentBalance, decimal targetBalance)
    {
        var hasUnmatched = result.UnmatchedDebits.Count > 0;
        var balanceDeviation = currentBalance - targetBalance;
        var hasBalanceIssue = balanceDeviation != 0;

        if (!hasUnmatched && !hasBalanceIssue)
            return null;

        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        if (hasUnmatched)
        {
            var count = result.UnmatchedDebits.Count;
            sb.AppendLine($"Kruispost Monitor — {count} unmatched expense{(count != 1 ? "s" : "")} found");
        }
        else
        {
            sb.AppendLine("Kruispost Monitor — balance deviation detected");
        }

        sb.AppendLine();
        sb.AppendLine(string.Format(culture,
            "Balance: EUR {0:F2} (expected: EUR {1:F2}, delta: {2:F2})",
            currentBalance, targetBalance, balanceDeviation));

        if (hasUnmatched)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched expenses:");
            for (var i = 0; i < result.UnmatchedDebits.Count; i++)
            {
                var d = result.UnmatchedDebits[i];
                sb.AppendLine(string.Format(culture,
                    "  {0}. {1:yyyy-MM-dd}  -EUR {2:F2}  {3}  \"{4}\"",
                    i + 1, d.ExecutionDate, d.AbsoluteAmount, d.CounterpartName, d.RemittanceInformation));
            }
        }

        if (result.PossibleMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Possible matches (low confidence):");
            for (var i = 0; i < result.PossibleMatches.Count; i++)
            {
                var pm = result.PossibleMatches[i];
                sb.AppendLine(string.Format(culture,
                    "  {0}. {1:yyyy-MM-dd}  -EUR {2:F2}  {3} <-> {4:yyyy-MM-dd}  +EUR {5:F2}  \"{6}\"",
                    i + 1, pm.Debit.ExecutionDate, pm.Debit.AbsoluteAmount, pm.Debit.CounterpartName,
                    pm.Credit.ExecutionDate, pm.Credit.AbsoluteAmount, pm.Credit.RemittanceInformation));
            }
        }

        return sb.ToString().TrimEnd();
    }
}
