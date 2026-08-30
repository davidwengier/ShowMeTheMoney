namespace ShowMeTheMoney.Core.Banking;

public sealed record TransactionCategoryAssignmentPreview(
    string TransactionId,
    string Description,
    string NormalizedDescription,
    int OtherMatchingCount,
    int OtherUncategorisedCount);
