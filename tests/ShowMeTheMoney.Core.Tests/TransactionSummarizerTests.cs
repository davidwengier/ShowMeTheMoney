using ShowMeTheMoney.Core.Banking;
using Xunit;

namespace ShowMeTheMoney.Core.Tests;

public sealed class TransactionSummarizerTests
{
    [Fact]
    public void Summarize_SeparatesIncomeAndSpending()
    {
        BankTransaction[] transactions =
        [
            CreateTransaction(2_000m),
            CreateTransaction(-125.50m),
            CreateTransaction(-24.50m)
        ];

        var result = TransactionSummarizer.Summarize(transactions);

        Assert.Equal(2_000m, result.Income);
        Assert.Equal(150m, result.Spending);
        Assert.Equal(1_850m, result.NetCashFlow);
    }

    private static BankTransaction CreateTransaction(decimal amount) =>
        new(
            Guid.NewGuid().ToString(),
            "account",
            new DateOnly(2026, 8, 29),
            "Description",
            "Category",
            amount,
            "AUD",
            false);
}
