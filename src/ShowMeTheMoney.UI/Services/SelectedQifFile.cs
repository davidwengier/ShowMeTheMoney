namespace ShowMeTheMoney.UI.Services;

public sealed record SelectedQifFile(string FileName, Stream Content) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
