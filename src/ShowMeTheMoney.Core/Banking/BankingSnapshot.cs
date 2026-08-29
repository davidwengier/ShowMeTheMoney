namespace ShowMeTheMoney.Core.Banking;

public sealed record BankingSnapshot(
    string InstitutionName,
    string DataSourceDescription,
    IReadOnlyList<BankAccount> Accounts,
    IReadOnlyList<BankTransaction> Transactions);
