using ShowMeTheMoney.UI.Services;

namespace ShowMeTheMoney.Desktop;

internal sealed class WindowsQifFilePicker : IQifFilePicker
{
    public Task<SelectedQifFile?> PickAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Quicken Interchange Format (*.qif)|*.qif|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = "Import bank transactions"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Task.FromResult<SelectedQifFile?>(null);
        }

        SelectedQifFile file = new(
            Path.GetFileName(dialog.FileName),
            new FileStream(
                dialog.FileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read));
        return Task.FromResult<SelectedQifFile?>(file);
    }
}
