using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Share links are OpenBao response-wrapping tokens. The payload lives in OpenBao's
/// cubbyhole, the token is single-use and expires on its own, and this application
/// stores nothing — so there is no share database to leak and no expiry job to run.
/// </summary>
public sealed class OpenBaoSecretShareService(HttpClient client, IOpenBaoTokenAccessor tokenAccessor)
    : ISecretShareService
{
    public static readonly TimeSpan MinimumTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumTtl = TimeSpan.FromDays(7);

    public async Task<(string Token, DateTimeOffset ExpiresAt)> WrapAsync(
        IReadOnlyDictionary<string, string> values,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("There is nothing to share.", nameof(values));
        }

        if (ttl < MinimumTtl || ttl > MaximumTtl)
        {
            throw new ArgumentException(
                $"A share link must last between {MinimumTtl.TotalMinutes:0} minutes and {MaximumTtl.TotalDays:0} days.",
                nameof(ttl));
        }

        using var request = CreateRequest(HttpMethod.Post, "v1/sys/wrapping/wrap");
        // The TTL is a header, not part of the body: everything in the body is payload.
        request.Headers.Add("X-Vault-Wrap-TTL", $"{(int)ttl.TotalSeconds}s");
        request.Content = JsonContent.Create(values);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<WrapResponse>(cancellationToken: cancellationToken);
        var info = payload?.WrapInfo
            ?? throw new InvalidOperationException("OpenBao did not return a wrapping token.");

        return (info.Token, DateTimeOffset.UtcNow.AddSeconds(info.Ttl));
    }

    public async Task<IReadOnlyDictionary<string, string>?> UnwrapAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // Unwrapping authenticates with the wrapping token itself, so this is the one
        // call that must not carry the caller's session token — the share page is public.
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/sys/wrapping/unwrap");
        request.Headers.Add("X-Vault-Token", token);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // A used, expired or invented token all look the same from here, and should:
            // telling them apart would let someone probe for live tokens.
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<UnwrapResponse>(cancellationToken: cancellationToken);
        if (payload?.Data is null)
        {
            return null;
        }

        return payload.Data.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ValueKind == JsonValueKind.String
                ? pair.Value.GetString() ?? string.Empty
                : pair.Value.ToString());
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Vault-Token", tokenAccessor.GetRequiredToken());
        return request;
    }

    private sealed record WrapResponse([property: JsonPropertyName("wrap_info")] WrapInfo? WrapInfo);

    private sealed record WrapInfo(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("ttl")] int Ttl);

    private sealed record UnwrapResponse(
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, JsonElement>? Data);
}
