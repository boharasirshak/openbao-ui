using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoPolicyService(OpenBaoAdministrativeClient client) : IPolicyService
{
    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([
            new("Development Viewer", "*", "development", true),
            new("Development Editor", "*", "development", false),
            new("Staging Viewer", "*", "staging", true),
            new("Staging Editor", "*", "staging", false),
            new("Production Viewer", "*", "production", true),
            new("Production Editor", "*", "production", false),
            new("Project Admin", "*", "*", false),
            new("Organization Admin", "*", "*", false),
            new("Machine Runtime", "*", "*", true),
        ]);

    public Task CreateRoleAsync(Role role, CancellationToken cancellationToken)
    {
        var project = role.Project == "*" ? "*" : ProjectId.Parse(role.Project).Value;
        var environment = role.Environment == "*" ? "*" : EnvironmentId.Parse(role.Environment).Value;
        var capabilities = role.ReadOnly ? "[\"read\", \"list\"]" : "[\"create\", \"read\", \"update\", \"patch\", \"delete\", \"list\"]";
        var policy = $"path \"{project}/data/{environment}/*\" {{ capabilities = {capabilities} }}\n"
            + $"path \"{project}/metadata/{environment}/*\" {{ capabilities = [\"read\", \"list\"] }}";
        return client.PutAsync(
            $"v1/sys/policies/acl/{Uri.EscapeDataString(role.Name)}",
            new { policy },
            cancellationToken);
    }

    public Task DeleteRoleAsync(string roleName, CancellationToken cancellationToken) =>
        client.DeleteAsync($"v1/sys/policies/acl/{Uri.EscapeDataString(roleName)}", cancellationToken);
}
