using System.Diagnostics;
using System.Text.Json;

namespace ShowMeTheMoney.Desktop;

internal sealed record WindowPlacement(Rectangle Bounds, bool IsMaximized);

internal static class WindowPlacementStore
{
    public static WindowPlacement? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path));
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Window placement could not be loaded: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Window placement could not be loaded: {exception.Message}");
        }
        catch (JsonException exception)
        {
            Trace.TraceWarning($"Window placement could not be loaded: {exception.Message}");
        }

        return null;
    }

    public static void Save(string path, WindowPlacement placement)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(placement));
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Window placement could not be saved: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Window placement could not be saved: {exception.Message}");
        }
    }

    public static bool IsVisible(Rectangle bounds) =>
        bounds.Width > 0
        && bounds.Height > 0
        && Screen.AllScreens.Any(screen =>
            Rectangle.Intersect(screen.WorkingArea, bounds) is { Width: >= 100, Height: >= 100 });
}
