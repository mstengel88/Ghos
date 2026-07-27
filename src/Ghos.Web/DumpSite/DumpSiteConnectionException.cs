namespace Ghos.Web.DumpSite;

public sealed class DumpSiteConnectionException : Exception
{
    public DumpSiteConnectionException(string message)
        : base(message)
    {
    }

    public DumpSiteConnectionException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
