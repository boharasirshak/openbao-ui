namespace ControlPlane.Domain;

public readonly record struct ProjectId
{
    public string Value { get; }

    private ProjectId(string value) => Value = value;

    public static ProjectId Parse(string value) => new(IdentifierValidation.ValidateProject(value));

    public override string ToString() => Value;
}

public readonly record struct EnvironmentId
{
    public string Value { get; }

    private EnvironmentId(string value) => Value = value;

    public static EnvironmentId Parse(string value) => new(IdentifierValidation.ValidateIdentifier(value, nameof(value)));

    public override string ToString() => Value;
}

public sealed record SecretPath
{
    public string Value { get; }

    private SecretPath(string value) => Value = value;

    public static SecretPath Parse(string? value)
    {
        var segments = (value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => !IdentifierValidation.IsValidSegment(segment)))
        {
            throw new ArgumentException("Secret path is invalid.", nameof(value));
        }

        return new SecretPath(string.Join('/', segments));
    }

    public override string ToString() => Value;
}

public sealed record SecretDocument(
    IReadOnlyDictionary<string, string> Values,
    int Version,
    string? Description = null);
public sealed record SecretVersion(int Version, DateTimeOffset? DeletedAt, bool Destroyed);
public sealed record SecretEntry(string Name, bool IsFolder);
public sealed record OpenBaoSession(
    string Token,
    string Accessor,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Policies);

public sealed record Project(
    ProjectId Id,
    string Description,
    IReadOnlyList<EnvironmentId> Environments);

public sealed record Member(
    string Username,
    string EntityId,
    bool Disabled,
    IReadOnlyList<string> Policies);

public sealed record Role(string Name, string Project, string Environment, bool ReadOnly);

public sealed record MachineIdentity(
    string Name,
    string RoleId,
    string Project,
    string Environment,
    bool ReadOnly,
    int? TokenTtlSeconds,
    int? TokenUses);

public sealed record AuditEvent(
    DateTimeOffset? Time,
    string Type,
    string Operation,
    string Path,
    string Actor);

public sealed record DynamicDatabaseCredential(
    string Username,
    string Password,
    string LeaseId,
    DateTimeOffset ExpiresAt);

file static class IdentifierValidation
{
    private static readonly string[] ReservedProjectNames =
    [
        "auth",
        "identity",
        "sys",
        "wrapper-metadata",
    ];

    public static string ValidateProject(string? value)
    {
        var identifier = ValidateIdentifier(value, nameof(value));
        if (ReservedProjectNames.Contains(identifier, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Project name is reserved.", nameof(value));
        }

        return identifier;
    }

    public static string ValidateIdentifier(string? value, string parameterName)
    {
        if (!IsValidSegment(value))
        {
            throw new ArgumentException("Identifier is invalid.", parameterName);
        }

        return value!;
    }

    public static bool IsValidSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}
