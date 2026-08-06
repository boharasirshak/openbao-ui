using ControlPlane.Domain;

namespace ControlPlane.Application;

public interface ISecretsEngine
{
    Task<SecretDocument?> ReadAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task WriteAsync(ProjectId project, EnvironmentId environment, SecretPath path, SecretDocument document, int? expectedVersion, CancellationToken cancellationToken);
    Task DeleteAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecretEntry>> ListAsync(ProjectId project, EnvironmentId environment, string? folder, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task RestoreAsync(ProjectId project, EnvironmentId environment, SecretPath path, int version, CancellationToken cancellationToken);
    Task UndeleteAsync(ProjectId project, EnvironmentId environment, SecretPath path, int version, CancellationToken cancellationToken);

    /// <summary>Irreversible: the version's data is erased, unlike a soft delete.</summary>
    Task DestroyAsync(ProjectId project, EnvironmentId environment, SecretPath path, IReadOnlyList<int> versions, CancellationToken cancellationToken);

    /// <summary>Removes the secret and its whole version history. Not recoverable.</summary>
    Task PurgeAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);

    Task<SecretMetadata?> ReadMetadataAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);

    /// <summary>Merge-patches only what is supplied; anything null is left as it is.</summary>
    Task WriteMetadataAsync(ProjectId project, EnvironmentId environment, SecretPath path, SecretAnnotations? annotations, SecretRetention? retention, CancellationToken cancellationToken);

    /// <summary>Every secret path beneath a folder, recursively. Backed by OpenBao's SCAN.</summary>
    Task<IReadOnlyList<string>> ScanAsync(ProjectId project, EnvironmentId environment, string? folder, CancellationToken cancellationToken);
}

public interface IOpenBaoTokenAccessor
{
    string GetRequiredToken();
}

public interface IProjectService
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<Project?> GetAsync(ProjectId project, CancellationToken cancellationToken);
    Task<Project> CreateAsync(ProjectId project, string description, CancellationToken cancellationToken);
    Task DeleteAsync(ProjectId project, CancellationToken cancellationToken);

    Task<Project> AddEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        string displayName,
        bool isProtected,
        CancellationToken cancellationToken);

    /// <summary>Any argument left null keeps its current value.</summary>
    Task<Project> UpdateEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        string? displayName,
        bool? isProtected,
        int? position,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refuses while the environment still holds secrets unless purge is set, so a
    /// mis-click cannot quietly take a whole environment with it.
    /// </summary>
    Task<Project> RemoveEnvironmentAsync(
        ProjectId project,
        EnvironmentId environment,
        bool purgeSecrets,
        CancellationToken cancellationToken);
}

public interface IIdentityService
{
    Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken);
    Task CreateAsync(string username, string password, IReadOnlyList<string> policies, CancellationToken cancellationToken);
    Task ResetPasswordAsync(string username, string password, CancellationToken cancellationToken);
    Task SetPoliciesAsync(string username, IReadOnlyList<string> policies, CancellationToken cancellationToken);
    Task DisableAsync(string username, CancellationToken cancellationToken);
    Task DeleteAsync(string username, CancellationToken cancellationToken);
}

/// <summary>
/// Writes the generated policy for a machine identity's runtime scope. Listing roles
/// lives on IAccessRoleService — this used to return nine invented names that matched
/// no real policy.
/// </summary>
public interface IPolicyService
{
    Task CreateRoleAsync(Role role, CancellationToken cancellationToken);
    Task DeleteRoleAsync(string roleName, CancellationToken cancellationToken);
}

/// <summary>Teams, backed by OpenBao identity groups.</summary>
public interface ITeamService
{
    Task<IReadOnlyList<Team>> ListAsync(CancellationToken cancellationToken);
    Task<Team?> GetAsync(string name, CancellationToken cancellationToken);
    Task<Team> CreateAsync(string name, IReadOnlyList<string> roles, CancellationToken cancellationToken);
    Task<Team> SetRolesAsync(string name, IReadOnlyList<string> roles, CancellationToken cancellationToken);
    Task<Team> SetMembersAsync(string name, IReadOnlyList<string> memberEntityIds, CancellationToken cancellationToken);
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// Roles defined by an administrator: a name, a set of environments and a capability
/// set, from which the enforced ACL policy is generated.
/// </summary>
public interface IAccessRoleService
{
    Task<IReadOnlyList<AccessRole>> ListAsync(ProjectId project, CancellationToken cancellationToken);
    Task<AccessRole?> GetAsync(ProjectId project, string name, CancellationToken cancellationToken);
    Task<AccessRole> SaveAsync(AccessRole role, CancellationToken cancellationToken);
    Task DeleteAsync(ProjectId project, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> AssignablePolicyNamesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Changes to a protected environment, held until someone else approves them.
///
/// <para>
/// OpenBao has no approval concept, so this is a workflow this application enforces. Be
/// honest about the limit: a caller with a token who talks to OpenBao directly bypasses
/// it. What OpenBao does still enforce is who can see the proposed values, because they
/// live inside the target project's mount.
/// </para>
/// </summary>
public interface IChangeRequestService
{
    Task<IReadOnlyList<ChangeRequest>> ListAsync(ProjectId project, CancellationToken cancellationToken);
    Task<ChangeRequest?> GetAsync(ProjectId project, string id, CancellationToken cancellationToken);

    /// <summary>The values a pending change would write. Null once it is closed.</summary>
    Task<SecretDocument?> ReadProposedAsync(ChangeRequest request, CancellationToken cancellationToken);

    Task<ChangeRequest> ProposeAsync(
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        IReadOnlyDictionary<string, string> values,
        string? description,
        string? reason,
        int? expectedVersion,
        bool isDeletion,
        string requestedBy,
        CancellationToken cancellationToken);

    /// <summary>Approves and writes the change through. Throws if the reviewer is the requester.</summary>
    Task<ChangeRequest> ApplyAsync(ProjectId project, string id, string reviewer, string? note, CancellationToken cancellationToken);

    Task<ChangeRequest> RejectAsync(ProjectId project, string id, string reviewer, string? note, CancellationToken cancellationToken);

    /// <summary>The requester taking their own change back. Nobody else may.</summary>
    Task<ChangeRequest> WithdrawAsync(ProjectId project, string id, string requester, CancellationToken cancellationToken);
}

/// <summary>
/// Asking for a role on a project you cannot touch yet. Submitting runs with the
/// caller's own token under the member-base grant; approving merges the requested
/// roles into the person's existing access without touching anything else they have.
/// </summary>
public interface IAccessRequestService
{
    Task<IReadOnlyList<AccessRequest>> ListAsync(ProjectId project, CancellationToken cancellationToken);
    Task SubmitAsync(AccessRequest request, CancellationToken cancellationToken);

    /// <summary>Grants the requested roles and closes the request. The requester cannot do this.</summary>
    Task<AccessRequest> ApproveAsync(ProjectId project, string username, string reviewer, CancellationToken cancellationToken);

    Task<AccessRequest> RejectAsync(ProjectId project, string username, string reviewer, CancellationToken cancellationToken);
}

public interface IMachineIdentityService
{
    Task<IReadOnlyList<MachineIdentity>> ListAsync(CancellationToken cancellationToken);
    Task<MachineIdentity> CreateAsync(MachineIdentity identity, CancellationToken cancellationToken);
    Task<string> GenerateSecretIdAsync(string roleId, CancellationToken cancellationToken);
    Task RevokeSecretIdsAsync(string roleId, CancellationToken cancellationToken);
}

/// <summary>
/// The product's own record of who changed what, distinct from OpenBao's audit device.
/// The audit device is for compliance and stays outside this application; this is the
/// feed a developer reads to answer "who rotated that key?".
/// </summary>
public interface IActivityLog
{
    Task RecordAsync(ActivityEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityEntry>> ReadAsync(string project, int days, CancellationToken cancellationToken);
}

/// <summary>
/// What the caller's token may do, straight from OpenBao. Used to decide which
/// affordances to show — never as the authorization decision itself.
/// </summary>
public interface ICapabilityService
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> CapabilitiesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}

public interface IDatabaseCredentialService
{
    Task<DynamicDatabaseCredential> ReadAsync(string role, CancellationToken cancellationToken);
}

/// <summary>
/// One-time share links. OpenBao's response wrapping already does exactly this: it
/// stores a payload behind a single-use token with a TTL, so nothing is stored or
/// encrypted by this application.
/// </summary>
public interface ISecretShareService
{
    Task<(string Token, DateTimeOffset ExpiresAt)> WrapAsync(
        IReadOnlyDictionary<string, string> values,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>Returns null when the token is unknown, already used or expired.</summary>
    Task<IReadOnlyDictionary<string, string>?> UnwrapAsync(string token, CancellationToken cancellationToken);
}

public interface ISessionService
{
    Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}
