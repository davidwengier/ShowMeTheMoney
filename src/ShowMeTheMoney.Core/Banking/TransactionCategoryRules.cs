using System.Text;

namespace ShowMeTheMoney.Core.Banking;

public static class TransactionCategoryRules
{
    public static IReadOnlyList<TransactionCategoryRule> BuiltInRules { get; } =
    [
        new("SALARY", "Income", TransactionCategoryRuleMatch.MoneyInDescriptionContains),
        new("PAYROLL", "Income", TransactionCategoryRuleMatch.MoneyInDescriptionContains),
        new("INTEREST", "Income", TransactionCategoryRuleMatch.MoneyInDescriptionContains),
        new("TRANSFER", "Transfers", TransactionCategoryRuleMatch.DescriptionContains),
        new("OSKO", "Transfers", TransactionCategoryRuleMatch.DescriptionContains),
        new("PAYID", "Transfers", TransactionCategoryRuleMatch.DescriptionContains),
        new("WOOLWORTHS", "Groceries", TransactionCategoryRuleMatch.DescriptionContains),
        new("COLES", "Groceries", TransactionCategoryRuleMatch.DescriptionContains),
        new("ALDI", "Groceries", TransactionCategoryRuleMatch.DescriptionContains),
        new("IGA", "Groceries", TransactionCategoryRuleMatch.DescriptionContains),
        new("UBER EATS", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("DOORDASH", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("MENULOG", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("MCDONALDS", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("KFC", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("CAFE", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("RESTAURANT", "Dining", TransactionCategoryRuleMatch.DescriptionContains),
        new("NETFLIX", "Entertainment", TransactionCategoryRuleMatch.DescriptionContains),
        new("SPOTIFY", "Entertainment", TransactionCategoryRuleMatch.DescriptionContains),
        new("DISNEY PLUS", "Entertainment", TransactionCategoryRuleMatch.DescriptionContains),
        new("DISNEY", "Entertainment", TransactionCategoryRuleMatch.DescriptionContains),
        new("STEAM", "Entertainment", TransactionCategoryRuleMatch.DescriptionContains),
        new("PHARMACY", "Health", TransactionCategoryRuleMatch.DescriptionContains),
        new("CHEMIST", "Health", TransactionCategoryRuleMatch.DescriptionContains),
        new("MEDICARE", "Health", TransactionCategoryRuleMatch.DescriptionContains),
        new("RENT", "Housing", TransactionCategoryRuleMatch.DescriptionContains),
        new("MORTGAGE", "Housing", TransactionCategoryRuleMatch.DescriptionContains),
        new("AMAZON", "Shopping", TransactionCategoryRuleMatch.DescriptionContains),
        new("KMART", "Shopping", TransactionCategoryRuleMatch.DescriptionContains),
        new("TARGET", "Shopping", TransactionCategoryRuleMatch.DescriptionContains),
        new("BIG W", "Shopping", TransactionCategoryRuleMatch.DescriptionContains),
        new("BUNNINGS", "Shopping", TransactionCategoryRuleMatch.DescriptionContains),
        new("UBER", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("OPAL", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("MYKI", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("TRANSLINK", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("AMPOL", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("CALTEX", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("PETROL", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
        new("TELSTRA", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("OPTUS", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("VODAFONE", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("AGL", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("ENERGYAUSTRALIA", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("ORIGIN ENERGY", "Utilities", TransactionCategoryRuleMatch.DescriptionContains),
        new("ACCOUNT FEE", "Fees", TransactionCategoryRuleMatch.DescriptionContains),
        new("TRANSACTION FEE", "Fees", TransactionCategoryRuleMatch.DescriptionContains),
        new("MONTHLY FEE", "Fees", TransactionCategoryRuleMatch.DescriptionContains)
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

        return BuiltInRules
            .FirstOrDefault(rule =>
                (rule.Match != TransactionCategoryRuleMatch.MoneyInDescriptionContains
                    || transaction.Amount > 0)
                && ContainsPattern(merchantKey, rule.Pattern))
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

    private static bool ContainsPattern(string description, string pattern)
    {
        var paddedDescription = $" {description} ";
        return paddedDescription.Contains(
            $" {NormalizeDescription(pattern)} ",
            StringComparison.Ordinal);
    }
}
