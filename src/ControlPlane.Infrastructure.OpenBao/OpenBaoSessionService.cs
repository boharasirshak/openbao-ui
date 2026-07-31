using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application;
using ControlPlane.Domain;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoSessionService(HttpClient client, IOptions<OpenBaoOptions> options) : ISessionService
{
    public async Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var escapedUsername = Uri.EscapeDataString(username);
        using var response = await client.PostAsJsonAsync($"v1/auth/userpass/login/{escapedUsername}", new { password }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("Invalid username or password.");

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned an invalid login response.");
        var auth = payload.Auth ?? throw new InvalidOperationException("OpenBao did not return a token.");
        return new OpenBaoSession(
            auth.ClientToken,
            auth.Accessor,
            DateTimeOffset.UtcNow.AddSeconds(auth.LeaseDuration) - options.Value.SessionSafetyMargin,
            auth.Policies);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/token/revoke-self");
        request.Headers.Add("X-Vault-Token", token);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record LoginResponse([property: JsonPropertyName("auth")] AuthResponse? Auth);
    private sealed record AuthResponse(
        [property: JsonPropertyName("client_token")] string ClientToken,
        [property: JsonPropertyName("accessor")] string Accessor,
        [property: JsonPropertyName("lease_duration")] int LeaseDuration,
        [property: JsonPropertyName("policies")] IReadOnlyList<string> Policies);
}
