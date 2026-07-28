using System.Security.Cryptography;
using System.Text;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Backups;

public static class BackupStatusEndpoints
{
    private const string IntegrationHeader = "X-GHOS-Backup-Key";

    private static readonly IReadOnlyDictionary<string, string> Sources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ghos"] = "GHOS application",
            ["counterpoint"] = "CounterPoint"
        };

    public static IEndpointRouteBuilder MapBackupStatusEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/integrations/backup-status/{source}",
                UpdateStatusAsync)
            .AllowAnonymous()
            .RequireRateLimiting("integrations");

        return endpoints;
    }

    private static async Task<IResult> UpdateStatusAsync(
        string source,
        BackupStatusUpdate request,
        HttpContext httpContext,
        IOptions<BackupStatusOptions> options,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Sources.TryGetValue(source, out var displayName))
        {
            return Results.NotFound();
        }

        if (!HasValidSecret(httpContext, options.Value.IntegrationSecret))
        {
            return Results.Json(
                new { error = "Invalid integration credentials." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var state = NormalizeState(request.State ?? request.Status);
        if (state is null)
        {
            return Results.BadRequest(new
            {
                error = "State must be Running, Success, or Failure."
            });
        }

        var operation = FirstNotBlank(request.Operation, request.Phase, "Backup");
        var message = FirstNotBlank(request.Message, $"{displayName} reported {state}.");
        var now = DateTime.UtcNow;

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedSource = source.ToLowerInvariant();
        var record = await dbContext.BackupStatuses
            .SingleOrDefaultAsync(
                item => item.Source == normalizedSource,
                cancellationToken);

        if (record is null)
        {
            record = new BackupStatusRecord
            {
                Source = normalizedSource,
                DisplayName = displayName
            };
            dbContext.BackupStatuses.Add(record);
        }

        record.DisplayName = displayName;
        record.State = state;
        record.Operation = operation[..Math.Min(operation.Length, 80)];
        record.Message = message[..Math.Min(message.Length, 2000)];
        record.Host = string.IsNullOrWhiteSpace(request.Host)
            ? null
            : request.Host.Trim()[..Math.Min(request.Host.Trim().Length, 160)];
        record.UpdatedAtUtc = now;

        if (state == "Success" && IsBackupCompletion(operation))
        {
            record.LastSuccessfulBackupAtUtc = now;
        }
        else if (state == "Failure")
        {
            record.LastFailureAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new
        {
            source = record.Source,
            state = record.State,
            updatedAtUtc = record.UpdatedAtUtc
        });
    }

    private static bool HasValidSecret(
        HttpContext httpContext,
        string configuredSecret)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret) ||
            !httpContext.Request.Headers.TryGetValue(
                IntegrationHeader,
                out var suppliedValues))
        {
            return false;
        }

        var suppliedSecret = suppliedValues.ToString();
        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret);

        return configuredBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(
                configuredBytes,
                suppliedBytes);
    }

    private static string? NormalizeState(string? state) =>
        state?.Trim().ToLowerInvariant() switch
        {
            "running" or "inprogress" or "in_progress" => "Running",
            "success" or "successful" or "completed" => "Success",
            "failure" or "failed" or "error" => "Failure",
            _ => null
        };

    private static bool IsBackupCompletion(string operation) =>
        operation.Equals("Backup", StringComparison.OrdinalIgnoreCase) ||
        operation.Equals("Complete", StringComparison.OrdinalIgnoreCase);

    private static string FirstNotBlank(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private sealed record BackupStatusUpdate(
        string? State,
        string? Status,
        string? Operation,
        string? Phase,
        string? Message,
        string? Host);
}
