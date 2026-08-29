using System.Text.Json;
using ShowMeTheMoney.Core.Banking;

namespace ShowMeTheMoney.Desktop;

internal sealed class JsonBankingDataStore : IBankingDataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BankingSnapshot? _snapshot;

    public JsonBankingDataStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<BankingSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot is not null)
            {
                return _snapshot;
            }

            if (!File.Exists(_filePath))
            {
                _snapshot = new BankingSnapshot(
                    "No bank data imported",
                    "Import a QIF file to get started",
                    [],
                    []);
                return _snapshot;
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            _snapshot = await JsonSerializer.DeserializeAsync<BankingSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken)
                ?? throw new InvalidDataException(
                    $"The saved transaction data at '{_filePath}' is empty.");
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceSnapshotAsync(
        BankingSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException(
                    $"The data path '{_filePath}' does not have a parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        snapshot,
                        SerializerOptions,
                        cancellationToken);
                }

                File.Move(temporaryPath, _filePath, overwrite: true);
                _snapshot = snapshot;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
