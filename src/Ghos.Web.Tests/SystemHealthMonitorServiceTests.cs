using System.Net;
using System.Text;
using Ghos.Web.SystemHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class SystemHealthMonitorServiceTests
{
    [Fact]
    public async Task SnapshotIncludesApplicationAndEveryContainer()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/containers/json")
            {
                const string json = """
                    [
                      {
                        "Id":"one",
                        "Names":["/healthy-service"],
                        "Image":"example/healthy:1",
                        "State":"running",
                        "Status":"Up 2 hours (healthy)"
                      },
                      {
                        "Id":"two",
                        "Names":["/worker"],
                        "Image":"example/worker:1",
                        "State":"running",
                        "Status":"Up 2 hours"
                      },
                      {
                        "Id":"three",
                        "Names":["/stopped-service"],
                        "Image":"example/stopped:1",
                        "State":"exited",
                        "Status":"Exited (1) 2 minutes ago"
                      }
                    ]
                    """;
                return Json(HttpStatusCode.OK, json);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = Options.Create(new SystemHealthOptions
        {
            ContainerApiBaseUrl = "http://docker-status-proxy:2375",
            Applications =
            [
                new ApplicationHealthTarget
                {
                    Name = "GHOS",
                    Category = "Core",
                    Url = "http://ghos/healthz"
                }
            ]
        });
        var service = new SystemHealthMonitorService(
            new HttpClient(handler),
            options,
            NullLogger<SystemHealthMonitorService>.Instance);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Single(snapshot.Applications);
        Assert.True(snapshot.Applications[0].IsHealthy);
        Assert.Equal(3, snapshot.Containers.Count);
        Assert.Equal(
            ContainerHealthState.Healthy,
            snapshot.Containers.Single(item =>
                item.Name == "healthy-service").Health);
        Assert.Equal(
            ContainerHealthState.Running,
            snapshot.Containers.Single(item =>
                item.Name == "worker").Health);
        Assert.Equal(
            ContainerHealthState.Stopped,
            snapshot.Containers.Single(item =>
                item.Name == "stopped-service").Health);
    }

    [Fact]
    public async Task FailedProbeDoesNotHideContainerResults()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/containers/json")
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var options = Options.Create(new SystemHealthOptions
        {
            Applications =
            [
                new ApplicationHealthTarget
                {
                    Name = "Unavailable app",
                    Url = "http://app/healthz"
                }
            ]
        });
        var service = new SystemHealthMonitorService(
            new HttpClient(handler),
            options,
            NullLogger<SystemHealthMonitorService>.Instance);

        var snapshot = await service.GetSnapshotAsync();

        Assert.False(snapshot.Applications[0].IsHealthy);
        Assert.Equal(503, snapshot.Applications[0].StatusCode);
        Assert.Null(snapshot.ContainerQueryError);
    }

    private static HttpResponseMessage Json(
        HttpStatusCode statusCode,
        string json) =>
        new(statusCode)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
