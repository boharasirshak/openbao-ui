using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Access requests, stored one per person per project. Submitting runs with the
/// requester's own token under the member-base grant (write-only, so nobody browses
/// other people's requests); listing and reviewing run under the reviewer's admin
/// authority. Approving merges the requested roles into whatever the person already
/// holds — it never replaces their other access.
/// </summary>
public sealed class OpenBaoAccessRequestService(
    OpenBaoAdministrativeClient client,
    IIdentityService identity,
    IOptions<OpenBaoOptions> options) : IAccessRequestService
{
    private string Mount => options.Value.MetadataMount;

    public async Task<IReadOnlyList<AccessRequest>> ListAsync(
        ProjectId project,
        CancellationToken cancellationToken)
    {
        var listing = await client.GetAsync(
            $"v1/{ControlPlanePaths.AccessRequestsMetadata(Mount, project.Value)}?list=true",
            cancellationToken);
        if (listing?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var requests = new List<AccessRequest>();
        foreach (var username in keys.EnumerateArray().Select(key => key.GetString()).OfType<string>())
        {
            var request = await GetAsync(project, username.TrimEnd('/'), cancellationToken);
            if (request is not null)
            {
                requests.Add(request);
            }
        }

        return requests
            .OrderByDescending(request => request.IsOpen)
            .ThenByDescending(request => request.RequestedAt)
            .ToList();
    }

    public Task SubmitAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        if (request.Policies.Count == 0)
        {
            throw new ArgumentException("Pick at least one role to ask for.", nameof(request));
        }

        return client.PostAsync(
            $"v1/{ControlPlanePaths.AccessRequest(Mount, request.Project.Value, request.Username)}",
            new
            {
                data = new
                {
                    policies = request.Policies,
                    reason = request.Reason ?? string.Empty,
                    requestedAt = request.RequestedAt.ToString("O"),
                    status = AccessRequestStatus.Pending.ToString(),
                    reviewedBy = string.Empty,
                    reviewedAt = string.Empty,
                },
            },
            cancellationToken);
    }

    public async Task<AccessRequest> ApproveAsync(
        ProjectId project,
        string username,
        string reviewer,
        CancellationToken cancellationToken)
    {
        var request = await RequireOpenAsync(project, username, reviewer, cancellationToken);

        // Merge, never replace: the request adds roles on this project and leaves every
        // other grant the person holds exactly as it was.
        var member = (await identity.ListAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Username == username)
            ?? throw new KeyNotFoundException("That account no longer exists.");
        await identity.SetPoliciesAsync(
            username,
            [.. member.Policies.Concat(request.Policies).Distinct(StringComparer.Ordinal)],
            cancellationToken);

        var approved = request with
        {
            Status = AccessRequestStatus.Approved,
            ReviewedBy = reviewer,
            ReviewedAt = DateTimeOffset.UtcNow,
        };
        await SaveAsync(approved, cancellationToken);
        return approved;
    }

    public async Task<AccessRequest> RejectAsync(
        ProjectId project,
        string username,
        string reviewer,
        CancellationToken cancellationToken)
    {
        var request = await RequireOpenAsync(project, username, reviewer, cancellationToken);
        var rejected = request with
        {
            Status = AccessRequestStatus.Rejected,
            ReviewedBy = reviewer,
            ReviewedAt = DateTimeOffset.UtcNow,
        };
        await SaveAsync(rejected, cancellationToken);
        return rejected;
    }

    private async Task<AccessRequest> RequireOpenAsync(
        ProjectId project,
        string username,
        string reviewer,
        CancellationToken cancellationToken)
    {
        var request = await GetAsync(project, username, cancellationToken)
            ?? throw new KeyNotFoundException("No such access request.");
        if (!request.IsOpen)
        {
            throw new InvalidOperationException("That request is already closed.");
        }

        if (string.Equals(reviewer, username, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("You cannot review your own access request.");
        }

        return request;
    }

    private async Task<AccessRequest?> GetAsync(
        ProjectId project,
        string username,
        CancellationToken cancellationToken)
    {
        Identifier.ValidateSegment(username, nameof(username));
        var document = await client.GetAsync(
            $"v1/{ControlPlanePaths.AccessRequest(Mount, project.Value, username)}",
            cancellationToken);
        if (document?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("data", out var stored))
        {
            return null;
        }

        var policies = stored.TryGetProperty("policies", out var storedPolicies)
            && storedPolicies.ValueKind == JsonValueKind.Array
            ? storedPolicies.EnumerateArray().Select(entry => entry.GetString()).OfType<string>().ToList()
            : [];

        return new AccessRequest(
            project,
            username,
            policies,
            Text(stored, "reason"),
            Time(stored, "requestedAt") ?? DateTimeOffset.UnixEpoch,
            Enum.TryParse<AccessRequestStatus>(Text(stored, "status"), out var status)
                ? status
                : AccessRequestStatus.Pending,
            Text(stored, "reviewedBy"),
            Time(stored, "reviewedAt"));
    }

    private Task SaveAsync(AccessRequest request, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/{ControlPlanePaths.AccessRequest(Mount, request.Project.Value, request.Username)}",
            new
            {
                data = new
                {
                    policies = request.Policies,
                    reason = request.Reason ?? string.Empty,
                    requestedAt = request.RequestedAt.ToString("O"),
                    status = request.Status.ToString(),
                    reviewedBy = request.ReviewedBy ?? string.Empty,
                    reviewedAt = request.ReviewedAt?.ToString("O") ?? string.Empty,
                },
            },
            cancellationToken);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), out var at) ? at : null;
}
