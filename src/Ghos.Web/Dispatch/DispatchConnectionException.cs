namespace Ghos.Web.Dispatch;

public sealed class DispatchConnectionException : Exception
{
    public DispatchConnectionException(string message)
        : base(message)
    {
    }

    public DispatchConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
