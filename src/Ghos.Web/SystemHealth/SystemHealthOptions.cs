namespace Ghos.Web.SystemHealth;

public sealed class SystemHealthOptions
{
    public const string SectionName = "SystemHealth";

    public string ContainerApiBaseUrl { get; set; } =
        "http://docker-status-proxy:2375";

    public int ProbeTimeoutSeconds { get; set; } = 5;

    public List<ApplicationHealthTarget> Applications { get; set; } = [];
}

public sealed class ApplicationHealthTarget
{
    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = "Application";

    public string Url { get; set; } = string.Empty;
}
