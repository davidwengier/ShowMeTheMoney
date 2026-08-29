namespace ShowMeTheMoney.Core.Banking;

public sealed record BankAccount(
    string Id,
    string Name,
    string MaskedNumber,
    decimal? Balance,
    string Currency);
