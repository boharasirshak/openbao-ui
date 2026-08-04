using ControlPlane.Application;
using ControlPlane.Contracts;

namespace ControlPlane.Api.Endpoints;

public static class DatabaseEndpoints
{
    public static void MapDatabaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/database/credentials/{role}",
                async (string role, IDatabaseCredentialService service, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var credential = await service.ReadAsync(role, cancellationToken);
                        return Results.Ok(new DatabaseCredentialResponse(
                            credential.Username,
                            credential.Password,
                            credential.LeaseId,
                            credential.ExpiresAt));
                    }
                    catch (ArgumentException)
                    {
                        // A database role may contain slashes, so it is not one of the
                        // IParsable identifier types and is still validated in the service.
                        return Results.BadRequest();
                    }
                })
            .RequireAuthorization()
            .Produces<DatabaseCredentialResponse>()
            .Produces(StatusCodes.Status400BadRequest);
    }
}
