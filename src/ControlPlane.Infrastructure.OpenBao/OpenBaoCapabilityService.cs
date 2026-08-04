using System.Text.Json;
using ControlPlane.Application;

namespace ControlPlane.Infrastructure.OpenBao;

/// <summary>
/// Asks OpenBao what the caller's own token may actually do, instead of inferring it
/// from policy names.
///
/// <para>
/// This is for showing the right affordances, never for authorization. OpenBao's 403
/// on the real request remains the only decision that matters — a capability answer
/// can go stale the moment a policy changes.
/// </para>
/// </summary>
public sealed class OpenBaoCapabilityService(OpenBaoAdministrativeClient client) : ICapabilityService
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> CapabilitiesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();
        if (paths.Count == 0)
        {
            return result;
        }

        // One round trip for the whole batch: capabilities-self takes a path array.
        var document = await client.PostAsyncValue(
            "v1/sys/capabilities-self",
            new { paths },
            cancellationToken);

        var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        foreach (var path in paths)
        {
            result[path] = root.TryGetProperty(path, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty).ToList()
                : [];
        }

        return result;
    }
}
