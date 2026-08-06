using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

/// <summary>
/// Asking for access, from inside the product instead of over chat. Anyone signed in
/// can ask; only an administrator can grant. The dialog offers the built-in roles
/// (project admin, per-environment read/edit) — custom roles are handed out from the
/// Members page, which keeps this endpoint readable by people with no access yet.
/// </summary>
public static class AccessRequestEndpoints
{
    public static void MapAccessRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var requests = app.MapGroup("/api/projects/{project}/access-requests");

        // What can be asked for. Reads the project record with the caller's own token
        // under the member-base grant — names and environments only, never a secret.
        requests.MapGet(
                "/options",
                async (
                    ProjectId project,
                    IProjectService projects,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    return stored is null
                        ? Results.NotFound()
                        : Results.Ok(new AccessRequestOptions(BuiltInRoles(project, stored.Environments)));
                })
            .RequireAuthorization()
            .Produces<AccessRequestOptions>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        requests.MapPost(
                "/",
                async (
                    ProjectId project,
                    CreateAccessRequest body,
                    IProjectService projects,
                    IAccessRequestService accessRequests,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    if (stored is null)
                    {
                        return Results.NotFound();
                    }

                    // Only roles this project can hand out; anything else is a loud 400.
                    var assignable = BuiltInRoles(project, stored.Environments)
                        .Select(option => option.Policy)
                        .ToHashSet(StringComparer.Ordinal);
                    if (body.Policies.Count == 0
                        || body.Policies.Any(policy => !assignable.Contains(policy)))
                    {
                        return Results.BadRequest("Pick one or more of this project's own roles.");
                    }

                    await accessRequests.SubmitAsync(
                        new AccessRequest(
                            project,
                            ChangeRequestEndpoints.Actor(context),
                            [.. body.Policies.Distinct(StringComparer.Ordinal)],
                            body.Reason,
                            DateTimeOffset.UtcNow,
                            AccessRequestStatus.Pending),
                        cancellationToken);
                    return Results.NoContent();
                })
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        requests.MapGet(
                "/",
                async (
                    ProjectId project,
                    IProjectService projects,
                    IAccessRequestService accessRequests,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    if (stored is null)
                    {
                        return Results.NotFound();
                    }

                    var all = await accessRequests.ListAsync(project, cancellationToken);
                    return Results.Ok(all
                        .Select(request => new AccessRequestResponse(
                            request.Username,
                            request.Policies
                                .Select(policy => new ProjectRoleOption(
                                    policy,
                                    ProjectPolicy.Label(project, stored.Environments, policy)))
                                .ToList(),
                            request.Reason,
                            request.RequestedAt,
                            request.Status.ToString(),
                            request.ReviewedBy))
                        .ToList());
                })
            .RequireAuthorization(ApiClaims.AdminPolicy)
            .Produces<IReadOnlyList<AccessRequestResponse>>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        requests.MapPost(
                "/{username}/approve",
                (
                    ProjectId project,
                    string username,
                    IAccessRequestService accessRequests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                    ReviewAsync(
                        () => accessRequests.ApproveAsync(
                            project,
                            username,
                            ChangeRequestEndpoints.Actor(context),
                            cancellationToken),
                        record: true,
                        project,
                        username,
                        activity,
                        context))
            .RequireAuthorization(ApiClaims.AdminPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        requests.MapPost(
                "/{username}/reject",
                (
                    ProjectId project,
                    string username,
                    IAccessRequestService accessRequests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                    ReviewAsync(
                        () => accessRequests.RejectAsync(
                            project,
                            username,
                            ChangeRequestEndpoints.Actor(context),
                            cancellationToken),
                        record: false,
                        project,
                        username,
                        activity,
                        context))
            .RequireAuthorization(ApiClaims.AdminPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ReviewAsync(
        Func<Task<AccessRequest>> action,
        bool record,
        ProjectId project,
        string username,
        IActivityLog activity,
        HttpContext context)
    {
        try
        {
            await action();
            if (record)
            {
                await activity.RecordAsync(context, ActivityAction.AccessChanged, project, keys: [username]);
            }

            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException error)
        {
            return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException error)
        {
            return Results.BadRequest(error.Message);
        }
    }

    /// <summary>Project admin plus read/edit per environment. Deliberately no custom roles.</summary>
    private static List<ProjectRoleOption> BuiltInRoles(
        ProjectId project,
        IReadOnlyList<ProjectEnvironment> environments)
    {
        var options = new List<ProjectRoleOption>
        {
            new(ProjectPolicy.Admin(project), "Project admin"),
        };
        foreach (var environment in environments)
        {
            options.Add(new ProjectRoleOption(
                ProjectPolicy.Environment(project, environment.Id, readOnly: false),
                $"{environment.DisplayName} · edit"));
            options.Add(new ProjectRoleOption(
                ProjectPolicy.Environment(project, environment.Id, readOnly: true),
                $"{environment.DisplayName} · read"));
        }

        return options;
    }
}
