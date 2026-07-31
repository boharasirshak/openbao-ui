using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoProjectService(OpenBaoAdministrativeClient client) : IProjectService
{
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
    {
        var mounts = await client.GetAsync("v1/sys/mounts", cancellationToken);
        if (mounts is null)
        {
            return [];
        }

        return mounts.RootElement.EnumerateObject()
            .Where(property => property.Value.TryGetProperty("type", out var type) && type.GetString() == "kv")
            .Select(property => new Project(
                ProjectId.Parse(property.Name.TrimEnd('/')),
                string.Empty,
                [EnvironmentId.Parse("development"), EnvironmentId.Parse("staging"), EnvironmentId.Parse("production")]))
            .ToList();
    }

    public async Task<Project> CreateAsync(ProjectId project, string description, CancellationToken cancellationToken)
    {
        var existing = await client.GetAsync($"v1/sys/mounts/{project}", cancellationToken);
        if (existing is null)
        {
            await client.PostAsync($"v1/sys/mounts/{project}", new { type = "kv", options = new { version = "2" }, description }, cancellationToken);
        }

        return new Project(project, description, [
            EnvironmentId.Parse("development"),
            EnvironmentId.Parse("staging"),
            EnvironmentId.Parse("production"),
        ]);
    }

    public Task DeleteAsync(ProjectId project, CancellationToken cancellationToken) =>
        client.DeleteAsync($"v1/sys/mounts/{project}", cancellationToken);
}
