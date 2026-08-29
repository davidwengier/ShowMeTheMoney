using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using ShowMeTheMoney.UI;

namespace ShowMeTheMoney.Desktop;

public sealed class MainForm : Form
{
    internal MainForm(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Text = "Show Me The Money";
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
}
