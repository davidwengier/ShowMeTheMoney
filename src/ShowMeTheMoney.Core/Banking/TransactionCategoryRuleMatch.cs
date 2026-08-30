namespace ShowMeTheMoney.Core.Banking;

public enum TransactionCategoryRuleMatch
{
    NoAutomaticMatch,
    ExactDescription,
    DescriptionContains,
    MoneyInDescriptionContains
}
