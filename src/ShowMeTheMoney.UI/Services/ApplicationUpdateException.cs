namespace ShowMeTheMoney.UI.Services;

public sealed class ApplicationUpdateException : Exception
{
    public ApplicationUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
