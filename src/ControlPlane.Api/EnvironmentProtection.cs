using System.Net;
using ControlPlane.Application;
using ControlPlane.Domain;

namespace ControlPlane.Api;

/// <summary>
/// A protected environment cannot be written directly through this API — the change has
/// to go through review first.
///
/// <para>
/// Say the limit plainly: this is a workflow guard in the application, not an OpenBao ACL
/// boundary. Everyone who can propose a change also holds the OpenBao capability to make
/// it, so a caller using their token against OpenBao directly bypasses this entirely. It
/// buys a second pair of eyes on the normal path, not a wall.
/// </para>
/// </summary>
public static class EnvironmentProtection
{
    public static async Task<bool> IsProtectedAsync(
        this IProjectService projects,
        ProjectId project,
        EnvironmentId environment,
        CancellationToken cancellationToken)
    {
        // ponytail: one extra KV read per write. Cache by project if it ever shows up in
        // a trace; a KV read is a few milliseconds and writes are not a hot path.
        try
        {
            var stored = await projects.GetAsync(project, cancellationToken);
            return stored?.Environments
                .Any(candidate => candidate.Id == environment && candidate.Protected) == true;
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Forbidden)
        {
            // The project's policies predate this check and do not grant read on its own
            // record. Refusing the write is the safe direction — the alternative is
            // treating "cannot tell" as "not protected" and letting a production write
            // through. Re-creating the project regenerates its policies.
            throw new InvalidOperationException(
                $"This project's access policies are out of date, so \"{environment}\" cannot be "
                + $"checked for protection. Re-create the project ({project}) to regenerate them.",
                error);
        }
    }

    /// <summary>
    /// 409 rather than 403: the caller is allowed to make this change, just not in one
    /// step. The message names the route that will accept it.
    /// </summary>
    public static IResult NeedsApproval(ProjectId project, EnvironmentId environment) =>
        Results.Problem(
            $"{environment} is protected, so changes to it need someone else's approval. "
            + $"Send this change to /api/projects/{project}/changes instead.",
            statusCode: StatusCodes.Status409Conflict,
            title: "Approval required");
}
