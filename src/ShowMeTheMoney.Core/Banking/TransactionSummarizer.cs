namespace ShowMeTheMoney.Core.Banking;

public static class TransactionSummarizer
{
    public static TransactionSummary Summarize(IEnumerable<BankTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var income = 0m;
        var spending = 0m;

        foreach (var transaction in transactions)
        {
            if (transaction.Amount >= 0)
            {
                income += transaction.Amount;
            }
            else
            {
                spending -= transaction.Amount;
            }
        }

        return new TransactionSummary(income, spending, income - spending);
    }
}
