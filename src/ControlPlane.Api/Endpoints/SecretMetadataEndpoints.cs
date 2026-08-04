using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

/// <summary>Annotations, retention and the irreversible version operations.</summary>
public static class SecretMetadataEndpoints
{
    public static void MapSecretMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var secrets = app.MapGroup("/api/projects/{project}/environments/{environment}/secrets")
            .RequireAuthorization();

        secrets.MapGet(
                "/metadata/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var metadata = await engine.ReadMetadataAsync(project, environment, path, cancellationToken);
                    return metadata is null ? Results.NotFound() : Results.Ok(ToResponse(metadata));
                })
            .Produces<SecretMetadataResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapPatch(
                "/metadata/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    UpdateSecretMetadataRequest request,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        await engine.WriteMetadataAsync(
                            project,
                            environment,
                            path,
                            ToAnnotations(request.Annotations),
                            ToRetention(request.Retention),
                            cancellationToken);
                        return Results.NoContent();
                    }
                    catch (ArgumentException error)
                    {
                        // Tag charset and the custom_metadata size cap are validated in
                        // the domain, and the reason is worth showing the person editing.
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        secrets.MapPost(
                "/destroy/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    DestroyVersionsRequest request,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    await engine.DestroyAsync(project, environment, path, request.Versions, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapDelete(
                "/purge/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    await engine.PurgeAsync(project, environment, path, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static SecretAnnotations? ToAnnotations(SecretAnnotationsPayload? payload) =>
        payload is null ? null : new SecretAnnotations(payload.Description, payload.Tags, payload.Comment);

    private static SecretRetention? ToRetention(SecretRetentionPayload? payload) =>
        payload is null
            ? null
            : new SecretRetention(
                payload.MaxVersions,
                payload.DeleteVersionAfterSeconds is { } seconds
                    ? TimeSpan.FromSeconds(seconds)
                    : null);

    private static SecretMetadataResponse ToResponse(SecretMetadata metadata) =>
        new(
            new SecretAnnotationsPayload(
                metadata.Annotations.Description,
                metadata.Annotations.Tags,
                metadata.Annotations.Comment),
            new SecretRetentionPayload(
                metadata.Retention.MaxVersions,
                metadata.Retention.DeleteVersionAfter is { } after ? (int)after.TotalSeconds : null),
            metadata.CurrentVersion,
            metadata.UpdatedAt,
            metadata.Versions
                .Select(version => new SecretVersionResponse(
                    version.Version,
                    version.DeletedAt,
                    version.Destroyed))
                .ToList());
}
