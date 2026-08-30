using System.Text;

namespace ShowMeTheMoney.Core.Banking;

public static class TransactionCategoryRules
{
    private static readonly Rule[] BuiltInRules =
    [
        new("Groceries", ["WOOLWORTHS", "COLES", "ALDI", "IGA"]),
        new("Dining", ["UBER EATS", "DOORDASH", "MENULOG", "MCDONALDS", "KFC", "CAFE", "RESTAURANT"]),
        new("Entertainment", ["NETFLIX", "SPOTIFY", "DISNEY PLUS", "DISNEY", "STEAM"]),
        new("Health", ["PHARMACY", "CHEMIST", "MEDICARE"]),
        new("Housing", ["RENT", "MORTGAGE"]),
        new("Shopping", ["AMAZON", "KMART", "TARGET", "BIG W", "BUNNINGS"]),
        new("Transport", ["UBER", "OPAL", "MYKI", "TRANSLINK", "AMPOL", "CALTEX", "PETROL"]),
        new("Utilities", ["TELSTRA", "OPTUS", "VODAFONE", "AGL", "ENERGYAUSTRALIA", "ORIGIN ENERGY"]),
        new("Fees", ["ACCOUNT FEE", "TRANSACTION FEE", "MONTHLY FEE"])
    ];

    public static string Categorize(
        BankTransaction transaction,
        IReadOnlyDictionary<string, string> learnedRules)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(learnedRules);

        var merchantKey = NormalizeDescription(transaction.Description);
        if (learnedRules.TryGetValue(merchantKey, out var learnedCategory))
        {
            return learnedCategory;
        }

        if (!transaction.Category.Equals(
                TransactionCategories.Uncategorised,
                StringComparison.OrdinalIgnoreCase))
        {
            return transaction.Category;
        }

        if (transaction.Amount > 0
            && ContainsAny(merchantKey, ["SALARY", "PAYROLL", "INTEREST"]))
        {
            return "Income";
        }

        if (ContainsAny(merchantKey, ["TRANSFER", "OSKO", "PAYID"]))
        {
            return "Transfers";
        }

        return BuiltInRules
            .FirstOrDefault(rule => ContainsAny(merchantKey, rule.Patterns))
            ?.Category
            ?? TransactionCategories.Uncategorised;
    }

    public static string NormalizeDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var normalized = new StringBuilder(description.Length);
        var previousWasSpace = true;

        foreach (var character in description.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                normalized.Append(' ');
                previousWasSpace = true;
            }
        }

        return normalized.ToString().TrimEnd();
    }

    private static bool ContainsAny(string description, IReadOnlyList<string> patterns)
    {
        var paddedDescription = $" {description} ";
        return patterns.Any(pattern =>
            paddedDescription.Contains(
                $" {NormalizeDescription(pattern)} ",
                StringComparison.Ordinal));
    }

    private sealed record Rule(string Category, IReadOnlyList<string> Patterns);
}
