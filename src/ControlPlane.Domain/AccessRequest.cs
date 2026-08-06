namespace ControlPlane.Domain;

public enum AccessRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Someone asking for a role on a project they cannot touch yet. Holds no secret
/// values — who, which roles, why — so unlike a pending change it lives entirely in
/// the control plane's own mount.
///
/// <para>
/// One request per person per project, stored under the requester's username: asking
/// again replaces your previous request rather than piling up duplicates.
/// </para>
/// </summary>
public sealed record AccessRequest(
    ProjectId Project,
    string Username,
    IReadOnlyList<string> Policies,
    string? Reason,
    DateTimeOffset RequestedAt,
    AccessRequestStatus Status,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null)
{
    public bool IsOpen => Status == AccessRequestStatus.Pending;
}

/// <summary>
/// The one policy every human account holds. It exists because the API always talks
/// to OpenBao with the caller's own token: someone requesting access to a project has,
/// by definition, no grant on it yet, so the ability to file the request itself has to
/// come from somewhere. This is that somewhere, and it is deliberately tiny:
///
/// <list type="bullet">
/// <item>write (but not read) on access-request records — you can file or replace a
/// request, but nobody can browse other people's requests with it;</item>
/// <item>read on project records — names and environment lists only, never secrets —
/// so the request dialog can offer real roles.</item>
/// </list>
/// </summary>
public static class MemberBasePolicy
{
    public const string Name = "member-base";

    public static string Hcl(string controlMount) =>
        $"path \"{controlMount}/data/access-requests/*\" {{ capabilities = [\"create\", \"update\"] }}\n"
        + $"path \"{controlMount}/data/projects/*\" {{ capabilities = [\"read\"] }}";
}
