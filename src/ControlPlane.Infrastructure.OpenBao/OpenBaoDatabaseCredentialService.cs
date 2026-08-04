using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoDatabaseCredentialService(
    HttpClient client,
    IOpenBaoTokenAccessor tokenAccessor) : IDatabaseCredentialService
{
    public async Task<DynamicDatabaseCredential> ReadAsync(
        string role,
        CancellationToken cancellationToken)
    {
        // Shares the one segment rule, which also rejects "." and ".." — the previous
        // inline copy allowed both.
        if (string.IsNullOrWhiteSpace(role)
            || role.Split('/').Any(segment => !Identifier.IsValidSegment(segment)))
        {
            throw new ArgumentException("Database role is invalid.", nameof(role));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/database/creds/{role}");
        request.Headers.Add("X-Vault-Token", tokenAccessor.GetRequiredToken());
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CredentialResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid database credentials.");
        return new DynamicDatabaseCredential(
            payload.Data.Username,
            payload.Data.Password,
            payload.LeaseId,
            DateTimeOffset.UtcNow.AddSeconds(payload.LeaseDuration));
    }

    private sealed record CredentialResponse(
        [property: JsonPropertyName("lease_id")] string LeaseId,
        [property: JsonPropertyName("lease_duration")] int LeaseDuration,
        [property: JsonPropertyName("data")] CredentialData Data);

    private sealed record CredentialData(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password);
}
