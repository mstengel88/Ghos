namespace Ghos.Web.Dispatch;

public sealed class DispatchSyncOptions
{
    public const string SectionName = "DispatchSync";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    public int InitialDelaySeconds { get; set; } = 30;
}
