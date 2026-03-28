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
            var (date, amount, debitCredit, txType) = ParseTransactionLine(transactionLine);

            // Collect :86: information (may span multiple lines)
            var rawDetails = CollectDetails(lines, i + 1);
            var (counterpartName, remittanceInfo) = ParseDetails(rawDetails, txType);

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

    private static (DateTime date, decimal amount, char debitCredit, string txType) ParseTransactionLine(string line)
    {
        // Format: YYMMDD[MMDD]D/Camount N3-char-type ...
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

        // Transaction type: 1 letter + 3 alphanumeric chars (e.g. NBA, NET, NID, NKN)
        var txType = string.Empty;
        pos = amountEnd;
        if (pos < line.Length && char.IsLetter(line[pos]))
        {
            var typeEnd = pos;
            while (typeEnd < line.Length && line[typeEnd] != ' ')
                typeEnd++;
            txType = line[pos..typeEnd];
        }

        return (date, amount, debitCredit, txType);
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
                // Join continuation lines without separator (they split mid-word in MT940)
                sb.Append(trimmed);
            }
        }
        return sb.ToString().Trim();
    }

    private static Dictionary<string, string> ParseSubfields(string details)
    {
        // Parse >XX subfield codes from Triodos MT940 :86: data
        // Format: headertext>20field20>21field21>22field22...
        var fields = new Dictionary<string, string>();

        // Find the first >XX marker
        var firstMarker = details.IndexOf('>');
        if (firstMarker < 0)
        {
            fields["header"] = details;
            return fields;
        }

        fields["header"] = details[..firstMarker];

        var pos = firstMarker;
        while (pos < details.Length)
        {
            if (details[pos] != '>' || pos + 2 >= details.Length)
            {
                pos++;
                continue;
            }

            var code = details[(pos + 1)..(pos + 3)];
            pos += 3;

            // Find next >XX marker
            var nextMarker = pos;
            while (nextMarker < details.Length)
            {
                var gt = details.IndexOf('>', nextMarker);
                if (gt < 0 || gt + 2 >= details.Length)
                {
                    nextMarker = details.Length;
                    break;
                }
                // Check if this is a real >XX marker (2 digits)
                if (char.IsDigit(details[gt + 1]) && char.IsDigit(details[gt + 2]))
                {
                    nextMarker = gt;
                    break;
                }
                nextMarker = gt + 1;
            }

            fields[code] = details[pos..nextMarker];
            pos = nextMarker;
        }

        return fields;
    }

    private static (string counterpartName, string remittanceInfo) ParseDetails(string details, string txType)
    {
        if (string.IsNullOrWhiteSpace(details))
            return (string.Empty, string.Empty);

        // Check for /REMI/ tag first (standard MT940)
        var remiIndex = details.IndexOf("/REMI/", StringComparison.OrdinalIgnoreCase);
        if (remiIndex >= 0)
        {
            var remi = details[(remiIndex + 6)..].Trim();
            var name = details[..remiIndex].Split('\n')[0].Trim();
            return (name, remi);
        }

        // Parse Triodos >XX subfield format
        var fields = ParseSubfields(details);

        if (!fields.ContainsKey("20"))
            return (details, string.Empty);

        // For transfers (NET, NID): >20=BIC, >21=IBAN, >22-24=name+description
        // For card payments (NBA): >20-24=merchant+terminal info
        // For bank costs (NKN): >20-24=description

        if (txType is "NET" or "NID")
        {
            // Combine >22 through >24 for counterpart name + description
            var narrative = string.Concat(
                fields.GetValueOrDefault("22", ""),
                fields.GetValueOrDefault("23", ""),
                fields.GetValueOrDefault("24", "")).Trim();

            var iban = fields.GetValueOrDefault("21", "").Trim();
            var counterpart = narrative;
            var remittance = !string.IsNullOrEmpty(iban) ? $"IBAN: {iban}" : string.Empty;

            return (counterpart, remittance);
        }
        else
        {
            // Card payments and other: >20-24 is the full description
            var narrative = string.Concat(
                fields.GetValueOrDefault("20", ""),
                fields.GetValueOrDefault("21", ""),
                fields.GetValueOrDefault("22", ""),
                fields.GetValueOrDefault("23", ""),
                fields.GetValueOrDefault("24", "")).Trim();

            // Try to extract merchant name (before " - TERMINAL" or " - ")
            var termIdx = narrative.IndexOf(" - TERMINAL", StringComparison.OrdinalIgnoreCase);
            if (termIdx > 0)
                return (narrative[..termIdx].Trim(), narrative);

            return (narrative, string.Empty);
        }
    }

    private static string GenerateId(DateTime date, decimal amount, char debitCredit, string counterpart, string remittance)
    {
        var input = $"{date:yyyyMMdd}|{amount}|{debitCredit}|{counterpart}|{remittance}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
