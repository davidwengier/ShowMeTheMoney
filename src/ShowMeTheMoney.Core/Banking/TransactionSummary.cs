namespace ShowMeTheMoney.Core.Banking;

public sealed record TransactionSummary(
    decimal Income,
    decimal Spending,
    decimal NetCashFlow);
