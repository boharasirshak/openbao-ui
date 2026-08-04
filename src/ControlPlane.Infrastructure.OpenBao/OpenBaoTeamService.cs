using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Teams are OpenBao identity groups. Putting membership there rather than in this
/// application means a member picks up the team's roles on their next login and the API
/// never has to expand team membership when deciding what someone can read.
/// </summary>
public sealed class OpenBaoTeamService(OpenBaoAdministrativeClient client) : ITeamService
{
    public async Task<IReadOnlyList<Team>> ListAsync(CancellationToken cancellationToken)
    {
        var listing = await client.GetAsync("v1/identity/group/name?list=true", cancellationToken);
        if (listing?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var teams = new List<Team>();
        foreach (var name in keys.EnumerateArray().Select(key => key.GetString()).OfType<string>())
        {
            var team = await GetAsync(name, cancellationToken);
            if (team is not null)
            {
                teams.Add(team);
            }
        }

        return teams.OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Team?> GetAsync(string name, CancellationToken cancellationToken)
    {
        var document = await client.GetAsync(
            $"v1/identity/group/name/{Uri.EscapeDataString(name)}",
            cancellationToken);
        if (document?.RootElement.TryGetProperty("data", out var data) != true)
        {
            return null;
        }

        return new Team(
            data.TryGetProperty("name", out var stored) ? stored.GetString() ?? name : name,
            data.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Strings(data, "policies"),
            Strings(data, "member_entity_ids"));
    }

    public async Task<Team> CreateAsync(
        string name,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        var teamName = Identifier.ValidateSegment(name, nameof(name));
        if (await GetAsync(teamName, cancellationToken) is not null)
        {
            throw new ArgumentException($"A team called \"{teamName}\" already exists.", nameof(name));
        }

        await client.PostAsync(
            "v1/identity/group",
            new { name = teamName, type = "internal", policies = roles },
            cancellationToken);

        return await GetAsync(teamName, cancellationToken)
            ?? throw new InvalidOperationException("OpenBao did not return the new team.");
    }

    public async Task<Team> SetRolesAsync(
        string name,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(name, cancellationToken);
        await client.PostAsync(
            $"v1/identity/group/name/{Uri.EscapeDataString(current.Name)}",
            new { policies = roles, member_entity_ids = current.MemberEntityIds },
            cancellationToken);
        return current with { Roles = roles };
    }

    public async Task<Team> SetMembersAsync(
        string name,
        IReadOnlyList<string> memberEntityIds,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(name, cancellationToken);
        // The whole membership list is sent every time: OpenBao replaces it, so a
        // partial write would silently remove everyone omitted.
        await client.PostAsync(
            $"v1/identity/group/name/{Uri.EscapeDataString(current.Name)}",
            new { policies = current.Roles, member_entity_ids = memberEntityIds },
            cancellationToken);
        return current with { MemberEntityIds = memberEntityIds };
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken) =>
        client.DeleteAsync($"v1/identity/group/name/{Uri.EscapeDataString(name)}", cancellationToken);

    private async Task<Team> RequireAsync(string name, CancellationToken cancellationToken) =>
        await GetAsync(name, cancellationToken)
        ?? throw new ArgumentException($"There is no team called \"{name}\".", nameof(name));

    private static IReadOnlyList<string> Strings(JsonElement data, string property) =>
        data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(entry => entry.GetString()).OfType<string>().ToList()
            : [];
}
