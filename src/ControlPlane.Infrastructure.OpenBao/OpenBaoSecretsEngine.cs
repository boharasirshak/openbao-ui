using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoSecretsEngine(HttpClient client, IOpenBaoTokenAccessor tokenAccessor) : ISecretsEngine
{
    public async Task<SecretDocument?> ReadAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"v1/{project}/data/{environment}/{path}");
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReadResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned an invalid secret response.");

        var data = payload.Data ?? throw new InvalidOperationException("OpenBao returned no secret data.");
        return new SecretDocument(
            data.Values.ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? string.Empty),
            data.Metadata.Version);
    }

    public async Task WriteAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        SecretDocument document,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        object body = expectedVersion is null
            ? new { data = document.Values }
            : new { data = document.Values, options = new { cas = expectedVersion } };
        using var request = CreateRequest(HttpMethod.Post, $"v1/{project}/data/{environment}/{path}");
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"v1/{project}/data/{environment}/{path}");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"v1/{project}/metadata/{environment}/{path}");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MetadataResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid metadata.");
        return payload.Data?.Versions.Values.Select(version => new SecretVersion(
            version.Version,
            version.DeletionTime,
            version.Destroyed)).ToList() ?? [];
    }

    public async Task RestoreAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        int version,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"v1/{project}/data/{environment}/{path}?version={version}");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReadResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid historical data.");
        var data = payload.Data ?? throw new InvalidOperationException("OpenBao returned no historical data.");
        await WriteAsync(
            project,
            environment,
            path,
            new SecretDocument(data.Values.ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? string.Empty), 0),
            expectedVersion: null,
            cancellationToken);
    }

    public async Task UndeleteAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        int version,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"v1/{project}/undelete/{environment}/{path}");
        request.Content = JsonContent.Create(new { versions = new[] { version } });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Vault-Token", tokenAccessor.GetRequiredToken());
        return request;
    }

    private sealed record ReadResponse([property: JsonPropertyName("data")] SecretPayload? Data);
    private sealed record SecretPayload(
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, JsonElement> Values,
        [property: JsonPropertyName("metadata")] SecretMetadata Metadata);
    private sealed record SecretMetadata([property: JsonPropertyName("version")] int Version);
    private sealed record MetadataResponse([property: JsonPropertyName("data")] MetadataPayload? Data);
    private sealed record MetadataPayload(
        [property: JsonPropertyName("versions")] IReadOnlyDictionary<string, MetadataVersion> Versions);
    private sealed record MetadataVersion(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("deletion_time")] DateTimeOffset? DeletionTime,
        [property: JsonPropertyName("destroyed")] bool Destroyed);
}
