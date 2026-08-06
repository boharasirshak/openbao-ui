using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

/// <summary>
/// Who has access to one project, managed from inside the project. Before this, giving
/// someone access meant the org-wide members page and raw policy strings — the single
/// most confusing flow in the product. Here a role is a plain label; the policy name
/// never reaches the screen.
///
/// <para>
/// Gated to org administrators for now: changing a member's policies runs with the
/// control token. The narrower "project admins manage their own project" needs a
/// per-project authority model this API does not have yet.
/// </para>
/// </summary>
public static class ProjectMemberEndpoints
{
    public static void MapProjectMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/projects/{project}/members")
            .RequireAuthorization(ApiClaims.AdminPolicy);

        members.MapGet(
                "/",
                async (
                    ProjectId project,
                    IProjectService projects,
                    IIdentityService identity,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    if (stored is null)
                    {
                        return Results.NotFound();
                    }

                    var everyone = await identity.ListAsync(cancellationToken);
                    var response = everyone
                        .Select(member => new ProjectMemberResponse(
                            member.Username,
                            member.Disabled,
                            member.Policies
                                .Where(policy => ProjectPolicy.BelongsTo(project, stored.Environments, policy))
                                .Select(policy => new ProjectRoleOption(
                                    policy,
                                    ProjectPolicy.Label(project, stored.Environments, policy)))
                                .ToList()))
                        .Where(member => member.Roles.Count > 0)
                        .OrderBy(member => member.Username, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return Results.Ok(response);
                })
            .Produces<IReadOnlyList<ProjectMemberResponse>>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // Everything the add/edit dialog needs in one call: who exists, and which roles
        // this project can hand out.
        members.MapGet(
                "/options",
                async (
                    ProjectId project,
                    IProjectService projects,
                    IIdentityService identity,
                    IAccessRoleService roles,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    if (stored is null)
                    {
                        return Results.NotFound();
                    }

                    var everyone = await identity.ListAsync(cancellationToken);
                    var custom = await roles.ListAsync(project, cancellationToken);
                    return Results.Ok(new ProjectMemberOptions(
                        everyone.Select(member => member.Username).ToList(),
                        AssignableRoles(project, stored.Environments, custom)));
                })
            .Produces<ProjectMemberOptions>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        members.MapPut(
                "/{username}",
                async (
                    ProjectId project,
                    string username,
                    SetProjectRolesRequest request,
                    IProjectService projects,
                    IIdentityService identity,
                    IAccessRoleService roles,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    var stored = await projects.GetAsync(project, cancellationToken);
                    if (stored is null)
                    {
                        return Results.NotFound();
                    }

                    // Only roles this project can actually hand out. Anything else in
                    // the body — another project's policy, wrapper-admin, root — is
                    // rejected outright rather than filtered, so a bad request is loud.
                    var custom = await roles.ListAsync(project, cancellationToken);
                    var assignable = AssignableRoles(project, stored.Environments, custom)
                        .Select(option => option.Policy)
                        .ToHashSet(StringComparer.Ordinal);
                    if (request.Policies.Any(policy => !assignable.Contains(policy)))
                    {
                        return Results.BadRequest("Only this project's own roles can be assigned here.");
                    }

                    var member = (await identity.ListAsync(cancellationToken))
                        .FirstOrDefault(candidate => candidate.Username == username);
                    if (member is null)
                    {
                        return Results.NotFound();
                    }

                    // Replace this project's slice of the policy list and leave every
                    // other grant — other projects, org roles — exactly as it was.
                    var kept = member.Policies
                        .Where(policy => !ProjectPolicy.BelongsTo(project, stored.Environments, policy));
                    await identity.SetPoliciesAsync(
                        username,
                        [.. kept.Concat(request.Policies).Distinct(StringComparer.Ordinal)],
                        cancellationToken);
                    await activity.RecordAsync(
                        context,
                        ActivityAction.AccessChanged,
                        project,
                        keys: [username]);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static List<ProjectRoleOption> AssignableRoles(
        ProjectId project,
        IReadOnlyList<ProjectEnvironment> environments,
        IReadOnlyList<AccessRole> custom)
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

        options.AddRange(custom.Select(role => new ProjectRoleOption(role.PolicyName, role.Name)));
        return options;
    }
}
