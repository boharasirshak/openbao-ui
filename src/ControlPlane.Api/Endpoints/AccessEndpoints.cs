using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

/// <summary>Members, roles and machine identities.</summary>
public static class AccessEndpoints
{
    public static void MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(ApiClaims.AdminPolicy);

        MapMembers(admin);
        MapMachineIdentities(admin);
    }

    private static void MapMembers(IEndpointRouteBuilder admin)
    {
        var members = admin.MapGroup("/members");

        members.MapGet(
                "/",
                async (IIdentityService service, CancellationToken cancellationToken) =>
                {
                    var found = await service.ListAsync(cancellationToken);
                    return Results.Ok(found
                        .Select(member => new MemberResponse(
                            member.Username,
                            member.EntityId,
                            member.Disabled,
                            member.Policies))
                        .ToList());
                })
            .Produces<IReadOnlyList<MemberResponse>>();

        members.MapPost(
                "/",
                async (CreateMemberRequest request, IIdentityService service, CancellationToken cancellationToken) =>
                {
                    await service.CreateAsync(request.Username, request.Password, request.Policies, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);

        members.MapPut(
                "/{username}",
                async (
                    string username,
                    UpdateMemberRequest request,
                    IIdentityService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!string.IsNullOrWhiteSpace(request.Password))
                    {
                        await service.ResetPasswordAsync(username, request.Password, cancellationToken);
                    }

                    await service.SetPoliciesAsync(username, request.Policies, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);

        members.MapPost(
                "/{username}/roles",
                async (
                    string username,
                    AssignRolesRequest request,
                    IIdentityService service,
                    CancellationToken cancellationToken) =>
                {
                    if (request.Roles.Any(role => !Identifier.IsValidSegment(role)))
                    {
                        return Results.BadRequest();
                    }

                    await service.SetPoliciesAsync(username, request.Roles, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        members.MapPost(
                "/{username}/disable",
                async (string username, IIdentityService service, CancellationToken cancellationToken) =>
                {
                    await service.DisableAsync(username, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);

        members.MapDelete(
                "/{username}",
                async (string username, IIdentityService service, CancellationToken cancellationToken) =>
                {
                    await service.DeleteAsync(username, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapMachineIdentities(IEndpointRouteBuilder admin)
    {
        var machines = admin.MapGroup("/machine-identities");

        machines.MapGet(
                "/",
                async (IMachineIdentityService service, CancellationToken cancellationToken) =>
                {
                    var found = await service.ListAsync(cancellationToken);
                    return Results.Ok(found.Select(ToResponse).ToList());
                })
            .Produces<IReadOnlyList<MachineIdentityResponse>>();

        machines.MapPost(
                "/",
                async (
                    CreateMachineIdentityRequest request,
                    IMachineIdentityService service,
                    CancellationToken cancellationToken) =>
                    Results.Ok(ToResponse(await service.CreateAsync(
                        new MachineIdentity(
                            request.Name,
                            request.Name,
                            request.Project,
                            request.Environment,
                            request.ReadOnly,
                            request.TokenTtlSeconds,
                            request.TokenUses),
                        cancellationToken))))
            .Produces<MachineIdentityResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        machines.MapPost(
                "/{roleName}/secret-id",
                async (string roleName, IMachineIdentityService service, CancellationToken cancellationToken) =>
                    Results.Ok(new SecretIdResponse(
                        await service.GenerateSecretIdAsync(roleName, cancellationToken))))
            .Produces<SecretIdResponse>();

        machines.MapPost(
                "/{roleName}/secret-id/revoke",
                async (string roleName, IMachineIdentityService service, CancellationToken cancellationToken) =>
                {
                    await service.RevokeSecretIdsAsync(roleName, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);
    }

    private static MachineIdentityResponse ToResponse(MachineIdentity identity) =>
        new(
            identity.Name,
            identity.RoleId,
            identity.Project,
            identity.Environment,
            identity.ReadOnly,
            identity.TokenTtlSeconds,
            identity.TokenUses);
}
