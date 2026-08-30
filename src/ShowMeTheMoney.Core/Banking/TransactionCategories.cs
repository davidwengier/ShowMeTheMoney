namespace ShowMeTheMoney.Core.Banking;

public static class TransactionCategories
{
    public const string Uncategorised = "Uncategorised";

    public static IReadOnlyList<string> Defaults { get; } =
    [
        Uncategorised,
        "Dining",
        "Entertainment",
        "Fees",
        "Groceries",
        "Health",
        "Housing",
        "Income",
        "Shopping",
        "Transfers",
        "Transport",
        "Utilities"
    ];
}
