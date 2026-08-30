namespace ShowMeTheMoney.Core.Banking;

public static class CategoryFlowSummarizer
{
    public static CategoryFlowSummary Summarize(IEnumerable<BankTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var moneyIn = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var moneyOut = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in transactions)
        {
            if (transaction.Amount == 0)
            {
                continue;
            }

            var category = string.IsNullOrWhiteSpace(transaction.Category)
                ? TransactionCategories.Uncategorised
                : transaction.Category.Trim();
            var totals = transaction.Amount > 0 ? moneyIn : moneyOut;
            totals[category] = totals.GetValueOrDefault(category) + Math.Abs(transaction.Amount);
        }

        return new CategoryFlowSummary(ToFlows(moneyIn), ToFlows(moneyOut));
    }

    private static IReadOnlyList<CategoryFlow> ToFlows(
        IReadOnlyDictionary<string, decimal> totals) =>
        totals
            .Select(pair => new CategoryFlow(pair.Key, pair.Value))
            .OrderByDescending(flow => flow.Amount)
            .ThenBy(flow => flow.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
