namespace Ghos.Web.WebsiteHealth;

public sealed class WebsiteHealthOptions
{
    public const string SectionName = "WebsiteHealth";

    public bool SchedulerEnabled { get; set; }

    public int InitialDelaySeconds { get; set; } = 120;

    public int SchedulerPollMinutes { get; set; } = 5;
}
