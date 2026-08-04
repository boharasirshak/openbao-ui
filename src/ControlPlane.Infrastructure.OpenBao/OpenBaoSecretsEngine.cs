using System.Net;
using System.Net.Http.Headers;
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
        using var request = CreateRequest(
            HttpMethod.Get,
            new SecretLocation(project, environment, path).Data);
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
            data.Metadata.Version,
            data.Metadata.CustomMetadata?.GetValueOrDefault("description"));
    }

    public async Task WriteAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        SecretDocument document,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var at = new SecretLocation(project, environment, path);
        object body = expectedVersion is null
            ? new { data = document.Values }
            : new { data = document.Values, options = new { cas = expectedVersion } };
        using var request = CreateRequest(HttpMethod.Post, at.Data);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (document.Description is not null)
        {
            await MergeCustomMetadataAsync(
                at,
                new Dictionary<string, string> { ["description"] = document.Description },
                cancellationToken);
        }
    }

    /// <summary>
    /// A POST to the metadata path replaces custom_metadata wholesale, which would wipe
    /// every other annotation on the secret. Merge-patch updates only the keys supplied.
    /// </summary>
    private async Task MergeCustomMetadataAsync(
        SecretLocation at,
        IReadOnlyDictionary<string, string> customMetadata,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Patch, at.Metadata);
        request.Content = JsonContent.Create(new { custom_metadata = customMetadata });
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/merge-patch+json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            new SecretLocation(project, environment, path).Data);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SecretEntry>> ListAsync(
        ProjectId project,
        EnvironmentId environment,
        string? folder,
        CancellationToken cancellationToken)
    {
        var at = new FolderLocation(
            project,
            environment,
            string.IsNullOrWhiteSpace(folder) ? null : SecretPath.Parse(folder));
        using var request = CreateRequest(HttpMethod.Get, $"{at.Metadata}?list=true");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ListResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid list data.");
        return payload.Data?.Keys?.Select(key => new SecretEntry(key.TrimEnd('/'), key.EndsWith('/'))).ToList() ?? [];
    }

    public async Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            new SecretLocation(project, environment, path).Metadata);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MetadataResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid metadata.");
        return payload.Data is null ? [] : ToVersions(payload.Data);
    }

    /// <summary>
    /// OpenBao keys the versions map by version number; the entries themselves carry
    /// no version field, so the key is the only place it exists.
    /// </summary>
    private static List<SecretVersion> ToVersions(MetadataPayload data) =>
        data.Versions
            .Where(entry => int.TryParse(entry.Key, out _))
            .Select(entry => new SecretVersion(
                int.Parse(entry.Key),
                DateTimeOffset.TryParse(entry.Value.DeletionTime, out var deletedAt) ? deletedAt : null,
                entry.Value.Destroyed))
            .OrderBy(version => version.Version)
            .ToList();

    public async Task RestoreAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        int version,
        CancellationToken cancellationToken)
    {
        var at = new SecretLocation(project, environment, path);
        using var request = CreateRequest(HttpMethod.Get, $"{at.Data}?version={version}");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReadResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenBao returned invalid historical data.");
        var data = payload.Data ?? throw new InvalidOperationException("OpenBao returned no historical data.");

        // custom_metadata lives on the secret, not on a version, so a rollback restores
        // values only. Passing no description leaves every annotation as it is; the old
        // code rewrote it from the historical read and dropped everything else.
        await WriteAsync(
            project,
            environment,
            path,
            new SecretDocument(
                data.Values.ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? string.Empty),
                0,
                Description: null),
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
        using var request = CreateRequest(
            HttpMethod.Post,
            new SecretLocation(project, environment, path).Undelete);
        request.Content = JsonContent.Create(new { versions = new[] { version } });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DestroyAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        IReadOnlyList<int> versions,
        CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
        {
            return;
        }

        using var request = CreateRequest(
            HttpMethod.Post,
            new SecretLocation(project, environment, path).Destroy);
        request.Content = JsonContent.Create(new { versions });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PurgeAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        // Deleting the metadata path removes every version and the annotations with it.
        using var request = CreateRequest(
            HttpMethod.Delete,
            new SecretLocation(project, environment, path).Metadata);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<SecretMetadata?> ReadMetadataAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            new SecretLocation(project, environment, path).Metadata);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MetadataResponse>(cancellationToken: cancellationToken);
        var data = payload?.Data;
        if (data is null)
        {
            return null;
        }

        return new SecretMetadata(
            AnnotationCodec.Decode(data.CustomMetadata),
            new SecretRetention(
                data.MaxVersions == 0 ? null : data.MaxVersions,
                ParseDuration(data.DeleteVersionAfter)),
            data.CurrentVersion,
            DateTimeOffset.TryParse(data.UpdatedTime, out var updatedAt) ? updatedAt : null,
            ToVersions(data));
    }

    public async Task WriteMetadataAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        SecretAnnotations? annotations,
        SecretRetention? retention,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>();
        if (annotations is not null && !annotations.IsEmpty)
        {
            body["custom_metadata"] = AnnotationCodec.Encode(annotations);
        }

        if (retention is not null)
        {
            if (retention.MaxVersions is { } maxVersions)
            {
                body["max_versions"] = maxVersions;
            }

            if (retention.DeleteVersionAfter is { } deleteAfter)
            {
                // OpenBao takes a Go duration string; "0s" turns the policy off.
                body["delete_version_after"] = $"{(int)deleteAfter.TotalSeconds}s";
            }
        }

        if (body.Count == 0)
        {
            return;
        }

        using var request = CreateRequest(
            HttpMethod.Patch,
            new SecretLocation(project, environment, path).Metadata);
        request.Content = JsonContent.Create(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/merge-patch+json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> ScanAsync(
        ProjectId project,
        EnvironmentId environment,
        string? folder,
        CancellationToken cancellationToken)
    {
        var at = new FolderLocation(
            project,
            environment,
            string.IsNullOrWhiteSpace(folder) ? null : SecretPath.Parse(folder));

        // SCAN is a verb OpenBao adds on top of HTTP. The documented query-parameter
        // form is used instead, so proxies that reject unknown methods still work.
        using var request = CreateRequest(HttpMethod.Get, $"{at.Metadata}?scan=true");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ListResponse>(cancellationToken: cancellationToken);
        return payload?.Data?.Keys?
            .Where(key => !key.EndsWith('/'))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList() ?? [];
    }

    private static TimeSpan? ParseDuration(string? value) =>
        int.TryParse(value?.TrimEnd('s'), out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>
    /// SecretLocation yields logical paths; the API version prefix is an HTTP detail
    /// and is added here so no caller has to remember it.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"v1/{path}");
        request.Headers.Add("X-Vault-Token", tokenAccessor.GetRequiredToken());
        return request;
    }

    private sealed record ReadResponse([property: JsonPropertyName("data")] SecretPayload? Data);
    private sealed record SecretPayload(
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, JsonElement> Values,
        [property: JsonPropertyName("metadata")] ReadVersionMetadata Metadata);

    /// <summary>The per-read envelope, distinct from the domain's SecretMetadata.</summary>
    private sealed record ReadVersionMetadata(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("custom_metadata")] IReadOnlyDictionary<string, string>? CustomMetadata);

    private sealed record MetadataResponse([property: JsonPropertyName("data")] MetadataPayload? Data);
    private sealed record MetadataPayload(
        [property: JsonPropertyName("versions")] IReadOnlyDictionary<string, MetadataVersion> Versions,
        [property: JsonPropertyName("custom_metadata")] IReadOnlyDictionary<string, string>? CustomMetadata,
        [property: JsonPropertyName("current_version")] int CurrentVersion,
        [property: JsonPropertyName("max_versions")] int MaxVersions,
        [property: JsonPropertyName("delete_version_after")] string? DeleteVersionAfter,
        [property: JsonPropertyName("updated_time")] string? UpdatedTime);
    private sealed record MetadataVersion(
        [property: JsonPropertyName("deletion_time")] string? DeletionTime,
        [property: JsonPropertyName("destroyed")] bool Destroyed);
    private sealed record ListResponse([property: JsonPropertyName("data")] ListPayload? Data);

    // Keys is nullable: OpenBao can answer with a data envelope that has no keys at
    // all, and a non-nullable declaration made that a NullReferenceException.
    private sealed record ListPayload([property: JsonPropertyName("keys")] IReadOnlyList<string>? Keys);
}
