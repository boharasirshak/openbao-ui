using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoMachineIdentityService(
    OpenBaoAdministrativeClient client,
    IPolicyService policyService) : IMachineIdentityService
{
    public async Task<IReadOnlyList<MachineIdentity>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await client.GetAsync("v1/auth/approle/role?list=true", cancellationToken);
        var data = roles?.RootElement.TryGetProperty("data", out var roleData) == true ? roleData : default;
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object
            || !data.TryGetProperty("keys", out var keys))
        {
            return [];
        }

        var identities = new List<MachineIdentity>();
        foreach (var key in keys.EnumerateArray().Select(value => value.GetString()!))
        {
            var config = await client.GetAsync($"v1/auth/approle/role/{Uri.EscapeDataString(key)}", cancellationToken);
            var role = config?.RootElement.GetProperty("data");
            var roleId = await client.GetAsync($"v1/auth/approle/role/{Uri.EscapeDataString(key)}/role-id", cancellationToken);
            var id = roleId?.RootElement.GetProperty("data").GetProperty("role_id").GetString() ?? key;
            identities.Add(new MachineIdentity(
                key,
                id,
                string.Empty,
                string.Empty,
                true,
                role?.GetProperty("token_ttl").GetInt32(),
                role?.GetProperty("secret_id_num_uses").GetInt32()));
        }

        return identities;
    }

    public async Task<MachineIdentity> CreateAsync(MachineIdentity identity, CancellationToken cancellationToken)
    {
        var policyName = $"{identity.Name}-runtime";
        await policyService.CreateRoleAsync(
            new Role(policyName, identity.Project, identity.Environment, identity.ReadOnly),
            cancellationToken);
        var authMethods = await client.GetAsync("v1/sys/auth", cancellationToken);
        var authData = authMethods?.RootElement.TryGetProperty("data", out var data) == true
            ? data
            : authMethods?.RootElement;
        if (authData?.TryGetProperty("approle/", out _) != true)
        {
            await client.PostAsync("v1/sys/auth/approle", new { type = "approle" }, cancellationToken);
        }

        await client.PostAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(identity.Name)}",
            new
            {
                token_policies = new[] { policyName },
                token_ttl = identity.TokenTtlSeconds,
                secret_id_num_uses = identity.TokenUses,
            },
            cancellationToken);
        var role = await client.GetAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(identity.Name)}/role-id",
            cancellationToken);
        var resolvedRoleId = role?.RootElement.GetProperty("data").GetProperty("role_id").GetString()
            ?? identity.RoleId;
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

    public async Task RevokeSecretIdsAsync(string roleId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(
            $"v1/auth/approle/role/{Uri.EscapeDataString(roleId)}/secret-id?list=true",
            cancellationToken);
        if (response?.RootElement.GetProperty("data").TryGetProperty("keys", out var keys) != true)
        {
            return;
        }

        foreach (var accessor in keys.EnumerateArray().Select(value => value.GetString()!))
        {
            await client.PostAsync(
                $"v1/auth/approle/role/{Uri.EscapeDataString(roleId)}/secret-id-accessor/destroy",
                new { secret_id_accessor = accessor },
                cancellationToken);
        }
    }
}
