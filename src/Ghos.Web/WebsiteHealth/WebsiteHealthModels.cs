using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.WebsiteHealth;

public enum WebsiteHealthRunStatus
{
    Running,
    Healthy,
    Degraded,
    Failed
}

public enum WebsiteHealthIssueSeverity
{
    Info,
    Warning,
    Critical
}

public enum WebsiteHealthCheckStatus
{
    Passed,
    Warning,
    Failed
}

public sealed class MonitoredSite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int CheckIntervalMinutes { get; set; } = 60;

    public int RequestTimeoutSeconds { get; set; } = 15;

    public int RequestDelayMilliseconds { get; set; } = 300;

    public int MaxCrawlPages { get; set; } = 25;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastCheckedAtUtc { get; set; }

    public ICollection<WebsiteCheck> Checks { get; set; } = [];

    public ICollection<WebsiteCheckRun> Runs { get; set; } = [];

    public ICollection<WebsiteHealthIssue> Issues { get; set; } = [];
}

public sealed class WebsiteCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MonitoredSiteId { get; set; }

    [MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? TargetPath { get; set; }

    public int Weight { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public MonitoredSite MonitoredSite { get; set; } = null!;

    public ICollection<WebsiteHealthMetric> Metrics { get; set; } = [];
}

public sealed class WebsiteCheckRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MonitoredSiteId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public WebsiteHealthRunStatus Status { get; set; } =
        WebsiteHealthRunStatus.Running;

    [MaxLength(32)]
    public string Trigger { get; set; } = "Manual";

    [MaxLength(450)]
    public string? RequestedByUserId { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public decimal OverallScore { get; set; }

    public decimal AvailabilityScore { get; set; }

    public decimal SecurityScore { get; set; }

    public decimal DiscoverabilityScore { get; set; }

    public decimal ContentScore { get; set; }

    public int PagesCrawled { get; set; }

    public int LinksChecked { get; set; }

    public MonitoredSite MonitoredSite { get; set; } = null!;

    public ICollection<WebsiteHealthMetric> Metrics { get; set; } = [];
}

public sealed class WebsiteHealthIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MonitoredSiteId { get; set; }

    [MaxLength(200)]
    public string Fingerprint { get; set; } = string.Empty;

    [MaxLength(80)]
    public string CheckKey { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? AffectedUrl { get; set; }

    public WebsiteHealthIssueSeverity Severity { get; set; }

    public DateTime FirstDetectedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastDetectedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAtUtc { get; set; }

    public Guid? LastSeenRunId { get; set; }

    public MonitoredSite MonitoredSite { get; set; } = null!;
}

public sealed class WebsiteHealthMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WebsiteCheckRunId { get; set; }

    public Guid? WebsiteCheckId { get; set; }

    [MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    public WebsiteHealthCheckStatus Status { get; set; }

    public decimal? NumericValue { get; set; }

    [MaxLength(40)]
    public string? Unit { get; set; }

    [MaxLength(1000)]
    public string? AffectedUrl { get; set; }

    [MaxLength(2000)]
    public string? Detail { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public WebsiteCheckRun WebsiteCheckRun { get; set; } = null!;

    public WebsiteCheck? WebsiteCheck { get; set; }
}
