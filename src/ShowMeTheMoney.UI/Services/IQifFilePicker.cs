namespace ShowMeTheMoney.UI.Services;

public interface IQifFilePicker
{
    Task<SelectedQifFile?> PickAsync();
}
