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
    public void Categorize_UncategorisedTransactionUsesSeedRules(
        string description,
        decimal amount,
        string expectedCategory)
    {
        var transaction = CreateTransaction(description, amount);

        var category = TransactionCategoryRules.Categorize(
            transaction,
            TransactionCategoryRules.Defaults);

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void Categorize_ExactRuleOverridesImportedCategory()
    {
        var transaction = CreateTransaction("Coffee-Club", -12m) with
        {
            Category = "Imported category"
        };
        TransactionCategoryRule[] rules =
        [
            new(
                "coffee club",
                "Dining",
                TransactionCategoryRuleMatch.ExactDescription)
        ];

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal("Dining", category);
    }

    [Fact]
    public void Categorize_NoAutomaticMatchPreservesCurrentCategory()
    {
        var transaction = CreateTransaction("Local Chemist", -12m) with
        {
            Category = "Health - Fred"
        };
        TransactionCategoryRule[] rules =
        [
            new(
                "Local Chemist",
                "Health - Fred",
                TransactionCategoryRuleMatch.NoAutomaticMatch),
            new(
                "Chemist",
                "Health",
                TransactionCategoryRuleMatch.DescriptionContains)
        ];

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal("Health - Fred", category);
    }

    [Fact]
    public void Categorize_NoAutomaticMatchDoesNotCategorizeNewTransaction()
    {
        var transaction = CreateTransaction("Local Chemist", -12m);
        TransactionCategoryRule[] rules =
        [
            new(
                "Local Chemist",
                "Health - Fred",
                TransactionCategoryRuleMatch.NoAutomaticMatch),
            new(
                "Chemist",
                "Health",
                TransactionCategoryRuleMatch.DescriptionContains)
        ];

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal(TransactionCategories.Uncategorised, category);
    }

    [Fact]
    public void Categorize_UnknownMerchantPreservesImportedCategory()
    {
        var transaction = CreateTransaction("Local merchant", -12m) with
        {
            Category = "Holiday"
        };

        var category = TransactionCategoryRules.Categorize(transaction, []);

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
        TransactionCategoryRule[] rules =
        [
            new("IGA", "Groceries", TransactionCategoryRuleMatch.DescriptionContains)
        ];

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal(TransactionCategories.Uncategorised, category);
    }

    [Fact]
    public void Categorize_MostSpecificContainsRuleWins()
    {
        var transaction = CreateTransaction("Uber Eats order", -24m);
        TransactionCategoryRule[] rules =
        [
            new("UBER", "Transport", TransactionCategoryRuleMatch.DescriptionContains),
            new("UBER EATS", "Dining", TransactionCategoryRuleMatch.DescriptionContains)
        ];

        var category = TransactionCategoryRules.Categorize(transaction, rules);

        Assert.Equal("Dining", category);
    }

    [Fact]
    public void Categories_UseNewCategoryFlowInsteadOfOther()
    {
        Assert.DoesNotContain("Other", TransactionCategories.Defaults);
    }

    [Fact]
    public void Categorize_OtherIsTreatedAsUncategorised()
    {
        var transaction = CreateTransaction("Unknown merchant", -12m) with
        {
            Category = "Other"
        };

        var category = TransactionCategoryRules.Categorize(transaction, []);

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
