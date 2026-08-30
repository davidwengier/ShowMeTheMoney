using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using ShowMeTheMoney.UI;

namespace ShowMeTheMoney.Desktop;

public sealed class MainForm : Form
{
    private const string ApplicationIconResourceName =
        "ShowMeTheMoney.Desktop.Assets.ShowMeTheMoney.ico";
    private readonly string _windowPlacementPath;

    internal MainForm(IServiceProvider services, string windowPlacementPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowPlacementPath);

        _windowPlacementPath = windowPlacementPath;
        Text = "Show Me The Money";
        Icon = LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 680);
        Size = new Size(1360, 860);
        RestoreWindowPlacement();

        var blazorWebView = new EmbeddedBlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot\\index.html",
            Services = services
        };
        blazorWebView.RootComponents.Add<App>("#app");

        Controls.Add(blazorWebView);
        FormClosing += SaveWindowPlacement;
    }

    private void RestoreWindowPlacement()
    {
        var placement = WindowPlacementStore.Load(_windowPlacementPath);
        if (placement is null || !WindowPlacementStore.IsVisible(placement.Bounds))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = placement.Bounds;
        if (placement.IsMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowPlacement(object? sender, FormClosingEventArgs args)
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        WindowPlacementStore.Save(
            _windowPlacementPath,
            new WindowPlacement(bounds, WindowState == FormWindowState.Maximized));
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
