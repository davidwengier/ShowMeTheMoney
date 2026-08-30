namespace ShowMeTheMoney.Core.Banking;

public sealed record TransactionPage(
    IReadOnlyList<TransactionLedgerEntry> Entries,
    int TotalCount,
    int PageIndex,
    int PageSize);
