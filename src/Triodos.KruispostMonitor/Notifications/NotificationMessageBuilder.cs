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
            sb.AppendLine();
            AppendTransactionTable(sb, result.UnmatchedDebits, culture, isDebit: true);
        }

        if (result.UnmatchedCredits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched credits:");
            sb.AppendLine();
            AppendTransactionTable(sb, result.UnmatchedCredits, culture, isDebit: false);
        }

        if (result.PossibleMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Possible matches (low confidence):");
            sb.AppendLine();
            foreach (var pm in result.PossibleMatches)
            {
                sb.AppendLine(string.Format(culture,
                    "  {0:yyyy-MM-dd}  -EUR {1,8:F2}  {2}",
                    pm.Debit.ExecutionDate, pm.Debit.AbsoluteAmount, pm.Debit.CounterpartName));
                sb.AppendLine(string.Format(culture,
                    "  {0:yyyy-MM-dd}  +EUR {1,8:F2}  {2}",
                    pm.Credit.ExecutionDate, pm.Credit.AbsoluteAmount, pm.Credit.CounterpartName));
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendTransactionTable(
        StringBuilder sb,
        List<TransactionRecord> transactions,
        CultureInfo culture,
        bool isDebit)
    {
        // Calculate column widths
        var sign = isDebit ? "-" : "+";
        var maxNameLen = Math.Min(
            transactions.Max(t => t.CounterpartName.Length),
            30);

        // Header
        sb.AppendLine(string.Format("  {0,-10}  {1,12}  {2}", "Date", "Amount", "Description"));
        sb.AppendLine(string.Format("  {0}  {1}  {2}", new string('-', 10), new string('-', 12), new string('-', maxNameLen)));

        // Rows
        foreach (var t in transactions)
        {
            var name = t.CounterpartName.Length > 30
                ? t.CounterpartName[..27] + "..."
                : t.CounterpartName;

            sb.AppendLine(string.Format(culture,
                "  {0:yyyy-MM-dd}  {1}EUR {2,8:F2}  {3}",
                t.ExecutionDate, sign, t.AbsoluteAmount, name));
        }

        // Total
        var total = transactions.Sum(t => t.AbsoluteAmount);
        sb.AppendLine(string.Format("  {0}  {1}  {2}", new string(' ', 10), new string('-', 12), ""));
        sb.AppendLine(string.Format(culture,
            "  {0,-10}  {1}EUR {2,8:F2}",
            "Total", sign, total));
    }
}
