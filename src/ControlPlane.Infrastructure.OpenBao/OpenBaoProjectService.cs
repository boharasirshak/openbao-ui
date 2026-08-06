using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoProjectService(
    OpenBaoAdministrativeClient client,
    Microsoft.Extensions.Options.IOptions<OpenBaoOptions> options) : IProjectService
{
    /// <summary>
    /// Every project the caller can see, which for a member is the ones they hold a role
    /// on. This used to read sys/mounts, which only an administrator may list, so a
    /// member got nothing and the dashboard fell back to asking them to type a project
    /// name from memory. sys/internal/ui/mounts answers the same question scoped to the
    /// caller's own token, which is exactly what is wanted here.
    /// </summary>
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
    {
        var mounts = await client.GetAsync("v1/sys/internal/ui/mounts", cancellationToken);
        if (mounts?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("secret", out var mountData))
        {
            return [];
        }
        var projects = new List<Project>();
        foreach (var property in mountData.EnumerateObject().Where(property =>
                     property.Value.TryGetProperty("type", out var type)
                     && type.GetString() == "kv"
                     && property.Name.TrimEnd('/') != options.Value.MetadataMount))
        {
            var project = ProjectId.Parse(property.Name.TrimEnd('/'));
            var description = string.Empty;
            var environments = DefaultEnvironments;
            var metadata = await client.GetAsync(
                $"v1/{options.Value.MetadataMount}/data/projects/{project}",
                cancellationToken);
            if (metadata?.RootElement.TryGetProperty("data", out var metadataEnvelope) == true
                && metadataEnvelope.TryGetProperty("data", out var metadataData))
            {
                if (metadataData.TryGetProperty("description", out var descriptionValue))
                {
                    description = descriptionValue.GetString() ?? string.Empty;
                }

                environments = ReadEnvironments(metadataData) ?? DefaultEnvironments;
            }

            projects.Add(new Project(project, description, environments));
        }

        return projects;
    }

    /// <summary>
    /// Reads the stored environment list. Two shapes are accepted: the original array
    /// of plain names, and the current array of objects. Projects written before
    /// display names and the protected flag existed keep working.
    /// </summary>
    private static IReadOnlyList<ProjectEnvironment>? ReadEnvironments(JsonElement metadataData)
    {
        if (!metadataData.TryGetProperty("environments", out var stored)
            || stored.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var environments = new List<ProjectEnvironment>();
        var index = 0;
        foreach (var entry in stored.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                if (entry.GetString() is { } legacy && EnvironmentId.TryParse(legacy, null, out var id))
                {
                    environments.Add(ProjectEnvironment.Default(id.Value, index++, legacy == "production"));
                }

                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var idValue)
                || idValue.GetString() is not { } name
                || !EnvironmentId.TryParse(name, null, out var parsed))
            {
                continue;
            }

            environments.Add(new ProjectEnvironment(
                parsed,
                entry.TryGetProperty("name", out var display) ? display.GetString() ?? name : name,
                entry.TryGetProperty("protected", out var guarded) && guarded.ValueKind == JsonValueKind.True,
                entry.TryGetProperty("position", out var position) && position.TryGetInt32(out var order)
                    ? order
                    : index++));
        }

        return environments.Count > 0
            ? environments.OrderBy(environment => environment.Position).ToList()
            : null;
    }

    public async Task<Project> CreateAsync(ProjectId project, string description, CancellationToken cancellationToken)
    {
        var mounts = await client.GetAsync("v1/sys/mounts", cancellationToken)
            ?? throw new InvalidOperationException("OpenBao did not return its mount list.");
        var mountName = $"{project.Value}/";
        var mountData = mounts.RootElement.TryGetProperty("data", out var data)
            ? data
            : mounts.RootElement;
        if (!mountData.TryGetProperty(mountName, out _))
        {
            await client.PostAsync($"v1/sys/mounts/{project}", new { type = "kv", options = new { version = "2" }, description }, cancellationToken);
        }

        if (!mountData.TryGetProperty($"{options.Value.MetadataMount}/", out _))
        {
            await client.PostAsync(
                $"v1/sys/mounts/{options.Value.MetadataMount}",
                new { type = "kv", options = new { version = "2" } },
                cancellationToken);
        }

        // Creating an existing project keeps whatever environments it already has;
        // this call doubles as the policy reconcile, so it must not reset them.
        var existing = await GetAsync(project, cancellationToken);
        var environments = existing?.Environments ?? DefaultEnvironments;

        await WriteProjectAsync(project, description, environments, cancellationToken);

        foreach (var environment in environments)
        {
            await CreateEnvironmentPoliciesAsync(project, environment.Id, cancellationToken);
        }

        await CreateProjectAdminPolicyAsync(project, cancellationToken);

        return new Project(project, description, environments);
    }

    public async Task<Project?> GetAsync(ProjectId project, CancellationToken cancellationToken)
    {
        var metadata = await client.GetAsync(
            $"v1/{options.Value.MetadataMount}/data/projects/{project}",
            cancellationToken);
        if (metadata?.RootElement.TryGetProperty("data", out var envelope) != true
            || !envelope.TryGetProperty("data", out var data))
        {
            return null;
        }

        var description = data.TryGetProperty("description", out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
        return new Project(project, description, ReadEnvironments(data) ?? DefaultEnvironments);
    }

    public async Task<Project> AddEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        string displayName,
        bool isProtected,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(project, cancellationToken);
        if (current.Environments.Any(existing => existing.Id == environment))
        {
            throw new ArgumentException($"\"{environment}\" already exists in this project.", nameof(environment));
        }

        var position = current.Environments.Count == 0
            ? 0
            : current.Environments.Max(existing => existing.Position) + 1;
        var updated = current.Environments
            .Append(new ProjectEnvironment(environment, Display(displayName, environment), isProtected, position))
            .ToList();

        await CreateEnvironmentPoliciesAsync(project, environment, cancellationToken);
        await WriteProjectAsync(project, current.Description, updated, cancellationToken);
        return current with { Environments = updated };
    }

    public async Task<Project> UpdateEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        string? displayName,
        bool? isProtected,
        int? position,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(project, cancellationToken);
        var target = current.Environments.FirstOrDefault(existing => existing.Id == environment)
            ?? throw new ArgumentException($"\"{environment}\" is not an environment of this project.", nameof(environment));

        var updated = current.Environments
            .Select(existing => existing.Id == environment
                ? existing with
                {
                    DisplayName = displayName is null ? target.DisplayName : Display(displayName, environment),
                    Protected = isProtected ?? target.Protected,
                    Position = position ?? target.Position,
                }
                : existing)
            .OrderBy(existing => existing.Position)
            .ToList();

        await WriteProjectAsync(project, current.Description, updated, cancellationToken);
        return current with { Environments = updated };
    }

    public async Task<Project> RemoveEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        bool purgeSecrets,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(project, cancellationToken);
        if (current.Environments.All(existing => existing.Id != environment))
        {
            throw new ArgumentException($"\"{environment}\" is not an environment of this project.", nameof(environment));
        }

        if (current.Environments.Count == 1)
        {
            throw new ArgumentException("A project needs at least one environment.", nameof(environment));
        }

        var paths = await ScanEnvironmentAsync(project, environment, cancellationToken);
        if (paths.Count > 0 && !purgeSecrets)
        {
            throw new ArgumentException(
                $"\"{environment}\" still holds {paths.Count} secret(s). Remove them first, or confirm that they should be destroyed.",
                nameof(environment));
        }

        foreach (var path in paths)
        {
            await client.DeleteAsync(
                $"v1/{SecretLocation.MetadataPrefix(project.Value, environment.Value)}/{path}",
                cancellationToken);
        }

        await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-viewer", cancellationToken);
        await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-editor", cancellationToken);

        var updated = current.Environments.Where(existing => existing.Id != environment).ToList();
        await WriteProjectAsync(project, current.Description, updated, cancellationToken);
        return current with { Environments = updated };
    }

    private async Task<IReadOnlyList<string>> ScanEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        CancellationToken cancellationToken)
    {
        var document = await client.GetAsync(
            $"v1/{SecretLocation.MetadataPrefix(project.Value, environment.Value)}?scan=true",
            cancellationToken);
        if (document?.RootElement.TryGetProperty("data", out var data) != true
            || !data.TryGetProperty("keys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return keys.EnumerateArray()
            .Select(key => key.GetString())
            .OfType<string>()
            .Where(key => !key.EndsWith('/'))
            .ToList();
    }

    private async Task<Project> RequireAsync(ProjectId project, CancellationToken cancellationToken) =>
        await GetAsync(project, cancellationToken)
        ?? throw new ArgumentException($"\"{project}\" is not a known project.", nameof(project));

    private static string Display(string displayName, EnvironmentId environment) =>
        string.IsNullOrWhiteSpace(displayName) ? environment.Value : displayName.Trim();

    private Task WriteProjectAsync(
        ProjectId project,
        string description,
        IReadOnlyList<ProjectEnvironment> environments,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/{options.Value.MetadataMount}/data/projects/{project}",
            new
            {
                data = new
                {
                    description,
                    environments = environments
                        .OrderBy(environment => environment.Position)
                        .Select(environment => new
                        {
                            id = environment.Id.Value,
                            name = environment.DisplayName,
                            @protected = environment.Protected,
                            position = environment.Position,
                        })
                        .ToArray(),
                },
            },
            cancellationToken);

    private async Task CreateEnvironmentPoliciesAsync(
        ProjectId project,
        EnvironmentId environment,
        CancellationToken cancellationToken)
    {
        await CreatePolicyAsync(project, environment, readOnly: true, cancellationToken);
        await CreatePolicyAsync(project, environment, readOnly: false, cancellationToken);
    }

    public async Task DeleteAsync(ProjectId project, CancellationToken cancellationToken)
    {
        // Read the environments before the metadata goes, or custom ones leak policies.
        var existing = await GetAsync(project, cancellationToken);
        await client.DeleteAsync($"v1/sys/mounts/{project}", cancellationToken);
        await client.DeleteAsync($"v1/{options.Value.MetadataMount}/metadata/projects/{project}", cancellationToken);
        foreach (var environment in (existing?.Environments ?? DefaultEnvironments).Select(entry => entry.Id))
        {
            await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-viewer", cancellationToken);
            await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-editor", cancellationToken);
        }

        await client.DeleteAsync($"v1/sys/policies/acl/{project}-admin", cancellationToken);
    }

    // Capability notes, because these are easy to get subtly wrong:
    //  - "patch" is required on metadata. Annotations are merge-patched so that saving
    //    one of them does not wipe the rest, and "update" does not cover PATCH.
    //  - "scan" is a capability of its own in OpenBao and gates recursive listing.
    //  - only an admin gets "delete" on metadata; that destroys every version at once.
    private const string ReadData = "[\"read\", \"list\"]";
    private const string WriteData = "[\"create\", \"read\", \"update\", \"patch\", \"delete\", \"list\"]";
    private const string ReadMetadata = "[\"read\", \"list\", \"scan\"]";
    private const string WriteMetadata = "[\"create\", \"read\", \"update\", \"patch\", \"list\", \"scan\"]";
    private const string OwnMetadata = "[\"create\", \"read\", \"update\", \"patch\", \"delete\", \"list\", \"scan\"]";

    private Task CreatePolicyAsync(
        ProjectId project,
        EnvironmentId environment,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        var data = SecretLocation.DataPrefix(project.Value, environment.Value);
        var metadata = SecretLocation.MetadataPrefix(project.Value, environment.Value);
        var policy = $"path \"{data}/*\" {{ capabilities = {(readOnly ? ReadData : WriteData)} }}\n"
            + $"path \"{metadata}/*\" {{ capabilities = {(readOnly ? ReadMetadata : WriteMetadata)} }}";

        // An editor may propose a change to a protected environment and review someone
        // else's. The proposed values sit in this project's mount under _pending, so the
        // same grant covers them — see ChangeRequest.
        if (!readOnly)
        {
            var pending = $"{ChangeRequest.PendingEnvironment}/{environment}";
            policy += $"\npath \"{SecretLocation.DataPrefix(project.Value, pending)}/*\" {{ capabilities = {WriteData} }}"
                + $"\npath \"{SecretLocation.MetadataPrefix(project.Value, pending)}/*\" {{ capabilities = {OwnMetadata} }}";
        }

        policy += ControlPlaneAccess(project, canWriteChanges: !readOnly);

        return client.PutAsync(
            $"v1/sys/policies/acl/{ProjectPolicy.Environment(project, environment, readOnly)}",
            new { policy },
            cancellationToken);
    }

    private Task CreateProjectAdminPolicyAsync(
        ProjectId project,
        CancellationToken cancellationToken)
    {
        var policy = $"path \"{SecretLocation.DataPrefix(project.Value, "*")}\" {{ capabilities = {WriteData} }}\n"
            + $"path \"{SecretLocation.MetadataPrefix(project.Value, "*")}\" {{ capabilities = {OwnMetadata} }}"
            + ControlPlaneAccess(project, canWriteChanges: true);
        return client.PutAsync(
            $"v1/sys/policies/acl/{ProjectPolicy.Admin(project)}",
            new { policy },
            cancellationToken);
    }

    /// <summary>
    /// The control plane's own records for this project. Everyone who can use the project
    /// needs to read its record — that is where "this environment is protected" lives, and
    /// the API checks it with the caller's own token before every write. Anyone who can
    /// write secrets also needs to write change requests.
    /// </summary>
    private string ControlPlaneAccess(ProjectId project, bool canWriteChanges)
    {
        var mount = options.Value.MetadataMount;
        var changes = canWriteChanges ? WriteData : ReadData;
        // Activity is append-only on purpose: "create" without "update" means an entry
        // cannot be rewritten after the fact, which is what makes the feed worth reading.
        var activity = canWriteChanges ? "[\"create\", \"read\", \"list\"]" : "[\"read\", \"list\"]";
        return $"\npath \"{ControlPlanePaths.ProjectRecord(mount, project.Value)}\" {{ capabilities = {ReadData} }}"
            + $"\npath \"{ControlPlanePaths.ProjectRecordMetadata(mount, project.Value)}\" {{ capabilities = {ReadMetadata} }}"
            + $"\npath \"{ControlPlanePaths.Changes(mount, project.Value)}/*\" {{ capabilities = {changes} }}"
            + $"\npath \"{ControlPlanePaths.ChangesMetadata(mount, project.Value)}\" {{ capabilities = {ReadMetadata} }}"
            + $"\npath \"{ControlPlanePaths.ChangesMetadata(mount, project.Value)}/*\" {{ capabilities = {ReadMetadata} }}"
            + $"\npath \"{ControlPlanePaths.Activity(mount, project.Value)}/*\" {{ capabilities = {activity} }}"
            + $"\npath \"{ControlPlanePaths.ActivityMetadata(mount, project.Value)}/*\" {{ capabilities = {ReadMetadata} }}";
    }

    private static IReadOnlyList<ProjectEnvironment> DefaultEnvironments => [
        ProjectEnvironment.Default("development", 0),
        ProjectEnvironment.Default("staging", 1),
        // Production starts protected: the safe default is the one you have to relax.
        ProjectEnvironment.Default("production", 2, isProtected: true),
    ];
}
