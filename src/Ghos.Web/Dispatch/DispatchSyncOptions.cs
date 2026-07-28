namespace Ghos.Web.Dispatch;

public sealed class DispatchSyncOptions
{
    public const string SectionName = "DispatchSync";

    public string BaseUrl { get; set; } =
        "https://dispatch.winterwatch-pro.info";

    public string IntegrationSecret { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    public int InitialDelaySeconds { get; set; } = 30;
}
