using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoIdentityService(OpenBaoAdministrativeClient client) : IIdentityService
{
    public async Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await client.GetAsync("v1/auth/userpass/users?list=true", cancellationToken);
        if (users is null || !users.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("keys", out var keys))
        {
            return [];
        }

        var members = new List<Member>();
        foreach (var username in keys.EnumerateArray().Select(value => value.GetString()!))
        {
            var user = await client.GetAsync($"v1/auth/userpass/users/{Uri.EscapeDataString(username)}", cancellationToken);
            var userData = user?.RootElement.GetProperty("data");
            var policies = userData?.TryGetProperty("policies", out var policyValues) == true
                ? policyValues.EnumerateArray().Select(value => value.GetString()!).ToList()
                : [];
            var disabled = userData?.TryGetProperty("disabled", out var disabledValue) == true
                && disabledValue.GetBoolean();
            var entity = await FindEntityAsync(username, cancellationToken);
            members.Add(new Member(username, entity?.Id ?? string.Empty, disabled, policies));
        }

        return members;
    }

    public async Task CreateAsync(
        string username,
        string password,
        IReadOnlyList<string> policies,
        CancellationToken cancellationToken)
    {
        await client.PostAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            new { password, policies },
            cancellationToken);
        await EnsureEntityAsync(username, policies, cancellationToken);
    }

    public Task ResetPasswordAsync(string username, string password, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            new { password },
            cancellationToken);

    public async Task SetPoliciesAsync(string username, IReadOnlyList<string> policies, CancellationToken cancellationToken)
    {
        await client.PostAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            new { policies },
            cancellationToken);
        await EnsureEntityAsync(username, policies, cancellationToken);
    }

    public async Task DisableAsync(string username, CancellationToken cancellationToken)
    {
        var entity = await FindEntityAsync(username, cancellationToken);
        if (entity is not null)
        {
            await client.PostAsync($"v1/identity/entity/id/{entity.Id}", new { disabled = true }, cancellationToken);
        }

        await DeleteAsync(username, cancellationToken);
    }

    public async Task DeleteAsync(string username, CancellationToken cancellationToken)
    {
        await client.DeleteAsync($"v1/auth/userpass/users/{Uri.EscapeDataString(username)}", cancellationToken);
        var entity = await FindEntityAsync(username, cancellationToken);
        if (entity is not null)
        {
            await client.DeleteAsync($"v1/identity/entity/id/{entity.Id}", cancellationToken);
        }
    }

    private async Task EnsureEntityAsync(string username, IReadOnlyList<string> policies, CancellationToken cancellationToken)
    {
        var entity = await FindEntityAsync(username, cancellationToken);
        var needsAlias = false;
        if (entity is null)
        {
            var created = await client.PostAsyncValue(
                "v1/identity/entity",
                new { name = username, policies },
                cancellationToken);
            entity = new EntityReference(created.RootElement.GetProperty("data").GetProperty("id").GetString()!, username);
            needsAlias = true;
        }

        if (needsAlias)
        {
            var auth = await client.GetAsync("v1/sys/auth", cancellationToken);
            var authData = auth?.RootElement.TryGetProperty("data", out var data) == true ? data : auth?.RootElement;
            var accessor = authData?.GetProperty("userpass/").GetProperty("accessor").GetString();
            if (accessor is not null)
            {
                await client.PostAsync(
                    "v1/identity/entity-alias",
                    new { name = username, canonical_id = entity.Id, mount_accessor = accessor },
                    cancellationToken);
            }
        }
    }

    private async Task<EntityReference?> FindEntityAsync(string username, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(
            $"v1/identity/entity/name/{Uri.EscapeDataString(username)}",
            cancellationToken);
        if (response is null)
        {
            return null;
        }

        var data = response.RootElement.GetProperty("data");
        return new EntityReference(data.GetProperty("id").GetString()!, username);
    }

    private sealed record EntityReference(string Id, string Name);
}
