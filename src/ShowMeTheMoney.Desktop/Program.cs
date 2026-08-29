using Microsoft.Extensions.DependencyInjection;
using ShowMeTheMoney.Core.Banking;
using ShowMeTheMoney.Core.Qif;
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
        var dataPath = Path.Combine(appDataDirectory, "banking-snapshot.json");
        var dataStore = new JsonBankingDataStore(dataPath);

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
