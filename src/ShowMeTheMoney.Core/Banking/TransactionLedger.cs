namespace ShowMeTheMoney.Core.Banking;

public static class TransactionLedger
{
    public static IReadOnlyList<TransactionLedgerEntry> Build(
        IEnumerable<BankTransaction> transactions,
        decimal? currentBalance)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var runningBalance = currentBalance;
        var entries = new List<TransactionLedgerEntry>();

        foreach (var transaction in transactions.OrderByDescending(item => item.PostedOn))
        {
            entries.Add(new TransactionLedgerEntry(transaction, runningBalance));
            runningBalance -= transaction.Amount;
        }

        return entries;
    }
}
