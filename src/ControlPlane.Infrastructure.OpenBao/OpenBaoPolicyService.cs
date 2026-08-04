using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoPolicyService(OpenBaoAdministrativeClient client) : IPolicyService
{
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
