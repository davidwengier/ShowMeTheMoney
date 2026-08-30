using System.Text.Json;
using ShowMeTheMoney.Core.Banking;

namespace ShowMeTheMoney.Desktop;

internal static class LegacyJsonSnapshotMigrator
{
    public static async Task MigrateAsync(
        string legacyJsonPath,
        string databasePath,
        IBankingDataStore dataStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(dataStore);

        if (!File.Exists(legacyJsonPath) || File.Exists(databasePath))
        {
            return;
        }

        await using var stream = new FileStream(
            legacyJsonPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var snapshot = await JsonSerializer.DeserializeAsync<BankingSnapshot>(stream)
            ?? throw new InvalidDataException(
                $"The legacy transaction data at '{legacyJsonPath}' is empty.");

        await dataStore.ReplaceSnapshotAsync(snapshot);
        File.Delete(legacyJsonPath);
    }
}
