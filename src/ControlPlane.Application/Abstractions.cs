using ControlPlane.Domain;

namespace ControlPlane.Application;

public interface ISecretsEngine
{
    Task<SecretDocument?> ReadAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task WriteAsync(ProjectId project, EnvironmentId environment, SecretPath path, SecretDocument document, int? expectedVersion, CancellationToken cancellationToken);
    Task DeleteAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(ProjectId project, EnvironmentId environment, SecretPath path, CancellationToken cancellationToken);
    Task RestoreAsync(ProjectId project, EnvironmentId environment, SecretPath path, int version, CancellationToken cancellationToken);
    Task UndeleteAsync(ProjectId project, EnvironmentId environment, SecretPath path, int version, CancellationToken cancellationToken);
}

public interface IOpenBaoTokenAccessor
{
    string GetRequiredToken();
}

public interface IProjectService
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<Project> CreateAsync(ProjectId project, string description, CancellationToken cancellationToken);
    Task DeleteAsync(ProjectId project, CancellationToken cancellationToken);
}

public interface IIdentityService
{
    Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken);
    Task CreateAsync(string username, string password, IReadOnlyList<string> policies, CancellationToken cancellationToken);
    Task DisableAsync(string username, CancellationToken cancellationToken);
    Task DeleteAsync(string username, CancellationToken cancellationToken);
}

public interface IPolicyService
{
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);
    Task CreateRoleAsync(Role role, CancellationToken cancellationToken);
    Task DeleteRoleAsync(string roleName, CancellationToken cancellationToken);
}

public interface IMachineIdentityService
{
    Task<IReadOnlyList<MachineIdentity>> ListAsync(CancellationToken cancellationToken);
    Task<MachineIdentity> CreateAsync(MachineIdentity identity, CancellationToken cancellationToken);
    Task<string> GenerateSecretIdAsync(string roleId, CancellationToken cancellationToken);
    Task RevokeSecretIdsAsync(string roleId, CancellationToken cancellationToken);
}

public interface IAuditService { }
public interface IOpenBaoSystemClient { }

public interface ISessionService
{
    Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}
