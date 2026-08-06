namespace ControlPlane.Contracts;

public sealed record LoginRequest(string Username, string Password);
public sealed record SessionResponse(
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Policies = null,
    string? Username = null);
public sealed record CsrfTokenResponse(string Token);

public sealed record ProjectResponse(string Id, string Description, IReadOnlyList<EnvironmentResponse> Environments);
public sealed record CreateProjectRequest(string Description);
public sealed record MemberResponse(string Username, string EntityId, bool Disabled, IReadOnlyList<string> Policies);
public sealed record CreateMemberRequest(string Username, string Password, IReadOnlyList<string> Policies);
public sealed record UpdateMemberRequest(string Password, IReadOnlyList<string> Policies);
public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);
public sealed record RoleResponse(string Name, string Project, string Environment, bool ReadOnly);
public sealed record CreateRoleRequest(string Name, string Project, string Environment, bool ReadOnly);
public sealed record MachineIdentityResponse(string Name, string RoleId, string Project, string Environment, bool ReadOnly, int? TokenTtlSeconds, int? TokenUses);
public sealed record CreateMachineIdentityRequest(string Name, string Project, string Environment, bool ReadOnly, int? TokenTtlSeconds, int? TokenUses);
public sealed record SecretIdResponse(string SecretId);
public sealed record ActivityEntryResponse(
    DateTimeOffset At,
    string Actor,
    string Action,
    string Project,
    string? Environment,
    string? Path,
    IReadOnlyList<string> KeysAffected,
    int? Version);

/* ---------- effective permissions ---------- */

public sealed record PermissionQuery(string Project, string? Environment);
public sealed record PermissionResult(
    string Project,
    string? Environment,
    bool CanRead,
    bool CanWrite,
    bool CanDelete);
public sealed record PermissionsRequest(IReadOnlyList<PermissionQuery> Resources);
public sealed record PermissionsResponse(IReadOnlyList<PermissionResult> Results);

/* ---------- environments ---------- */

public sealed record EnvironmentResponse(string Id, string DisplayName, bool Protected, int Position);
public sealed record CreateEnvironmentRequest(string Id, string DisplayName, bool Protected);
public sealed record UpdateEnvironmentRequest(string? DisplayName, bool? Protected, int? Position);

/* ---------- teams ---------- */

public sealed record TeamResponse(
    string Name,
    string Id,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> MemberEntityIds);
public sealed record CreateTeamRequest(string Name, IReadOnlyList<string> Roles);
public sealed record SetTeamRolesRequest(IReadOnlyList<string> Roles);
public sealed record SetTeamMembersRequest(IReadOnlyList<string> MemberEntityIds);

/* ---------- custom project roles ---------- */

/// <param name="Describe">See that a secret exists, its keys, tags and history — not its values.</param>
/// <param name="ReadValues">See the values themselves.</param>
public sealed record RolePermissionsPayload(
    bool Describe,
    bool ReadValues,
    bool WriteSecrets,
    bool DeleteSecrets,
    bool ManageDetails,
    bool RollBack,
    bool Destroy);

public sealed record AccessRoleResponse(
    string Name,
    string Project,
    string PolicyName,
    IReadOnlyList<string> Environments,
    RolePermissionsPayload Permissions,
    string? Description);

public sealed record SaveAccessRoleRequest(
    IReadOnlyList<string> Environments,
    RolePermissionsPayload Permissions,
    string? Description);

public sealed record AssignablePoliciesResponse(IReadOnlyList<string> Policies);

/* ---------- per-project members ---------- */

public sealed record ProjectRoleOption(string Policy, string Label);
public sealed record ProjectMemberResponse(
    string Username,
    bool Disabled,
    IReadOnlyList<ProjectRoleOption> Roles);
public sealed record ProjectMemberOptions(
    IReadOnlyList<string> Users,
    IReadOnlyList<ProjectRoleOption> Roles);
public sealed record SetProjectRolesRequest(IReadOnlyList<string> Policies);

/* ---------- access requests ---------- */

public sealed record AccessRequestResponse(
    string Username,
    IReadOnlyList<ProjectRoleOption> Roles,
    string? Reason,
    DateTimeOffset RequestedAt,
    string Status,
    string? ReviewedBy);
public sealed record CreateAccessRequest(IReadOnlyList<string> Policies, string? Reason);
public sealed record AccessRequestOptions(IReadOnlyList<ProjectRoleOption> Roles);
