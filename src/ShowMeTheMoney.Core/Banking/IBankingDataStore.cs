namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataStore : IBankingDataSource
{
    Task AddAccountAsync(
        BankAccount account,
        CancellationToken cancellationToken = default);

    Task UpdateAccountAsync(
        string accountId,
        string name,
        decimal? balance,
        CancellationToken cancellationToken = default);

    Task SetTransactionCategoryAsync(
        string transactionId,
        string category,
        TransactionCategoryAssignmentScope scope,
        CancellationToken cancellationToken = default);

    Task<TransactionCategoryAssignmentPreview> GetTransactionCategoryAssignmentPreviewAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionCategory>> GetTransactionCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task AddTransactionCategoryAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task RenameTransactionCategoryAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default);

    Task DeleteTransactionCategoryAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<UncategorisedTransaction?> GetRandomUncategorisedTransactionAsync(
        CancellationToken cancellationToken = default);

    Task<int> ApplyTransactionCategoryRulesAsync(
        string accountId,
        CancellationToken cancellationToken = default);

    Task<TransactionPage> GetTransactionPageAsync(
        string accountId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionCategoryRule>> GetTransactionCategoryRulesAsync(
        CancellationToken cancellationToken = default);

    Task SaveTransactionCategoryRuleAsync(
        string? originalPattern,
        TransactionCategoryRule rule,
        CancellationToken cancellationToken = default);

    Task DeleteTransactionCategoryRuleAsync(
        string pattern,
        CancellationToken cancellationToken = default);

    Task ImportTransactionsAsync(
        string accountId,
        IReadOnlyList<BankTransaction> transactions,
        string dataSourceDescription,
        CancellationToken cancellationToken = default);

    Task ReplaceSnapshotAsync(
        BankingSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
