using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Real roles, replacing a hardcoded list of nine that matched no actual policy.
///
/// <para>
/// Two things are written for each role: the ACL policy OpenBao enforces, and the
/// definition it was generated from. The definition is kept because a policy document
/// cannot be read back into checkboxes reliably — parsing generated HCL to recover
/// intent is guesswork, and getting it wrong would silently widen access.
/// </para>
/// </summary>
public sealed class OpenBaoAccessRoleService(
    OpenBaoAdministrativeClient client,
    IOptions<OpenBaoOptions> options) : IAccessRoleService
{
    private string Definitions => $"v1/{options.Value.MetadataMount}";

    public async Task<IReadOnlyList<AccessRole>> ListAsync(
        ProjectId project,
        CancellationToken cancellationToken)
    {
        var listing = await client.GetAsync(
            $"{Definitions}/metadata/roles/{project}?list=true",
            cancellationToken);
        if (listing?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var roles = new List<AccessRole>();
        foreach (var name in keys.EnumerateArray().Select(key => key.GetString()).OfType<string>())
        {
            var role = await GetAsync(project, name.TrimEnd('/'), cancellationToken);
            if (role is not null)
            {
                roles.Add(role);
            }
        }

        return roles.OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<AccessRole?> GetAsync(
        ProjectId project,
        string name,
        CancellationToken cancellationToken)
    {
        var document = await client.GetAsync(
            $"{Definitions}/data/roles/{project}/{Uri.EscapeDataString(name)}",
            cancellationToken);
        if (document?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("data", out var data))
        {
            return null;
        }

        var environments = new List<EnvironmentId>();
        if (data.TryGetProperty("environments", out var stored) && stored.ValueKind == JsonValueKind.Array)
        {
            environments.AddRange(stored.EnumerateArray()
                .Select(entry => entry.GetString())
                .OfType<string>()
                .Select(entry => EnvironmentId.TryParse(entry, null, out var parsed) ? parsed : (EnvironmentId?)null)
                .OfType<EnvironmentId>());
        }

        bool Flag(string key) =>
            data.TryGetProperty(key, out var value)
            && (value.ValueKind == JsonValueKind.True
                || (value.ValueKind == JsonValueKind.String && value.GetString() == "true"));

        return new AccessRole(
            name,
            project,
            environments,
            new RolePermissions(
                Flag("describe"),
                Flag("readValues"),
                Flag("writeSecrets"),
                Flag("deleteSecrets"),
                Flag("manageDetails"),
                Flag("rollBack"),
                Flag("destroy")),
            data.TryGetProperty("description", out var description) ? description.GetString() : null);
    }

    public async Task<AccessRole> SaveAsync(AccessRole role, CancellationToken cancellationToken)
    {
        Identifier.ValidateSegment(role.Name, nameof(role));
        if (role.Environments.Count == 0)
        {
            throw new ArgumentException("A role needs at least one environment.", nameof(role));
        }

        if (role.Permissions.GrantsNothing)
        {
            throw new ArgumentException("A role that grants nothing is not useful.", nameof(role));
        }

        // Policy first: if writing the definition failed afterwards the role would be
        // enforced but invisible, which is far better than visible but unenforced.
        await client.PutAsync(
            $"v1/sys/policies/acl/{role.PolicyName}",
            new { policy = role.ToPolicy(options.Value.MetadataMount) },
            cancellationToken);

        await client.PostAsync(
            $"{Definitions}/data/roles/{role.Project}/{Uri.EscapeDataString(role.Name)}",
            new
            {
                data = new
                {
                    description = role.Description ?? string.Empty,
                    environments = role.Environments.Select(environment => environment.Value).ToArray(),
                    describe = role.Permissions.Describe,
                    readValues = role.Permissions.ReadValues,
                    writeSecrets = role.Permissions.WriteSecrets,
                    deleteSecrets = role.Permissions.DeleteSecrets,
                    manageDetails = role.Permissions.ManageDetails,
                    rollBack = role.Permissions.RollBack,
                    destroy = role.Permissions.Destroy,
                },
            },
            cancellationToken);

        return role;
    }

    public async Task DeleteAsync(ProjectId project, string name, CancellationToken cancellationToken)
    {
        var role = await GetAsync(project, name, cancellationToken);
        if (role is not null)
        {
            await client.DeleteAsync($"v1/sys/policies/acl/{role.PolicyName}", cancellationToken);
        }

        await client.DeleteAsync(
            $"{Definitions}/metadata/roles/{project}/{Uri.EscapeDataString(name)}",
            cancellationToken);
    }

    /// <summary>
    /// Every policy name that can be assigned: the generated per-environment ones, the
    /// project owner policy, and any custom role. Used to populate role pickers with
    /// things that actually exist.
    /// </summary>
    public async Task<IReadOnlyList<string>> AssignablePolicyNamesAsync(CancellationToken cancellationToken)
    {
        var listing = await client.GetAsync("v1/sys/policies/acl?list=true", cancellationToken);
        if (listing?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return keys.EnumerateArray()
            .Select(key => key.GetString())
            .OfType<string>()
            .Where(name => name is not "root" and not "default")
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
