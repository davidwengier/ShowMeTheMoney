using System.Text;
using ShowMeTheMoney.Core.Qif;
using Xunit;

namespace ShowMeTheMoney.Core.Tests;

public sealed class QifParserTests
{
    [Fact]
    public void Parse_ImportsAustralianBankTransactions()
    {
        const string qif = """
            !Account
            NEveryday Account
            TBank
            ^
            !Type:Bank
            D29/08/2026
            T-84.62
            PWoolworths Metro
            LGroceries
            ^
            D28/08'26
            T3650.00
            PSalary
            LIncome
            ^
            """;
        using var stream = CreateStream(qif);

        var result = new QifParser().Parse(stream, "transactions.qif");

        var account = Assert.Single(result.Snapshot.Accounts);
        Assert.Equal("Everyday Account", account.Name);
        Assert.Null(account.Balance);
        Assert.Equal(2, result.Snapshot.Transactions.Count);
        Assert.Equal("Woolworths Metro", result.Snapshot.Transactions[0].Description);
        Assert.Equal(new DateOnly(2026, 8, 29), result.Snapshot.Transactions[0].PostedOn);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_UsesFileNameAndMemoWhenOptionalFieldsAreMissing()
    {
        const string qif = """
            !Type:CCard
            D1/8/26
            T-25.99
            MStreaming service
            ^
            """;
        using var stream = CreateStream(qif);

        var result = new QifParser().Parse(stream, "Credit Card.qif");

        Assert.Equal("Credit Card", Assert.Single(result.Snapshot.Accounts).Name);
        Assert.Equal(
            "Streaming service",
            Assert.Single(result.Snapshot.Transactions).Description);
    }

    [Fact]
    public void Parse_SkipsMalformedRecordsAndReportsWarnings()
    {
        const string qif = """
            !Type:Bank
            Dnot-a-date
            T-10.00
            PBad transaction
            ^
            D29/08/2026
            T12.50
            PValid transaction
            ^
            """;
        using var stream = CreateStream(qif);

        var result = new QifParser().Parse(stream, "account.qif");

        Assert.Single(result.Snapshot.Transactions);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Parse_ThrowsWhenThereAreNoSupportedTransactions()
    {
        const string qif = """
            !Type:Bank
            D29/08/2026
            PNo amount
            ^
            """;
        using var stream = CreateStream(qif);

        var exception = Assert.Throws<QifParseException>(
            () => new QifParser().Parse(stream, "empty.qif"));

        Assert.Contains("did not contain", exception.Message);
    }

    private static MemoryStream CreateStream(string value) =>
        new(Encoding.UTF8.GetBytes(value));
}
