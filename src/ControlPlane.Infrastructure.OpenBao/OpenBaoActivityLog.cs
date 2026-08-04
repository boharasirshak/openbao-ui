using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Stores the activity feed in the control plane's own KV mount, one entry per path so
/// a listing is a prefix walk.
///
/// <para>
/// Entries are tamper-evident by ACL rather than by convention: the member policy
/// grants "create" on this prefix without "update", so KV-v2 accepts the first write
/// to a path and refuses every later one. Members append with their own token, so
/// nobody can forge an entry as someone else or quietly rewrite history.
/// </para>
/// </summary>
public sealed class OpenBaoActivityLog(
    OpenBaoAdministrativeClient client,
    IOptions<OpenBaoOptions> options) : IActivityLog
{
    private string Root => $"v1/{options.Value.MetadataMount}";

    public async Task RecordAsync(ActivityEntry entry, CancellationToken cancellationToken)
    {
        var path = $"{Root}/data/activity/{entry.Project}/{ActivityId.Day(entry.At)}/{ActivityId.New(entry.At)}";
        try
        {
            await client.PostAsync(
                path,
                new
                {
                    data = new
                    {
                        at = entry.At.ToString("O"),
                        actor = entry.Actor,
                        action = entry.Action.ToString(),
                        project = entry.Project,
                        environment = entry.Environment ?? string.Empty,
                        path = entry.Path ?? string.Empty,
                        // Names only. Never the values.
                        keys = string.Join(',', entry.KeysAffected),
                        version = entry.Version?.ToString() ?? string.Empty,
                    },
                },
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Recording activity must never fail the operation the person actually
            // asked for. A member without the append grant still gets their write.
        }
    }

    public async Task<IReadOnlyList<ActivityEntry>> ReadAsync(
        string project,
        int days,
        CancellationToken cancellationToken)
    {
        var entries = new List<ActivityEntry>();
        var today = DateTimeOffset.UtcNow;

        for (var offset = 0; offset < Math.Clamp(days, 1, 90); offset++)
        {
            var day = ActivityId.Day(today.AddDays(-offset));
            var listing = await client.GetAsync(
                $"{Root}/metadata/activity/{project}/{day}?list=true",
                cancellationToken);
            if (listing is null
                || !listing.RootElement.TryGetProperty("data", out var listData)
                || !listData.TryGetProperty("keys", out var keys)
                || keys.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var key in keys.EnumerateArray().Select(value => value.GetString()).OfType<string>())
            {
                var document = await client.GetAsync(
                    $"{Root}/data/activity/{project}/{day}/{key}",
                    cancellationToken);
                var parsed = Parse(document);
                if (parsed is not null)
                {
                    entries.Add(parsed);
                }
            }
        }

        return entries.OrderByDescending(entry => entry.At).ToList();
    }

    private static ActivityEntry? Parse(JsonDocument? document)
    {
        if (document?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("data", out var data))
        {
            return null;
        }

        string Field(string name) =>
            data.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

        if (!DateTimeOffset.TryParse(Field("at"), out var at))
        {
            return null;
        }

        return new ActivityEntry(
            at,
            Field("actor"),
            Enum.TryParse<ActivityAction>(Field("action"), out var action) ? action : ActivityAction.SecretSaved,
            Field("project"),
            Field("environment") is { Length: > 0 } environment ? environment : null,
            Field("path") is { Length: > 0 } path ? path : null,
            Field("keys").Split(',', StringSplitOptions.RemoveEmptyEntries),
            int.TryParse(Field("version"), out var version) ? version : null);
    }
}
