using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Triodos.KruispostMonitor.Matching;

namespace Triodos.KruispostMonitor.Mt940;

public static class Mt940Parser
{
    public static Mt940Statement Parse(string fileContent)
    {
        var lines = fileContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var accountId = ExtractTagValue(lines, ":25:")
            ?? throw new FormatException("MT940 statement missing required :25: (account identification) tag");

        var closingBalanceLine = ExtractTagValue(lines, ":62F:")
            ?? throw new FormatException("MT940 statement missing required :62F: (closing balance) tag");

        var (closingBalance, currency) = ParseBalanceLine(closingBalanceLine);
        var transactions = ParseTransactions(lines);

        return new Mt940Statement(accountId, closingBalance, currency, transactions);
    }

    private static string? ExtractTagValue(List<string> lines, string tag)
    {
        var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith(tag));
        return line?.TrimStart()[tag.Length..].Trim();
    }

    private static (decimal balance, string currency) ParseBalanceLine(string line)
    {
        // Format: D/C + YYMMDD + Currency + Amount  OR  D/C + Amount + Currency
        // Triodos uses: C1184,06EUR or D500,00EUR (indicator + amount + currency)
        var indicator = line[0];
        var remaining = line[1..];

        // Check if there's a date (6 digits) after the indicator
        if (remaining.Length >= 6 && remaining[..6].All(char.IsDigit))
        {
            remaining = remaining[6..];
        }

        // Find the currency code — 3 uppercase letters
        string currency;
        string amountStr;

        var currencyStart = -1;
        for (var i = 0; i < remaining.Length - 2; i++)
        {
            if (char.IsUpper(remaining[i]) && char.IsUpper(remaining[i + 1]) && char.IsUpper(remaining[i + 2]))
            {
                currencyStart = i;
                break;
            }
        }

        if (currencyStart >= 0)
        {
            currency = remaining[currencyStart..(currencyStart + 3)];
            amountStr = remaining[..currencyStart] + remaining[(currencyStart + 3)..];
        }
        else
        {
            currency = "EUR";
            amountStr = remaining;
        }

        amountStr = amountStr.Trim();
        var amount = decimal.Parse(amountStr.Replace(',', '.'), CultureInfo.InvariantCulture);

        return indicator == 'D' ? (-amount, currency) : (amount, currency);
    }

    private static List<TransactionRecord> ParseTransactions(List<string> lines)
    {
        var transactions = new List<TransactionRecord>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(":61:"))
                continue;

            var transactionLine = trimmed[4..];
            var (date, amount, debitCredit) = ParseTransactionLine(transactionLine);

            // Collect :86: information (may span multiple lines)
            var details = CollectDetails(lines, i + 1);
            var (counterpartName, remittanceInfo) = ParseDetails(details);

            var signedAmount = debitCredit == 'D' ? -amount : amount;
            var id = GenerateId(date, signedAmount, debitCredit, counterpartName, remittanceInfo);

            transactions.Add(new TransactionRecord(
                id,
                signedAmount,
                counterpartName,
                remittanceInfo,
                new DateTimeOffset(date, TimeSpan.Zero)));
        }

        return transactions;
    }

    private static (DateTime date, decimal amount, char debitCredit) ParseTransactionLine(string line)
    {
        // Format: YYMMDD[MMDD]D/Camount...
        var year = 2000 + int.Parse(line[..2]);
        var month = int.Parse(line[2..4]);
        var day = int.Parse(line[4..6]);
        var date = new DateTime(year, month, day);

        // Skip optional booking date (4 chars MMDD)
        var pos = 6;
        if (pos + 4 <= line.Length && line[pos..].Length >= 4 &&
            char.IsDigit(line[pos]) && char.IsDigit(line[pos + 1]) &&
            char.IsDigit(line[pos + 2]) && char.IsDigit(line[pos + 3]))
        {
            pos += 4;
        }

        // D or C (or RD/RC for reversals)
        var debitCredit = line[pos];
        pos++;
        if (debitCredit == 'R')
        {
            debitCredit = line[pos];
            pos++;
        }

        // Amount: digits and comma until first letter
        var amountEnd = pos;
        while (amountEnd < line.Length && (char.IsDigit(line[amountEnd]) || line[amountEnd] == ','))
            amountEnd++;

        var amountStr = line[pos..amountEnd].Replace(',', '.');
        var amount = decimal.Parse(amountStr, CultureInfo.InvariantCulture);

        return (date, amount, debitCredit);
    }

    private static string CollectDetails(List<string> lines, int startIndex)
    {
        var sb = new StringBuilder();
        for (var i = startIndex; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (i == startIndex)
            {
                if (!trimmed.StartsWith(":86:"))
                    break;
                sb.Append(trimmed[4..]);
            }
            else
            {
                // Continuation lines for :86: don't start with a tag
                if (trimmed.StartsWith(':') && trimmed.Length > 3 && trimmed[3] == ':')
                    break;
                sb.Append('\n').Append(trimmed);
            }
        }
        return sb.ToString().Trim();
    }

    private static (string counterpartName, string remittanceInfo) ParseDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return (string.Empty, string.Empty);

        var lines = details.Split('\n');
        var counterpartName = lines[0].Trim();
        var remittanceInfo = string.Empty;

        // Look for /REMI/ tag in detail lines
        foreach (var line in lines)
        {
            var remiIndex = line.IndexOf("/REMI/", StringComparison.OrdinalIgnoreCase);
            if (remiIndex >= 0)
            {
                remittanceInfo = line[(remiIndex + 6)..].Trim();
                break;
            }
        }

        // If no /REMI/ found, use remaining lines as remittance info
        if (string.IsNullOrEmpty(remittanceInfo) && lines.Length > 1)
        {
            remittanceInfo = string.Join(" ", lines.Skip(1).Select(l => l.Trim())).Trim();
        }

        return (counterpartName, remittanceInfo);
    }

    private static string GenerateId(DateTime date, decimal amount, char debitCredit, string counterpart, string remittance)
    {
        var input = $"{date:yyyyMMdd}|{amount}|{debitCredit}|{counterpart}|{remittance}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
