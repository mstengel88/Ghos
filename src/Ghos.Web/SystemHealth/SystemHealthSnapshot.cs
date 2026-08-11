namespace Ghos.Web.SystemHealth;

public sealed record SystemHealthSnapshot(
    DateTime CheckedAtUtc,
    IReadOnlyList<ApplicationHealthResult> Applications,
    IReadOnlyList<ContainerHealthResult> Containers,
    string? ContainerQueryError);

public sealed record ApplicationHealthResult(
    string Name,
    string Category,
    bool IsHealthy,
    int? StatusCode,
    long? ResponseMilliseconds,
    string Status);

public sealed record ContainerHealthResult(
    string Name,
    string Image,
    string State,
    string Status,
    ContainerHealthState Health);

public enum ContainerHealthState
{
    Healthy,
    Running,
    Starting,
    Unhealthy,
    Stopped
}
