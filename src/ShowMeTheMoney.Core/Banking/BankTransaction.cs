namespace ShowMeTheMoney.Core.Banking;

public sealed record BankTransaction(
    string Id,
    string AccountId,
    DateOnly PostedOn,
    string Description,
    string Category,
    decimal Amount,
    string Currency,
    bool IsPending);
