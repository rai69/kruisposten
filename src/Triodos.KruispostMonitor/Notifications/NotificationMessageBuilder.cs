using System.Globalization;
using System.Text;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Notifications;

public static class NotificationMessageBuilder
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static NotificationMessage? Build(MatchResult result, decimal currentBalance, decimal targetBalance)
    {
        var hasUnmatched = result.UnmatchedDebits.Count > 0;
        var balanceDeviation = currentBalance - targetBalance;
        var hasBalanceIssue = balanceDeviation != 0;

        if (!hasUnmatched && !hasBalanceIssue)
            return null;

        var subject = hasUnmatched
            ? $"Kruispost Monitor — {result.UnmatchedDebits.Count} unmatched expense{(result.UnmatchedDebits.Count != 1 ? "s" : "")} found"
            : "Kruispost Monitor — balance deviation detected";

        var plainText = BuildPlainText(subject, result, currentBalance, targetBalance, balanceDeviation);
        var html = BuildHtml(subject, result, currentBalance, targetBalance, balanceDeviation);

        return new NotificationMessage(subject, plainText, html);
    }

    public static NotificationMessage BuildSuccess(decimal currentBalance, string currency)
    {
        var subject = "Kruispost Monitor — all clear";
        var text = string.Format(Culture, "{0}. Balance: {1} {2:F2}", subject, currency, currentBalance);
        var html = $"""
            <html><body style="font-family: sans-serif;">
            <h2 style="color: #2e7d32;">✅ {subject}</h2>
            <p>Balance: <strong>{currency} {currentBalance.ToString("F2", Culture)}</strong></p>
            </body></html>
            """;
        return new NotificationMessage(subject, text, html);
    }

    private static string BuildPlainText(string subject, MatchResult result, decimal currentBalance, decimal targetBalance, decimal balanceDeviation)
    {
        var sb = new StringBuilder();
        sb.AppendLine(subject);
        sb.AppendLine();
        sb.AppendLine(string.Format(Culture,
            "Balance: EUR {0:F2} (expected: EUR {1:F2}, delta: {2:F2})",
            currentBalance, targetBalance, balanceDeviation));

        if (result.UnmatchedDebits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched expenses:");
            sb.AppendLine();
            AppendPlainTextTable(sb, result.UnmatchedDebits, isDebit: true);
        }

        if (result.UnmatchedCredits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unmatched credits:");
            sb.AppendLine();
            AppendPlainTextTable(sb, result.UnmatchedCredits, isDebit: false);
        }

        if (result.PossibleMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Possible matches (low confidence):");
            sb.AppendLine();
            foreach (var pm in result.PossibleMatches)
            {
                sb.AppendLine(string.Format(Culture,
                    "  {0:yyyy-MM-dd}  -EUR {1,8:F2}  {2}",
                    pm.Debit.ExecutionDate, pm.Debit.AbsoluteAmount, pm.Debit.CounterpartName));
                sb.AppendLine(string.Format(Culture,
                    "  {0:yyyy-MM-dd}  +EUR {1,8:F2}  {2}",
                    pm.Credit.ExecutionDate, pm.Credit.AbsoluteAmount, pm.Credit.CounterpartName));
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendPlainTextTable(StringBuilder sb, List<TransactionRecord> transactions, bool isDebit)
    {
        var sign = isDebit ? "-" : "+";
        var maxNameLen = Math.Min(transactions.Max(t => t.CounterpartName.Length), 30);

        sb.AppendLine(string.Format("  {0,-10}  {1,12}  {2}", "Date", "Amount", "Description"));
        sb.AppendLine(string.Format("  {0}  {1}  {2}", new string('-', 10), new string('-', 12), new string('-', maxNameLen)));

        foreach (var t in transactions)
        {
            var name = t.CounterpartName.Length > 30 ? t.CounterpartName[..27] + "..." : t.CounterpartName;
            sb.AppendLine(string.Format(Culture, "  {0:yyyy-MM-dd}  {1}EUR {2,8:F2}  {3}",
                t.ExecutionDate, sign, t.AbsoluteAmount, name));
        }

        var total = transactions.Sum(t => t.AbsoluteAmount);
        sb.AppendLine(string.Format("  {0}  {1}  {2}", new string(' ', 10), new string('-', 12), ""));
        sb.AppendLine(string.Format(Culture, "  {0,-10}  {1}EUR {2,8:F2}", "Total", sign, total));
    }

    private static string BuildHtml(string subject, MatchResult result, decimal currentBalance, decimal targetBalance, decimal balanceDeviation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <html><body style="font-family: sans-serif; color: #333; max-width: 700px;">
            """);

        sb.AppendLine($"<h2 style=\"color: #c62828;\">{Escape(subject)}</h2>");

        var deltaColor = balanceDeviation < 0 ? "#c62828" : "#2e7d32";
        sb.AppendLine(string.Format(Culture,
            "<p>Balance: <strong>EUR {0:F2}</strong> (expected: EUR {1:F2}, delta: <span style=\"color:{3}\">{2:F2}</span>)</p>",
            currentBalance, targetBalance, balanceDeviation, deltaColor));

        if (result.UnmatchedDebits.Count > 0)
        {
            sb.AppendLine("<h3>Unmatched expenses</h3>");
            AppendHtmlTable(sb, result.UnmatchedDebits, isDebit: true);
        }

        if (result.UnmatchedCredits.Count > 0)
        {
            sb.AppendLine("<h3>Unmatched credits</h3>");
            AppendHtmlTable(sb, result.UnmatchedCredits, isDebit: false);
        }

        if (result.PossibleMatches.Count > 0)
        {
            sb.AppendLine("<h3>Possible matches (low confidence)</h3>");
            sb.AppendLine("""<table style="border-collapse: collapse; width: 100%;">""");
            sb.AppendLine("""<tr style="background: #f5f5f5; font-weight: bold;">""");
            sb.AppendLine("<th style=\"padding: 6px 12px; text-align: left;\">Debit</th>");
            sb.AppendLine("<th style=\"padding: 6px 12px; text-align: left;\">Credit</th></tr>");

            foreach (var pm in result.PossibleMatches)
            {
                sb.AppendLine("<tr style=\"border-bottom: 1px solid #e0e0e0;\">");
                sb.AppendLine(string.Format(Culture,
                    "<td style=\"padding: 6px 12px;\">{0:yyyy-MM-dd} &minus;EUR {1:F2}<br><small>{2}</small></td>",
                    pm.Debit.ExecutionDate, pm.Debit.AbsoluteAmount, Escape(pm.Debit.CounterpartName)));
                sb.AppendLine(string.Format(Culture,
                    "<td style=\"padding: 6px 12px;\">{0:yyyy-MM-dd} +EUR {1:F2}<br><small>{2}</small></td>",
                    pm.Credit.ExecutionDate, pm.Credit.AbsoluteAmount, Escape(pm.Credit.CounterpartName)));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<hr style=\"border: none; border-top: 1px solid #e0e0e0; margin-top: 20px;\">");
        sb.AppendLine("<p style=\"color: #999; font-size: 12px;\">Kruispost Monitor — automated notification</p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static void AppendHtmlTable(StringBuilder sb, List<TransactionRecord> transactions, bool isDebit)
    {
        var sign = isDebit ? "&minus;" : "+";
        var amountColor = isDebit ? "#c62828" : "#2e7d32";

        sb.AppendLine("""<table style="border-collapse: collapse; width: 100%;">""");
        sb.AppendLine("""<tr style="background: #f5f5f5; font-weight: bold;">""");
        sb.AppendLine("<th style=\"padding: 6px 12px; text-align: left;\">Date</th>");
        sb.AppendLine("<th style=\"padding: 6px 12px; text-align: right;\">Amount</th>");
        sb.AppendLine("<th style=\"padding: 6px 12px; text-align: left;\">Description</th></tr>");

        foreach (var t in transactions)
        {
            sb.AppendLine("<tr style=\"border-bottom: 1px solid #e0e0e0;\">");
            sb.AppendLine(string.Format(Culture,
                "<td style=\"padding: 6px 12px;\">{0:yyyy-MM-dd}</td>", t.ExecutionDate));
            sb.AppendLine(string.Format(Culture,
                "<td style=\"padding: 6px 12px; text-align: right; color: {0};\">{1}EUR {2:F2}</td>",
                amountColor, sign, t.AbsoluteAmount));
            sb.AppendLine(string.Format("<td style=\"padding: 6px 12px;\">{0}</td>", Escape(t.CounterpartName)));
            sb.AppendLine("</tr>");
        }

        var total = transactions.Sum(t => t.AbsoluteAmount);
        sb.AppendLine(string.Format(Culture,
            """<tr style="font-weight: bold; border-top: 2px solid #333;">""" +
            "<td style=\"padding: 6px 12px;\">Total</td>" +
            "<td style=\"padding: 6px 12px; text-align: right; color: {0};\">{1}EUR {2:F2}</td>" +
            "<td></td></tr>",
            amountColor, sign, total));

        sb.AppendLine("</table>");
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
