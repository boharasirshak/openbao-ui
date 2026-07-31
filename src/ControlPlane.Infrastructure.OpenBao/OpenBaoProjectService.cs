using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoProjectService(
    OpenBaoAdministrativeClient client,
    Microsoft.Extensions.Options.IOptions<OpenBaoOptions> options) : IProjectService
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
    {
        var mounts = await client.GetAsync("v1/sys/mounts", cancellationToken);
        if (mounts is null)
        {
            return [];
        }

        var mountData = mounts.RootElement.TryGetProperty("data", out var data)
            ? data
            : mounts.RootElement;
        var projects = new List<Project>();
        foreach (var property in mountData.EnumerateObject().Where(property =>
                     property.Value.TryGetProperty("type", out var type)
                     && type.GetString() == "kv"
                     && property.Name.TrimEnd('/') != options.Value.MetadataMount))
        {
            var project = ProjectId.Parse(property.Name.TrimEnd('/'));
            var description = string.Empty;
            var metadata = await client.GetAsync(
                $"v1/{options.Value.MetadataMount}/data/projects/{project}",
                cancellationToken);
            if (metadata?.RootElement.TryGetProperty("data", out var metadataEnvelope) == true
                && metadataEnvelope.TryGetProperty("data", out var metadataData)
                && metadataData.TryGetProperty("description", out var descriptionValue))
            {
                description = descriptionValue.GetString() ?? string.Empty;
            }

            projects.Add(new Project(project, description, DefaultEnvironments));
        }

        return projects;
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

        await client.PostAsync(
            $"v1/{options.Value.MetadataMount}/data/projects/{project}",
            new { data = new { description, environments = DefaultEnvironments.Select(environment => environment.Value).ToArray() } },
            cancellationToken);

        foreach (var environment in DefaultEnvironments)
        {
            await CreatePolicyAsync(project, environment, readOnly: true, cancellationToken);
            await CreatePolicyAsync(project, environment, readOnly: false, cancellationToken);
        }

        return new Project(project, description, DefaultEnvironments);
    }

    public async Task DeleteAsync(ProjectId project, CancellationToken cancellationToken)
    {
        await client.DeleteAsync($"v1/sys/mounts/{project}", cancellationToken);
        await client.DeleteAsync($"v1/{options.Value.MetadataMount}/metadata/projects/{project}", cancellationToken);
        foreach (var environment in DefaultEnvironments)
        {
            await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-viewer", cancellationToken);
            await client.DeleteAsync($"v1/sys/policies/acl/{project}-{environment}-editor", cancellationToken);
        }
    }

    private Task CreatePolicyAsync(
        ProjectId project,
        EnvironmentId environment,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        var suffix = readOnly ? "viewer" : "editor";
        var capabilities = readOnly ? "[\"read\", \"list\"]" : "[\"create\", \"read\", \"update\", \"patch\", \"delete\", \"list\"]";
        var policy = $"path \"{project}/data/{environment}/*\" {{ capabilities = {capabilities} }}\n"
            + $"path \"{project}/metadata/{environment}/*\" {{ capabilities = [\"read\", \"list\"] }}";
        return client.PutAsync(
            $"v1/sys/policies/acl/{project}-{environment}-{suffix}",
            new { policy },
            cancellationToken);
    }

    private static IReadOnlyList<EnvironmentId> DefaultEnvironments => [
        EnvironmentId.Parse("development"),
        EnvironmentId.Parse("staging"),
        EnvironmentId.Parse("production"),
    ];
}
