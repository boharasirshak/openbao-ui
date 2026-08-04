using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class SecretEndpoints
{
    // ProjectId, EnvironmentId and SecretPath are IParsable, so the binder rejects a
    // malformed route with 400 before a handler runs. That replaced an identical
    // try/catch in every handler below.
    public static void MapSecretEndpoints(this IEndpointRouteBuilder app)
    {
        var secrets = app.MapGroup("/api/projects/{project}/environments/{environment}/secrets")
            .RequireAuthorization();

        secrets.MapGet(
                "/list/{**folder}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    string? folder,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    // An empty folder is the environment root, so this one stays a
                    // string: SecretPath deliberately rejects the empty path.
                    var entries = await engine.ListAsync(project, environment, folder, cancellationToken);
                    return Results.Ok(entries
                        .Select(entry => new SecretEntryResponse(entry.Name, entry.IsFolder))
                        .ToList());
                })
            .Produces<IReadOnlyList<SecretEntryResponse>>()
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapGet(
                "/versions/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var versions = await engine.ListVersionsAsync(project, environment, path, cancellationToken);
                    return Results.Ok(versions
                        .Select(version => new SecretVersionResponse(
                            version.Version,
                            version.DeletedAt,
                            version.Destroyed))
                        .ToList());
                })
            .Produces<IReadOnlyList<SecretVersionResponse>>()
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapPost(
                "/restore/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    SecretVersionRequest request,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    await engine.RestoreAsync(project, environment, path, request.Version, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapPost(
                "/undelete/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    SecretVersionRequest request,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    await engine.UndeleteAsync(project, environment, path, request.Version, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapGet(
                "/export/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    string? format,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var document = await engine.ReadAsync(project, environment, path, cancellationToken);
                    if (document is null)
                    {
                        return Results.NotFound();
                    }

                    if (string.Equals(format, "env", StringComparison.OrdinalIgnoreCase))
                    {
                        var contents = string.Join(
                            Environment.NewLine,
                            document.Values.Select(pair => $"{pair.Key}={EscapeDotEnv(pair.Value)}"));
                        return Results.Text(contents, "text/plain");
                    }

                    return Results.Json(document.Values);
                })
            .Produces<IReadOnlyDictionary<string, string>>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapPost(
                "/import/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ImportSecretsRequest request,
                    ISecretsEngine engine,
                    IProjectService projects,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    if (!HasValidKeys(request.Values))
                    {
                        return Results.BadRequest();
                    }

                    try
                    {
                        if (await projects.IsProtectedAsync(project, environment, cancellationToken))
                        {
                            return EnvironmentProtection.NeedsApproval(project, environment);
                        }
                    }
                    catch (InvalidOperationException stale)
                    {
                        return Results.Problem(stale.Message, statusCode: StatusCodes.Status409Conflict);
                    }

                    await engine.WriteAsync(
                        project,
                        environment,
                        path,
                        new SecretDocument(request.Values, 0, request.Description),
                        request.ExpectedVersion,
                        cancellationToken);
                    await activity.RecordAsync(
                        context,
                        ActivityAction.SecretImported,
                        project,
                        environment,
                        path,
                        [.. request.Values.Keys]);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        secrets.MapGet(
                "/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var document = await engine.ReadAsync(project, environment, path, cancellationToken);
                    return document is null
                        ? Results.NotFound()
                        : Results.Ok(new SecretDocumentResponse(
                            document.Values,
                            document.Version,
                            document.Description));
                })
            .Produces<SecretDocumentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        secrets.MapPut(
                "/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    SecretDocumentRequest request,
                    ISecretsEngine engine,
                    IProjectService projects,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    if (!HasValidKeys(request.Values))
                    {
                        return Results.BadRequest();
                    }

                    try
                    {
                        if (await projects.IsProtectedAsync(project, environment, cancellationToken))
                        {
                            return EnvironmentProtection.NeedsApproval(project, environment);
                        }
                    }
                    catch (InvalidOperationException stale)
                    {
                        return Results.Problem(stale.Message, statusCode: StatusCodes.Status409Conflict);
                    }

                    await engine.WriteAsync(
                        project,
                        environment,
                        path,
                        new SecretDocument(request.Values, 0, request.Description),
                        request.ExpectedVersion,
                        cancellationToken);
                    await activity.RecordAsync(
                        context,
                        ActivityAction.SecretSaved,
                        project,
                        environment,
                        path,
                        [.. request.Values.Keys]);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        secrets.MapDelete(
                "/{**path}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    SecretPath path,
                    ISecretsEngine engine,
                    IProjectService projects,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        if (await projects.IsProtectedAsync(project, environment, cancellationToken))
                        {
                            return EnvironmentProtection.NeedsApproval(project, environment);
                        }
                    }
                    catch (InvalidOperationException stale)
                    {
                        return Results.Problem(stale.Message, statusCode: StatusCodes.Status409Conflict);
                    }

                    await engine.DeleteAsync(project, environment, path, cancellationToken);
                    await activity.RecordAsync(
                        context,
                        ActivityAction.SecretDeleted,
                        project,
                        environment,
                        path);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static bool HasValidKeys(IReadOnlyDictionary<string, string> values) =>
        values.Count > 0 && values.Keys.All(Identifier.IsValidSecretKey);

    private static string EscapeDotEnv(string value) =>
        value.IndexOfAny([' ', '\t', '\n', '\r', '"', '\'']) >= 0
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")}\""
            : value;
}
