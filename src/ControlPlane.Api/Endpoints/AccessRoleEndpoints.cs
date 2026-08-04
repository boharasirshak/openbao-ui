using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class AccessRoleEndpoints
{
    public static void MapAccessRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(ApiClaims.AdminPolicy);

        // Everything assignable to a member or a team, so pickers offer real names.
        admin.MapGet(
                "/assignable-policies",
                async (IAccessRoleService service, CancellationToken cancellationToken) =>
                    Results.Ok(new AssignablePoliciesResponse(
                        await service.AssignablePolicyNamesAsync(cancellationToken))))
            .Produces<AssignablePoliciesResponse>();

        var roles = admin.MapGroup("/projects/{project}/roles");

        roles.MapGet(
                "/",
                async (ProjectId project, IAccessRoleService service, CancellationToken cancellationToken) =>
                {
                    var found = await service.ListAsync(project, cancellationToken);
                    return Results.Ok(found.Select(ToResponse).ToList());
                })
            .Produces<IReadOnlyList<AccessRoleResponse>>()
            .Produces(StatusCodes.Status400BadRequest);

        roles.MapPut(
                "/{name}",
                async (
                    ProjectId project,
                    string name,
                    SaveAccessRoleRequest request,
                    IAccessRoleService service,
                    CancellationToken cancellationToken) =>
                {
                    var environments = request.Environments
                        .Select(entry => EnvironmentId.TryParse(entry, null, out var parsed) ? parsed : (EnvironmentId?)null)
                        .OfType<EnvironmentId>()
                        .ToList();

                    try
                    {
                        var saved = await service.SaveAsync(
                            new AccessRole(
                                name,
                                project,
                                environments,
                                ToPermissions(request.Permissions),
                                request.Description),
                            cancellationToken);
                        return Results.Ok(ToResponse(saved));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<AccessRoleResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        roles.MapDelete(
                "/{name}",
                async (
                    ProjectId project,
                    string name,
                    IAccessRoleService service,
                    CancellationToken cancellationToken) =>
                {
                    await service.DeleteAsync(project, name, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static RolePermissions ToPermissions(RolePermissionsPayload payload) =>
        new(
            payload.Describe,
            payload.ReadValues,
            payload.WriteSecrets,
            payload.DeleteSecrets,
            payload.ManageDetails,
            payload.RollBack,
            payload.Destroy);

    private static AccessRoleResponse ToResponse(AccessRole role) =>
        new(
            role.Name,
            role.Project.Value,
            role.PolicyName,
            role.Environments.Select(environment => environment.Value).ToList(),
            new RolePermissionsPayload(
                role.Permissions.Describe,
                role.Permissions.ReadValues,
                role.Permissions.WriteSecrets,
                role.Permissions.DeleteSecrets,
                role.Permissions.ManageDetails,
                role.Permissions.RollBack,
                role.Permissions.Destroy),
            role.Description);
}
