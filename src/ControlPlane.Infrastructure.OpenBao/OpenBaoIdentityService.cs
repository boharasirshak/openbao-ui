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
            members.Add(new Member(username, string.Empty, disabled, policies));
        }

        return members;
    }

    public Task CreateAsync(
        string username,
        string password,
        IReadOnlyList<string> policies,
        CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            new { password, policies },
            cancellationToken);

    public Task DisableAsync(string username, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            new { disabled = true },
            cancellationToken);

    public Task DeleteAsync(string username, CancellationToken cancellationToken) =>
        client.DeleteAsync(
            $"v1/auth/userpass/users/{Uri.EscapeDataString(username)}",
            cancellationToken);
}
