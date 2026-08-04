using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/permissions",
                async (
                    PermissionsRequest request,
                    ICapabilityService capabilities,
                    CancellationToken cancellationToken) =>
                {
                    // Translate product resources to OpenBao paths, ask once, translate back.
                    var paths = new List<string>();
                    foreach (var resource in request.Resources)
                    {
                        paths.Add(DataPath(resource));
                    }

                    var answers = await capabilities.CapabilitiesAsync(paths, cancellationToken);

                    var results = request.Resources.Select(resource =>
                    {
                        var granted = answers.TryGetValue(DataPath(resource), out var list) ? list : [];
                        var root = granted.Contains("root");
                        return new PermissionResult(
                            resource.Project,
                            resource.Environment,
                            root || granted.Contains("read"),
                            root || granted.Contains("create") || granted.Contains("update"),
                            root || granted.Contains("delete"));
                    }).ToList();

                    return Results.Ok(new PermissionsResponse(results));
                })
            .RequireAuthorization()
            .Produces<PermissionsResponse>();
    }

    private static string DataPath(PermissionQuery resource) =>
        $"{SecretLocation.DataPrefix(resource.Project, resource.Environment ?? "+")}/*";
}
