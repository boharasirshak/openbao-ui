using System.Security.Claims;
using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class ChangeRequestEndpoints
{
    public static void MapChangeRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var changes = app.MapGroup("/api/projects/{project}/changes").RequireAuthorization();

        changes.MapGet(
                "/",
                async (
                    ProjectId project,
                    IChangeRequestService requests,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    var all = await requests.ListAsync(project, cancellationToken);
                    return Results.Ok(all.Select(request => ToResponse(request, context)).ToList());
                })
            .Produces<IReadOnlyList<ChangeRequestResponse>>()
            .Produces(StatusCodes.Status400BadRequest);

        changes.MapPost(
                "/",
                async (
                    ProjectId project,
                    CreateChangeRequest body,
                    IChangeRequestService requests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    if (!EnvironmentId.TryParse(body.Environment, null, out var environment)
                        || !SecretPath.TryParse(body.Path, null, out var path))
                    {
                        return Results.BadRequest();
                    }

                    try
                    {
                        var request = await requests.ProposeAsync(
                            project,
                            environment,
                            path,
                            body.Values,
                            body.Description,
                            body.Reason,
                            body.ExpectedVersion,
                            body.Delete,
                            Actor(context),
                            cancellationToken);
                        await activity.RecordAsync(
                            context,
                            ActivityAction.ChangeProposed,
                            project,
                            environment,
                            path,
                            request.KeysAffected);
                        return Results.Ok(ToResponse(request, context));
                    }
                    catch (ArgumentException error)
                    {
                        return Results.BadRequest(ProblemMessage.ForClient(error));
                    }
                })
            .Produces<ChangeRequestResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        // The proposed values are read through the caller's own token, so OpenBao — not
        // this endpoint — decides whether a reviewer may see them.
        changes.MapGet(
                "/{id}/values",
                async (
                    ProjectId project,
                    string id,
                    IChangeRequestService requests,
                    CancellationToken cancellationToken) =>
                {
                    var request = await requests.GetAsync(project, id, cancellationToken);
                    if (request is null)
                    {
                        return Results.NotFound();
                    }

                    var proposed = await requests.ReadProposedAsync(request, cancellationToken);
                    return proposed is null
                        ? Results.NotFound()
                        : Results.Ok(new SecretDocumentResponse(
                            proposed.Values,
                            proposed.Version,
                            proposed.Description));
                })
            .Produces<SecretDocumentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        changes.MapPost(
                "/{id}/approve",
                (
                    ProjectId project,
                    string id,
                    ReviewChangeRequest body,
                    IChangeRequestService requests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                    ReviewAsync(
                        () => requests.ApplyAsync(project, id, Actor(context), body.Note, cancellationToken),
                        ActivityAction.ChangeApplied,
                        project,
                        activity,
                        context))
            .Produces<ChangeRequestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        changes.MapPost(
                "/{id}/reject",
                (
                    ProjectId project,
                    string id,
                    ReviewChangeRequest body,
                    IChangeRequestService requests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                    ReviewAsync(
                        () => requests.RejectAsync(project, id, Actor(context), body.Note, cancellationToken),
                        ActivityAction.ChangeRejected,
                        project,
                        activity,
                        context))
            .Produces<ChangeRequestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        changes.MapDelete(
                "/{id}",
                (
                    ProjectId project,
                    string id,
                    IChangeRequestService requests,
                    IActivityLog activity,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                    ReviewAsync(
                        () => requests.WithdrawAsync(project, id, Actor(context), cancellationToken),
                        ActivityAction.ChangeWithdrawn,
                        project,
                        activity,
                        context))
            .Produces<ChangeRequestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Every review action fails the same four ways, so the mapping lives in one place
    /// rather than in three copies of the same try/catch.
    /// </summary>
    private static async Task<IResult> ReviewAsync(
        Func<Task<ChangeRequest>> action,
        ActivityAction recorded,
        ProjectId project,
        IActivityLog activity,
        HttpContext context)
    {
        try
        {
            var request = await action();
            await activity.RecordAsync(
                context,
                recorded,
                project,
                request.Environment,
                request.Path,
                request.KeysAffected);
            return Results.Ok(ToResponse(request, context));
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
        catch (ArgumentException error)
        {
            return Results.BadRequest(ProblemMessage.ForClient(error));
        }
    }

    internal static string Actor(HttpContext context) =>
        context.User.FindFirstValue(ApiClaims.Username)
        ?? context.User.FindFirstValue(ClaimTypes.Name)
        ?? "unknown";

    private static ChangeRequestResponse ToResponse(ChangeRequest request, HttpContext context) =>
        new(
            request.Id,
            request.Project.Value,
            request.Environment.Value,
            request.Path.Value,
            request.KeysAffected,
            request.RequestedBy,
            request.RequestedAt,
            request.Status.ToString(),
            request.Reason,
            request.ExpectedVersion,
            request.Reviews
                .Select(review => new ChangeReviewPayload(review.Reviewer, review.Approved, review.At, review.Note))
                .ToList(),
            request.CanBeReviewedBy(Actor(context)),
            request.IsDeletion);
}
