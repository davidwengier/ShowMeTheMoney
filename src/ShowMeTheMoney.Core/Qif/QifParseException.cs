namespace ShowMeTheMoney.Core.Qif;

public sealed class QifParseException : Exception
{
    public QifParseException(string message)
        : base(message)
    {
    }
}
