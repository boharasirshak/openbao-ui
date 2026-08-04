using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Approval workflow for protected environments.
///
/// <para>
/// The split matters: the proposed values go through <see cref="ISecretsEngine"/> into
/// the target project's own mount, so OpenBao decides who can read them. Only the
/// envelope — who asked, what path, which key names, the review trail — is kept in the
/// control plane's mount. A key name is not a secret; a value is.
/// </para>
/// </summary>
public sealed class OpenBaoChangeRequestService(
    OpenBaoAdministrativeClient client,
    ISecretsEngine engine,
    IOptions<OpenBaoOptions> options) : IChangeRequestService
{
    private static readonly EnvironmentId Pending = EnvironmentId.Reserved(ChangeRequest.PendingEnvironment);

    private string Root => $"v1/{options.Value.MetadataMount}";

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(
        ProjectId project,
        CancellationToken cancellationToken)
    {
        var listing = await client.GetAsync($"{Root}/metadata/changes/{project}?list=true", cancellationToken);
        if (listing?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var requests = new List<ChangeRequest>();
        foreach (var id in keys.EnumerateArray().Select(key => key.GetString()).OfType<string>())
        {
            var request = await GetAsync(project, id.TrimEnd('/'), cancellationToken);
            if (request is not null)
            {
                requests.Add(request);
            }
        }

        // Ids start with a tick count, so newest first is a plain descending sort.
        return requests.OrderByDescending(request => request.Id, StringComparer.Ordinal).ToList();
    }

    public async Task<ChangeRequest?> GetAsync(
        ProjectId project,
        string id,
        CancellationToken cancellationToken)
    {
        Identifier.ValidateSegment(id, nameof(id));
        var document = await client.GetAsync($"{Root}/data/changes/{project}/{id}", cancellationToken);
        if (document?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("data", out var stored))
        {
            return null;
        }

        return Read(project, id, stored);
    }

    public async Task<SecretDocument?> ReadProposedAsync(
        ChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.IsOpen || !request.HasProposedValues)
        {
            return null;
        }

        return await engine.ReadAsync(
            request.Project,
            Pending,
            request.PendingPath,
            cancellationToken);
    }

    public async Task<ChangeRequest> ProposeAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        IReadOnlyDictionary<string, string> values,
        string? description,
        string? reason,
        int? expectedVersion,
        bool isDeletion,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        if (isDeletion)
        {
            values = new Dictionary<string, string>();
        }
        else if (values.Count == 0 || !values.Keys.All(Identifier.IsValidSecretKey))
        {
            throw new ArgumentException("A change needs at least one valid key.", nameof(values));
        }

        var at = DateTimeOffset.UtcNow;
        var request = new ChangeRequest(
            ChangeRequest.NewId(at),
            project,
            environment,
            path,
            [.. values.Keys.Order(StringComparer.Ordinal)],
            requestedBy,
            at,
            ChangeRequestStatus.Pending,
            reason,
            expectedVersion,
            [],
            isDeletion);

        // Values first. If the envelope write then fails, the payload is an orphan that
        // nothing points at — harmless. The other order would leave a visible request
        // that cannot be applied.
        if (request.HasProposedValues)
        {
            await engine.WriteAsync(
                project,
                Pending,
                request.PendingPath,
                new SecretDocument(values, 0, description),
                expectedVersion: null,
                cancellationToken);
        }

        await SaveAsync(request, cancellationToken);
        return request;
    }

    public async Task<ChangeRequest> ApplyAsync(
        ProjectId project,
        string id,
        string reviewer,
        string? note,
        CancellationToken cancellationToken)
    {
        var request = await RequireOpenAsync(project, id, reviewer, cancellationToken);
        if (request.IsDeletion)
        {
            await engine.DeleteAsync(request.Project, request.Environment, request.Path, cancellationToken);
        }
        else
        {
            var proposed = await ReadProposedAsync(request, cancellationToken)
                ?? throw new InvalidOperationException("The proposed values are no longer available.");

            // The version check the proposer saw still applies at apply time, so a change
            // reviewed against v4 will not silently overwrite someone else's v5.
            await engine.WriteAsync(
                request.Project,
                request.Environment,
                request.Path,
                proposed,
                request.ExpectedVersion,
                cancellationToken);
        }

        var applied = request with
        {
            Status = ChangeRequestStatus.Applied,
            Reviews = [.. request.Reviews, new ChangeReview(reviewer, true, DateTimeOffset.UtcNow, note)],
        };
        await SaveAsync(applied, cancellationToken);
        await DiscardProposedAsync(request, cancellationToken);
        return applied;
    }

    public async Task<ChangeRequest> RejectAsync(
        ProjectId project,
        string id,
        string reviewer,
        string? note,
        CancellationToken cancellationToken)
    {
        var request = await RequireOpenAsync(project, id, reviewer, cancellationToken);
        var rejected = request with
        {
            Status = ChangeRequestStatus.Rejected,
            Reviews = [.. request.Reviews, new ChangeReview(reviewer, false, DateTimeOffset.UtcNow, note)],
        };
        await SaveAsync(rejected, cancellationToken);
        await DiscardProposedAsync(request, cancellationToken);
        return rejected;
    }

    public async Task<ChangeRequest> WithdrawAsync(
        ProjectId project,
        string id,
        string requester,
        CancellationToken cancellationToken)
    {
        var request = await GetAsync(project, id, cancellationToken)
            ?? throw new KeyNotFoundException("No such change request.");
        if (!request.IsOpen)
        {
            throw new InvalidOperationException("That change is already closed.");
        }

        if (!string.Equals(request.RequestedBy, requester, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only the person who asked for a change can withdraw it.");
        }

        var withdrawn = request with { Status = ChangeRequestStatus.Withdrawn };
        await SaveAsync(withdrawn, cancellationToken);
        await DiscardProposedAsync(request, cancellationToken);
        return withdrawn;
    }

    private async Task<ChangeRequest> RequireOpenAsync(
        ProjectId project,
        string id,
        string reviewer,
        CancellationToken cancellationToken)
    {
        var request = await GetAsync(project, id, cancellationToken)
            ?? throw new KeyNotFoundException("No such change request.");
        if (!request.IsOpen)
        {
            throw new InvalidOperationException("That change is already closed.");
        }

        if (!request.CanBeReviewedBy(reviewer))
        {
            throw new UnauthorizedAccessException("A change cannot be reviewed by the person who asked for it.");
        }

        return request;
    }

    /// <summary>
    /// Once a change is closed the proposed values have no reason to exist, so the whole
    /// version history goes with them rather than lingering as a readable copy.
    /// </summary>
    private Task DiscardProposedAsync(ChangeRequest request, CancellationToken cancellationToken) =>
        request.HasProposedValues
            ? engine.PurgeAsync(request.Project, Pending, request.PendingPath, cancellationToken)
            : Task.CompletedTask;

    private Task SaveAsync(ChangeRequest request, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"{Root}/data/changes/{request.Project}/{request.Id}",
            new
            {
                data = new
                {
                    environment = request.Environment.Value,
                    path = request.Path.Value,
                    keys = request.KeysAffected,
                    requestedBy = request.RequestedBy,
                    requestedAt = request.RequestedAt.ToString("O"),
                    status = request.Status.ToString(),
                    reason = request.Reason ?? string.Empty,
                    expectedVersion = request.ExpectedVersion,
                    isDeletion = request.IsDeletion,
                    reviews = request.Reviews.Select(review => new
                    {
                        reviewer = review.Reviewer,
                        approved = review.Approved,
                        at = review.At.ToString("O"),
                        note = review.Note ?? string.Empty,
                    }).ToArray(),
                },
            },
            cancellationToken);

    private static ChangeRequest Read(ProjectId project, string id, JsonElement stored)
    {
        var reviews = new List<ChangeReview>();
        if (stored.TryGetProperty("reviews", out var storedReviews)
            && storedReviews.ValueKind == JsonValueKind.Array)
        {
            reviews.AddRange(storedReviews.EnumerateArray().Select(review => new ChangeReview(
                Text(review, "reviewer") ?? "unknown",
                review.TryGetProperty("approved", out var approved) && IsTrue(approved),
                Time(review, "at"),
                Text(review, "note"))));
        }

        return new ChangeRequest(
            id,
            project,
            EnvironmentId.Parse(Text(stored, "environment")!),
            SecretPath.Parse(Text(stored, "path")!),
            stored.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
                ? [.. keys.EnumerateArray().Select(key => key.GetString()).OfType<string>()]
                : [],
            Text(stored, "requestedBy") ?? "unknown",
            Time(stored, "requestedAt"),
            Enum.TryParse<ChangeRequestStatus>(Text(stored, "status"), out var status)
                ? status
                : ChangeRequestStatus.Pending,
            Text(stored, "reason"),
            Version(stored),
            reviews,
            stored.TryGetProperty("isDeletion", out var deletion) && IsTrue(deletion));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;

    private static DateTimeOffset Time(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), out var at) ? at : DateTimeOffset.UnixEpoch;

    private static bool IsTrue(JsonElement value) =>
        value.ValueKind == JsonValueKind.True
        || (value.ValueKind == JsonValueKind.String && value.GetString() == "true");

    // KV round-trips numbers as JSON numbers, but a hand-edited entry could hold a
    // string, so both are accepted rather than throwing on read.
    private static int? Version(JsonElement stored)
    {
        if (!stored.TryGetProperty("expectedVersion", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
