namespace ShowMeTheMoney.Core.Banking;

public sealed record UncategorisedTransaction(
    BankTransaction Transaction,
    string AccountName);
