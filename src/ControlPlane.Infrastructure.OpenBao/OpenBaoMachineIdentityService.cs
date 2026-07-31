using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoMachineIdentityService(OpenBaoAdministrativeClient client) : IMachineIdentityService
{
    public Task<IReadOnlyList<MachineIdentity>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineIdentity>>([]);

    public async Task<MachineIdentity> CreateAsync(MachineIdentity identity, CancellationToken cancellationToken)
    {
        var roleId = identity.RoleId;
        await client.PostAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(identity.Name)}",
            new
            {
                token_policies = new[] { roleId },
                token_ttl = identity.TokenTtlSeconds,
                secret_id_num_uses = identity.TokenUses,
            },
            cancellationToken);
        var role = await client.GetAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(identity.Name)}/role-id",
            cancellationToken);
        var resolvedRoleId = role?.RootElement.GetProperty("data").GetProperty("role_id").GetString() ?? roleId;
        return identity with { RoleId = resolvedRoleId };
    }

    public async Task<string> GenerateSecretIdAsync(string roleId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsyncValue(
            $"v1/auth/approle/role/{Uri.EscapeDataString(roleId)}/secret-id",
            new { },
            cancellationToken);
        return response.RootElement.GetProperty("data").GetProperty("secret_id").GetString()!;
    }

    public Task RevokeSecretIdsAsync(string roleId, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(roleId)}/secret-id/destroy",
            new { secret_id = roleId },
            cancellationToken);
}
