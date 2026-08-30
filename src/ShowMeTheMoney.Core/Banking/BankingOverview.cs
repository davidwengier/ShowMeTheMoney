namespace ShowMeTheMoney.Core.Banking;

public sealed record BankingOverview(
    string InstitutionName,
    string DataSourceDescription,
    IReadOnlyList<BankAccount> Accounts);
