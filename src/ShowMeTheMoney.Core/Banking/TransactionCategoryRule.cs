namespace ShowMeTheMoney.Core.Banking;

public sealed record TransactionCategoryRule(
    string Pattern,
    string Category,
    TransactionCategoryRuleMatch Match);
