using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/api/admin/projects")
            .RequireAuthorization(ApiClaims.AdminPolicy);

        // Reading your own project list is not an administrative act, and gating it
        // behind the admin policy left members with no way to find their projects at
        // all. The listing is scoped by the caller's token inside OpenBao, so a member
        // only ever sees what they hold a role on. Creating and deleting stay admin-only.
        app.MapGet(
                "/api/admin/projects",
                async (IProjectService service, CancellationToken cancellationToken) =>
                {
                    var found = await service.ListAsync(cancellationToken);
                    return Results.Ok(found.Select(ToResponse).ToList());
                })
            .RequireAuthorization()
            .Produces<IReadOnlyList<ProjectResponse>>();

        // Creating an existing project is idempotent: the mount is left alone and the
        // generated policies are rewritten, which doubles as a policy reconcile.
        projects.MapPost(
                "/{project}",
                async (
                    ProjectId project,
                    CreateProjectRequest request,
                    IProjectService service,
                    CancellationToken cancellationToken) =>
                    Results.Ok(ToResponse(await service.CreateAsync(project, request.Description, cancellationToken))))
            .Produces<ProjectResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        MapEnvironments(projects);

        projects.MapDelete(
                "/{project}",
                async (ProjectId project, IProjectService service, CancellationToken cancellationToken) =>
                {
                    await service.DeleteAsync(project, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static void MapEnvironments(IEndpointRouteBuilder projects)
    {
        var environments = projects.MapGroup("/{project}/environments");

        environments.MapPost(
                "/",
                async (
                    ProjectId project,
                    CreateEnvironmentRequest request,
                    IProjectService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EnvironmentId.TryParse(request.Id, null, out var environment))
                    {
                        return Results.Problem(
                            "An environment id uses letters, digits, dashes and underscores, and cannot start with an underscore.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    try
                    {
                        var updated = await service.AddEnvironmentAsync(
                            project,
                            environment,
                            request.DisplayName,
                            request.Protected,
                            cancellationToken);
                        return Results.Ok(ToResponse(updated));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        environments.MapPatch(
                "/{environment}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    UpdateEnvironmentRequest request,
                    IProjectService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var updated = await service.UpdateEnvironmentAsync(
                            project,
                            environment,
                            request.DisplayName,
                            request.Protected,
                            request.Position,
                            cancellationToken);
                        return Results.Ok(ToResponse(updated));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        environments.MapDelete(
                "/{environment}",
                async (
                    ProjectId project,
                    EnvironmentId environment,
                    bool? purge,
                    IProjectService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var updated = await service.RemoveEnvironmentAsync(
                            project,
                            environment,
                            purge == true,
                            cancellationToken);
                        return Results.Ok(ToResponse(updated));
                    }
                    catch (ArgumentException error)
                    {
                        // Includes the "still holds secrets" refusal, which the person
                        // needs to read before they can decide to purge.
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    internal static ProjectResponse ToResponse(Project project) =>
        new(
            project.Id.Value,
            project.Description,
            project.Environments
                .Select(environment => new EnvironmentResponse(
                    environment.Id.Value,
                    environment.DisplayName,
                    environment.Protected,
                    environment.Position))
                .ToList());
}
