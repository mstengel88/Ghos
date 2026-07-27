namespace Ghos.Web.Dispatch;

public static class DispatchOrderLinks
{
    public static string? Build(
        string? baseUrl,
        string? externalDispatchId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(externalDispatchId))
        {
            return null;
        }

        return $"{baseUrl.TrimEnd('/')}/orders?order=" +
            Uri.EscapeDataString(externalDispatchId);
    }
}
