using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoAuditService(IOptions<OpenBaoOptions> options) : IAuditService
{
    public async Task<IReadOnlyList<AuditEvent>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        var path = options.Value.AuditLogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return lines
            .TakeLast(Math.Clamp(limit, 1, 500))
            .Select(Parse)
            .Where(item => item is not null)
            .Cast<AuditEvent>()
            .ToList();
    }

    private static AuditEvent? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var request = root.TryGetProperty("request", out var requestElement)
                ? requestElement
                : default;
            var auth = root.TryGetProperty("auth", out var authElement) ? authElement : default;
            return new AuditEvent(
                root.TryGetProperty("time", out var time) && DateTimeOffset.TryParse(time.GetString(), out var parsed)
                    ? parsed
                    : null,
                root.TryGetProperty("type", out var type) ? type.GetString() ?? "unknown" : "unknown",
                request.ValueKind == JsonValueKind.Object && request.TryGetProperty("operation", out var operation)
                    ? operation.GetString() ?? "unknown"
                    : "unknown",
                request.ValueKind == JsonValueKind.Object && request.TryGetProperty("path", out var path)
                    ? path.GetString() ?? "unknown"
                    : "unknown",
                auth.ValueKind == JsonValueKind.Object && auth.TryGetProperty("display_name", out var actor)
                    ? actor.GetString() ?? "unknown"
                    : "unknown");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
