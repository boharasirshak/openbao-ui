using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoAdministrativeClient(
    HttpClient client,
    IOptions<OpenBaoOptions> options,
    IOpenBaoTokenAccessor? tokenAccessor = null)
{
    public async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, cancellationToken);

        // A missing secret answers 404, but reading under a mount that does not exist
        // yet answers 400. Both mean "nothing here", and on a fresh install the second
        // happens before the control plane's own mount has been created.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public async Task PutAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, path);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonDocument> PostAsyncValue(string path, object body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, path);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var token = TryGetSessionToken() ?? options.Value.ControlToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An authorized OpenBao session or OpenBao:ControlToken is required.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Vault-Token", token);
        return request;
    }

    private string? TryGetSessionToken()
    {
        try
        {
            return tokenAccessor?.GetRequiredToken();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
