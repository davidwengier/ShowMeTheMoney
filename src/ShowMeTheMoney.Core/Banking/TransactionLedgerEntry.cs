namespace ShowMeTheMoney.Core.Banking;

public sealed record TransactionLedgerEntry(
    BankTransaction Transaction,
    decimal? RunningBalance);
