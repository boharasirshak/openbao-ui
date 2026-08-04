using ControlPlane.Application;
using ControlPlane.Contracts;

namespace ControlPlane.Api.Endpoints;

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/shares",
                async (
                    CreateShareRequest request,
                    ISecretShareService shares,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var (token, expiresAt) = await shares.WrapAsync(
                            request.Values,
                            TimeSpan.FromSeconds(request.TtlSeconds),
                            cancellationToken);
                        return Results.Ok(new CreateShareResponse(token, expiresAt));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .RequireAuthorization()
            .Produces<CreateShareResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Deliberately anonymous: the whole point is that a recipient without an
        // account can open the link once. The wrapping token is the credential, and
        // OpenBao enforces single use and expiry.
        app.MapPost(
                "/api/shares/{token}/open",
                async (string token, ISecretShareService shares, CancellationToken cancellationToken) =>
                {
                    var values = await shares.UnwrapAsync(token, cancellationToken);
                    return values is null
                        ? Results.NotFound()
                        : Results.Ok(new ShareResponse(values));
                })
            .AllowAnonymous()
            .Produces<ShareResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }
}
