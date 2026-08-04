using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoSessionService(HttpClient client, IOptions<OpenBaoOptions> options) : ISessionService
{
    public async Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        // Userpass login is a single unauthenticated POST. Doing it here rather than
        // through an SDK keeps it on the same resilience-handled HttpClient as every
        // other call, and the CLI already performs the identical request.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/auth/userpass/login/{Uri.EscapeDataString(username)}")
        {
            Content = JsonContent.Create(new { password }),
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        var auth = payload?.Auth ?? throw new InvalidOperationException("OpenBao did not return a token.");

        return new OpenBaoSession(
            auth.ClientToken,
            auth.Accessor,
            DateTimeOffset.UtcNow.AddSeconds(auth.LeaseDurationSeconds) - options.Value.SessionSafetyMargin,
            auth.Policies ?? [],
            username);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/token/revoke-self");
        request.Headers.Add("X-Vault-Token", token);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record LoginResponse([property: JsonPropertyName("auth")] LoginAuth? Auth);

    private sealed record LoginAuth(
        [property: JsonPropertyName("client_token")] string ClientToken,
        [property: JsonPropertyName("accessor")] string Accessor,
        [property: JsonPropertyName("lease_duration")] int LeaseDurationSeconds,
        [property: JsonPropertyName("policies")] IReadOnlyList<string>? Policies);
}
