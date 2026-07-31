using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoAdministrativeClient(HttpClient client, IOptions<OpenBaoOptions> options)
{
    public async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
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
        if (string.IsNullOrWhiteSpace(options.Value.ControlToken))
        {
            throw new InvalidOperationException("OpenBao:ControlToken is required for administrative operations.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Vault-Token", options.Value.ControlToken);
        return request;
    }
}
