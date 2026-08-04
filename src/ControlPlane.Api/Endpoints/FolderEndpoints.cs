using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this IEndpointRouteBuilder app)
    {
        var secrets = app.MapGroup("/api/projects/{project}/environments/{environment}/secrets")
            .RequireAuthorization();

        // A folder in KV-v2 is only a shared path prefix, so there is nothing to create
        // and nothing to delete except the secrets underneath it. This walks them.
        secrets.MapDelete(
                "/folders/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    bool? purge,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var paths = await engine.ScanAsync(project, environment, path.Value, cancellationToken);
                    var affected = 0;

                    foreach (var relative in paths)
                    {
                        // SCAN returns paths relative to the folder it was given.
                        if (!SecretPath.TryParse($"{path.Value}/{relative}", null, out var secret))
                        {
                            continue;
                        }

                        if (purge == true)
                        {
                            await engine.PurgeAsync(project, environment, secret, cancellationToken);
                        }
                        else
                        {
                            await engine.DeleteAsync(project, environment, secret, cancellationToken);
                        }

                        affected++;
                    }

                    return Results.Ok(new FolderOperationResponse(affected));
                })
            .Produces<FolderOperationResponse>()
            .Produces(StatusCodes.Status400BadRequest);
    }
}
