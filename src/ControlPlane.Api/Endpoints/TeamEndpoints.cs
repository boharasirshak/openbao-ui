using ControlPlane.Application;
using ControlPlane.Contracts;

namespace ControlPlane.Api.Endpoints;

public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var teams = app.MapGroup("/api/admin/teams").RequireAuthorization(ApiClaims.AdminPolicy);

        teams.MapGet(
                "/",
                async (ITeamService service, CancellationToken cancellationToken) =>
                {
                    var found = await service.ListAsync(cancellationToken);
                    return Results.Ok(found.Select(ToResponse).ToList());
                })
            .Produces<IReadOnlyList<TeamResponse>>();

        teams.MapPost(
                "/",
                async (CreateTeamRequest request, ITeamService service, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return Results.Ok(ToResponse(
                            await service.CreateAsync(request.Name, request.Roles, cancellationToken)));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<TeamResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        teams.MapPut(
                "/{name}/roles",
                async (
                    string name,
                    SetTeamRolesRequest request,
                    ITeamService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return Results.Ok(ToResponse(
                            await service.SetRolesAsync(name, request.Roles, cancellationToken)));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<TeamResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        teams.MapPut(
                "/{name}/members",
                async (
                    string name,
                    SetTeamMembersRequest request,
                    ITeamService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return Results.Ok(ToResponse(
                            await service.SetMembersAsync(name, request.MemberEntityIds, cancellationToken)));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.Problem(error.ForClient(), statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<TeamResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        teams.MapDelete(
                "/{name}",
                async (string name, ITeamService service, CancellationToken cancellationToken) =>
                {
                    await service.DeleteAsync(name, cancellationToken);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent);
    }

    private static TeamResponse ToResponse(Domain.Team team) =>
        new(team.Name, team.Id, team.Roles, team.MemberEntityIds);
}
