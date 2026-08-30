namespace ShowMeTheMoney.Core.Banking;

public sealed record CategoryFlowSummary(
    IReadOnlyList<CategoryFlow> MoneyIn,
    IReadOnlyList<CategoryFlow> MoneyOut);
