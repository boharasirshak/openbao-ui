using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods.UserPass;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoSessionService(HttpClient client, IOptions<OpenBaoOptions> options) : ISessionService
{
    public async Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authMethod = new UserPassAuthMethodInfo(username, password);
        var vaultClient = new VaultClient(new VaultClientSettings(options.Value.Address.ToString(), authMethod));
        try
        {
            await vaultClient.V1.Auth.Token.LookupSelfAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UnauthorizedAccessException("Invalid username or password.", exception);
        }

        var auth = authMethod.ReturnedLoginAuthInfo
            ?? throw new InvalidOperationException("OpenBao did not return a token.");
        return new OpenBaoSession(
            auth.ClientToken,
            auth.ClientTokenAccessor,
            DateTimeOffset.UtcNow.AddSeconds(auth.LeaseDurationSeconds) - options.Value.SessionSafetyMargin,
            auth.Policies);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/token/revoke-self");
        request.Headers.Add("X-Vault-Token", token);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
