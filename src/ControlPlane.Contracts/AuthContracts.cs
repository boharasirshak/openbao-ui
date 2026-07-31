namespace ControlPlane.Contracts;

public sealed record LoginRequest(string Username, string Password);
public sealed record SessionResponse(DateTimeOffset ExpiresAt);

public sealed record ProjectResponse(string Id, string Description, IReadOnlyList<string> Environments);
public sealed record CreateProjectRequest(string Description);
public sealed record MemberResponse(string Username, string EntityId, bool Disabled, IReadOnlyList<string> Policies);
public sealed record CreateMemberRequest(string Username, string Password, IReadOnlyList<string> Policies);
public sealed record RoleResponse(string Name, string Project, string Environment, bool ReadOnly);
public sealed record CreateRoleRequest(string Name, string Project, string Environment, bool ReadOnly);
public sealed record MachineIdentityResponse(string Name, string RoleId, string Project, string Environment, int? TokenTtlSeconds, int? TokenUses);
public sealed record CreateMachineIdentityRequest(string Name, string Project, string Environment, bool ReadOnly, int? TokenTtlSeconds, int? TokenUses);
