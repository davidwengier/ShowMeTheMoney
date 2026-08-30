using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using ShowMeTheMoney.UI;

namespace ShowMeTheMoney.Desktop;

public sealed class MainForm : Form
{
    private const string ApplicationIconResourceName =
        "ShowMeTheMoney.Desktop.Assets.ShowMeTheMoney.ico";

    internal MainForm(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Text = "Show Me The Money";
        Icon = LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 680);
        Size = new Size(1360, 860);

        var blazorWebView = new EmbeddedBlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot\\index.html",
            Services = services
        };
        blazorWebView.RootComponents.Add<App>("#app");

        Controls.Add(blazorWebView);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(
            ApplicationIconResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded application icon '{ApplicationIconResourceName}' was not found.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
