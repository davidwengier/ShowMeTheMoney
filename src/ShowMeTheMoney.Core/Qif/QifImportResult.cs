using ShowMeTheMoney.Core.Banking;

namespace ShowMeTheMoney.Core.Qif;

public sealed record QifImportResult(
    BankingSnapshot Snapshot,
    IReadOnlyList<string> Warnings);
