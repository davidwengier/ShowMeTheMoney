using ShowMeTheMoney.Core.Banking;
using Xunit;

namespace ShowMeTheMoney.Core.Tests;

public sealed class TransactionCategoryRulesTests
{
    [Theory]
    [InlineData("Woolworths Metro", -42.10, "Groceries")]
    [InlineData("Netflix.com", -18.99, "Entertainment")]
    [InlineData("Monthly account fee", -5, "Fees")]
    [InlineData("Salary payment", 2500, "Income")]
    [InlineData("Osko payment", -100, "Transfers")]
    public void Categorize_UncategorisedTransactionUsesBuiltInRules(
        string description,
        decimal amount,
        string expectedCategory)
    {
        var transaction = CreateTransaction(description, amount);

        var category = TransactionCategoryRules.Categorize(
            transaction,
            new Dictionary<string, string>());

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void Categorize_LearnedRuleOverridesImportedCategory()
    {
        var transaction = CreateTransaction("Coffee-Club", -12m) with
        {
            Category = "Imported category"
        };
        var rules = new Dictionary<string, string>
        {
            [TransactionCategoryRules.NormalizeDescription("coffee club")] = "Dining"
        };

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal("Dining", category);
    }

    [Fact]
    public void Categorize_UnknownMerchantPreservesImportedCategory()
    {
        var transaction = CreateTransaction("Local merchant", -12m) with
        {
            Category = "Holiday"
        };

        var category = TransactionCategoryRules.Categorize(
            transaction,
            new Dictionary<string, string>());

        Assert.Equal("Holiday", category);
    }

    [Fact]
    public void NormalizeDescription_IgnoresCaseWhitespaceAndPunctuation()
    {
        var first = TransactionCategoryRules.NormalizeDescription("  Coffee-Club #42 ");
        var second = TransactionCategoryRules.NormalizeDescription("coffee club 42");

        Assert.Equal(second, first);
    }

    [Fact]
    public void Categorize_ShortRuleDoesNotMatchInsideAnotherWord()
    {
        var transaction = CreateTransaction("Origami supplies", -12m);

        var category = TransactionCategoryRules.Categorize(
            transaction,
            new Dictionary<string, string>());

        Assert.Equal(TransactionCategories.Uncategorised, category);
    }

    private static BankTransaction CreateTransaction(string description, decimal amount) =>
        new(
            "transaction",
            "account",
            new DateOnly(2026, 8, 30),
            description,
            TransactionCategories.Uncategorised,
            amount,
            "AUD",
            false);
}
