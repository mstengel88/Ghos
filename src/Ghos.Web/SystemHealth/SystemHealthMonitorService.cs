using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ghos.Web.SystemHealth;

public sealed class SystemHealthMonitorService(
    HttpClient httpClient,
    IOptions<SystemHealthOptions> options,
    ILogger<SystemHealthMonitorService> logger)
{
    private readonly SystemHealthOptions _options = options.Value;

    public async Task<SystemHealthSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var checkedAtUtc = DateTime.UtcNow;
        var applicationsTask = ProbeApplicationsAsync(cancellationToken);
        var containersTask = GetContainersAsync(cancellationToken);

        await Task.WhenAll(applicationsTask, containersTask);

        var (containers, error) = await containersTask;
        return new SystemHealthSnapshot(
            checkedAtUtc,
            await applicationsTask,
            containers,
            error);
    }

    private async Task<IReadOnlyList<ApplicationHealthResult>>
        ProbeApplicationsAsync(CancellationToken cancellationToken)
    {
        var tasks = _options.Applications
            .Where(target =>
                !string.IsNullOrWhiteSpace(target.Name) &&
                Uri.TryCreate(target.Url, UriKind.Absolute, out _))
            .Select(target => ProbeApplicationAsync(target, cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private async Task<ApplicationHealthResult> ProbeApplicationAsync(
        ApplicationHealthTarget target,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(_options.ProbeTimeoutSeconds, 2, 30)));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                target.Url);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            stopwatch.Stop();
            var isHealthy = (int)response.StatusCode is >= 200 and < 400;

            return new ApplicationHealthResult(
                target.Name,
                target.Category,
                isHealthy,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                isHealthy ? "Online" : $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new ApplicationHealthResult(
                target.Name,
                target.Category,
                false,
                null,
                null,
                "Timed out");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Health probe failed for {ApplicationName}.",
                target.Name);
            return new ApplicationHealthResult(
                target.Name,
                target.Category,
                false,
                null,
                null,
                "Unavailable");
        }
    }

    private async Task<(
        IReadOnlyList<ContainerHealthResult> Containers,
        string? Error)> GetContainersAsync(
            CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _options.ContainerApiBaseUrl.TrimEnd('/');
            var containers = await httpClient.GetFromJsonAsync<
                List<DockerContainerSummary>>(
                $"{baseUrl}/containers/json?all=1",
                cancellationToken) ?? [];

            return (containers
                .Select(MapContainer)
                .OrderBy(item => item.Health == ContainerHealthState.Stopped)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(), null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The read-only Docker status endpoint could not be queried.");
            return ([], "Container status is unavailable.");
        }
    }

    private static ContainerHealthResult MapContainer(
        DockerContainerSummary container)
    {
        var name = container.Names.FirstOrDefault()?.TrimStart('/')
            ?? container.Id[..Math.Min(12, container.Id.Length)];
        var status = container.Status ?? container.State ?? "Unknown";
        var health = GetContainerHealth(container.State, status);

        return new ContainerHealthResult(
            name,
            container.Image,
            container.State ?? "unknown",
            status,
            health);
    }

    private static ContainerHealthState GetContainerHealth(
        string? state,
        string status)
    {
        if (status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Unhealthy;
        }

        if (status.Contains("health: starting", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("starting", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Starting;
        }

        if (status.Contains("healthy", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Healthy;
        }

        return string.Equals(state, "running", StringComparison.OrdinalIgnoreCase)
            ? ContainerHealthState.Running
            : ContainerHealthState.Stopped;
    }

    private sealed class DockerContainerSummary
    {
        [JsonPropertyName("Id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("Names")]
        public List<string> Names { get; init; } = [];

        [JsonPropertyName("Image")]
        public string Image { get; init; } = string.Empty;

        [JsonPropertyName("State")]
        public string? State { get; init; }

        [JsonPropertyName("Status")]
        public string? Status { get; init; }
    }
}
