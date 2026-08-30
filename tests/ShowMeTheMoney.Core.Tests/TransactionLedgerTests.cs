using ShowMeTheMoney.Core.Banking;
using Xunit;

namespace ShowMeTheMoney.Core.Tests;

public sealed class TransactionLedgerTests
{
    [Fact]
    public void Build_WalksBackwardFromCurrentBalance()
    {
        BankTransaction[] transactions =
        [
            CreateTransaction("newest", new DateOnly(2026, 8, 30), -25m),
            CreateTransaction("middle", new DateOnly(2026, 8, 29), 100m),
            CreateTransaction("oldest", new DateOnly(2026, 8, 28), -10m)
        ];

        var entries = TransactionLedger.Build(transactions, 500m);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("newest", entry.Transaction.Id);
                Assert.Equal(500m, entry.RunningBalance);
            },
            entry =>
            {
                Assert.Equal("middle", entry.Transaction.Id);
                Assert.Equal(525m, entry.RunningBalance);
            },
            entry =>
            {
                Assert.Equal("oldest", entry.Transaction.Id);
                Assert.Equal(425m, entry.RunningBalance);
            });
    }

    [Fact]
    public void Build_LeavesBalancesUnknownWithoutCurrentBalance()
    {
        var entries = TransactionLedger.Build(
            [CreateTransaction("transaction", new DateOnly(2026, 8, 30), -25m)],
            null);

        Assert.Null(Assert.Single(entries).RunningBalance);
    }

    private static BankTransaction CreateTransaction(
        string id,
        DateOnly date,
        decimal amount) =>
        new(
            id,
            "account",
            date,
            "Description",
            "Category",
            amount,
            "AUD",
            false);
}
