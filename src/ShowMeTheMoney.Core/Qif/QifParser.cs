using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShowMeTheMoney.Core.Banking;

namespace ShowMeTheMoney.Core.Qif;

public sealed class QifParser
{
    public QifImportResult Parse(Stream stream, string sourceFileName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        var fallbackAccountName = Path.GetFileNameWithoutExtension(sourceFileName);
        var accountName = fallbackAccountName;
        var warnings = new List<string>();
        var parsedTransactions = new List<ParsedTransaction>();
        var fields = new Dictionary<char, string>();
        var readingAccount = false;
        var readingTransactions = false;
        var lineNumber = 0;
        var recordStartLine = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Equals("!Account", StringComparison.OrdinalIgnoreCase))
            {
                CommitTransaction(fields, parsedTransactions, warnings, recordStartLine);
                fields.Clear();
                readingAccount = true;
                readingTransactions = false;
                recordStartLine = lineNumber + 1;
                continue;
            }

            if (line.StartsWith("!Type:", StringComparison.OrdinalIgnoreCase))
            {
                CommitTransaction(fields, parsedTransactions, warnings, recordStartLine);
                fields.Clear();
                readingAccount = false;
                readingTransactions = IsSupportedAccountType(line[6..]);
                recordStartLine = lineNumber + 1;
                continue;
            }

            if (line == "^")
            {
                if (readingAccount)
                {
                    if (fields.TryGetValue('N', out var importedAccountName)
                        && !string.IsNullOrWhiteSpace(importedAccountName))
                    {
                        accountName = importedAccountName.Trim();
                    }
                }
                else if (readingTransactions)
                {
                    CommitTransaction(fields, parsedTransactions, warnings, recordStartLine);
                }

                fields.Clear();
                recordStartLine = lineNumber + 1;
                continue;
            }

            if ((readingAccount || readingTransactions) && line.Length > 1)
            {
                fields[line[0]] = line[1..].Trim();
            }
        }

        if (readingTransactions)
        {
            CommitTransaction(fields, parsedTransactions, warnings, recordStartLine);
        }

        if (parsedTransactions.Count == 0)
        {
            throw new QifParseException(
                "The selected file did not contain any supported QIF bank transactions.");
        }

        var accountId = CreateStableId(accountName);
        var transactions = parsedTransactions
            .Select((transaction, index) => new BankTransaction(
                CreateStableId(
                    $"{accountId}|{transaction.Date:yyyy-MM-dd}|{transaction.Amount}|"
                    + $"{transaction.Payee}|{transaction.Memo}|{index}"),
                accountId,
                transaction.Date,
                GetDescription(transaction),
                string.IsNullOrWhiteSpace(transaction.Category)
                    ? "Uncategorised"
                    : transaction.Category,
                transaction.Amount,
                "AUD",
                false))
            .OrderByDescending(transaction => transaction.PostedOn)
            .ToArray();

        BankAccount[] accounts =
        [
            new(accountId, accountName, "Imported QIF", null, "AUD")
        ];

        return new QifImportResult(
            new BankingSnapshot(
                "Imported bank account",
                $"Imported from {sourceFileName}",
                accounts,
                transactions),
            warnings);
    }

    private static bool IsSupportedAccountType(string accountType) =>
        accountType.Trim() is "Bank" or "Cash" or "CCard" or "Oth A" or "Oth L";

    private static void CommitTransaction(
        Dictionary<char, string> fields,
        List<ParsedTransaction> transactions,
        List<string> warnings,
        int recordStartLine)
    {
        if (fields.Count == 0)
        {
            return;
        }

        if (!fields.TryGetValue('D', out var dateText)
            || !TryParseAustralianDate(dateText, out var date))
        {
            warnings.Add($"Skipped the record near line {recordStartLine}: invalid or missing date.");
            return;
        }

        if (!fields.TryGetValue('T', out var amountText)
            || !decimal.TryParse(
                amountText,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            warnings.Add($"Skipped the record near line {recordStartLine}: invalid or missing amount.");
            return;
        }

        fields.TryGetValue('P', out var payee);
        fields.TryGetValue('M', out var memo);
        fields.TryGetValue('L', out var category);
        transactions.Add(new ParsedTransaction(
            date,
            amount,
            payee ?? string.Empty,
            memo ?? string.Empty,
            category ?? string.Empty));
    }

    private static bool TryParseAustralianDate(string value, out DateOnly date)
    {
        var normalized = value.Trim().Replace('\'', '/');
        var parts = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 3
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out var year))
        {
            date = default;
            return false;
        }

        if (year < 100)
        {
            year += year >= 70 ? 1900 : 2000;
        }

        return DateOnly.TryParseExact(
            $"{year:D4}-{month:D2}-{day:D2}",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static string GetDescription(ParsedTransaction transaction)
    {
        if (!string.IsNullOrWhiteSpace(transaction.Payee))
        {
            return transaction.Payee;
        }

        if (!string.IsNullOrWhiteSpace(transaction.Memo))
        {
            return transaction.Memo;
        }

        return "Transaction";
    }

    private static string CreateStableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes[..12]).ToLowerInvariant();
    }

    private sealed record ParsedTransaction(
        DateOnly Date,
        decimal Amount,
        string Payee,
        string Memo,
        string Category);
}
