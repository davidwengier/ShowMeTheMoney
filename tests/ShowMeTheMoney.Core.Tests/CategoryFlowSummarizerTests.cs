using ShowMeTheMoney.Core.Banking;
using Xunit;

namespace ShowMeTheMoney.Core.Tests;

public sealed class CategoryFlowSummarizerTests
{
    [Fact]
    public void Summarize_GroupsIncomeAndSpendingByCategory()
    {
        BankTransaction[] transactions =
        [
            CreateTransaction("Salary", "Income", 2500m),
            CreateTransaction("Interest", "Income", 10m),
            CreateTransaction("Supermarket", "Groceries", -80m),
            CreateTransaction("Second supermarket", "Groceries", -20m),
            CreateTransaction("Cafe", "Dining", -15m),
            CreateTransaction("Ignored", "Other", 0m)
        ];

        var summary = CategoryFlowSummarizer.Summarize(transactions);

        var income = Assert.Single(summary.MoneyIn);
        Assert.Equal("Income", income.Category);
        Assert.Equal(2510m, income.Amount);
        Assert.Collection(
            summary.MoneyOut,
            groceries =>
            {
                Assert.Equal("Groceries", groceries.Category);
                Assert.Equal(100m, groceries.Amount);
            },
            dining =>
            {
                Assert.Equal("Dining", dining.Category);
                Assert.Equal(15m, dining.Amount);
            });
    }

    [Fact]
    public void Summarize_BlankCategoryIsUncategorised()
    {
        var summary = CategoryFlowSummarizer.Summarize(
            [CreateTransaction("Unknown", " ", -25m)]);

        var flow = Assert.Single(summary.MoneyOut);
        Assert.Equal(TransactionCategories.Uncategorised, flow.Category);
        Assert.Equal(25m, flow.Amount);
    }

    private static BankTransaction CreateTransaction(
        string description,
        string category,
        decimal amount) =>
        new(
            Guid.NewGuid().ToString("N"),
            "account",
            new DateOnly(2026, 8, 30),
            description,
            category,
            amount,
            "AUD",
            false);
}
