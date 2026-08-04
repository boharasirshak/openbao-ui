using System.Security.Claims;
using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/projects/{project}/activity",
                async (
                    ProjectId project,
                    int? days,
                    IActivityLog activity,
                    CancellationToken cancellationToken) =>
                {
                    var entries = await activity.ReadAsync(project.Value, days ?? 14, cancellationToken);
                    return Results.Ok(entries.Select(ToResponse).ToList());
                })
            .RequireAuthorization()
            .Produces<IReadOnlyList<ActivityEntryResponse>>()
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static ActivityEntryResponse ToResponse(ActivityEntry entry) =>
        new(
            entry.At,
            entry.Actor,
            entry.Action.ToString(),
            entry.Project,
            entry.Environment,
            entry.Path,
            entry.KeysAffected,
            entry.Version);
}

/// <summary>
/// Recording must never change the outcome of the operation being recorded, so every
/// helper here swallows its own failures — see <see cref="IActivityLog"/>.
/// </summary>
public static class ActivityRecording
{
    public static Task RecordAsync(
        this IActivityLog activity,
        HttpContext context,
        ActivityAction action,
        ProjectId project,
        EnvironmentId? environment = null,
        SecretPath? path = null,
        IReadOnlyList<string>? keys = null,
        int? version = null) =>
        activity.RecordAsync(
            new ActivityEntry(
                DateTimeOffset.UtcNow,
                Actor(context),
                action,
                project.Value,
                environment?.Value,
                path?.Value,
                keys ?? [],
                version),
            context.RequestAborted);

    private static string Actor(HttpContext context) =>
        context.User.FindFirstValue(ApiClaims.Username)
        ?? context.User.FindFirstValue(ClaimTypes.Name)
        ?? "unknown";
}
