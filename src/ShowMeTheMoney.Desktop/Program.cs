using Microsoft.Extensions.DependencyInjection;
using ShowMeTheMoney.Core.Banking;
using ShowMeTheMoney.Core.Qif;
using ShowMeTheMoney.Storage.Sqlite;
using ShowMeTheMoney.UI.Services;
using Velopack;

namespace ShowMeTheMoney.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShowMeTheMoney");
        var databasePath = Environment.GetEnvironmentVariable("SHOW_ME_THE_MONEY_DATABASE");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            databasePath = Path.Combine(appDataDirectory, "show-me-the-money.db");
        }

        var dataStore = new SqliteBankingDataStore(databasePath);
        LegacyJsonSnapshotMigrator.MigrateAsync(
            Path.Combine(appDataDirectory, "banking-snapshot.json"),
            databasePath,
            dataStore).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IBankingDataSource>(dataStore);
        services.AddSingleton<IBankingDataStore>(dataStore);
        services.AddSingleton<QifParser>();
        services.AddSingleton<IQifFilePicker, WindowsQifFilePicker>();
        services.AddSingleton<IApplicationUpdateService, VelopackApplicationUpdateService>();

        using var serviceProvider = services.BuildServiceProvider();
        Application.Run(new MainForm(serviceProvider));
    }
}
