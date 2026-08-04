namespace ControlPlane.Domain;

// These implement IParsable so minimal APIs bind them straight from the route and
// answer 400 on bad input, instead of every handler repeating the same try/catch.

public readonly record struct ProjectId : IParsable<ProjectId>
{
    public string Value { get; }

    private ProjectId(string value) => Value = value;

    public static ProjectId Parse(string value) => new(Identifier.ValidateProject(value, nameof(value)));

    public static ProjectId Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out ProjectId result)
    {
        try
        {
            result = Parse(value!);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
    }

    public override string ToString() => Value;
}

public readonly record struct EnvironmentId : IParsable<EnvironmentId>
{
    public string Value { get; }

    private EnvironmentId(string value) => Value = value;

    public static EnvironmentId Parse(string value) => new(Identifier.ValidateEnvironment(value, nameof(value)));

    /// <summary>
    /// Builds a control-plane environment such as "_pending". Parse deliberately rejects
    /// the underscore prefix so nobody can create one of these through the API; this is
    /// the only door in, and it still validates the rest of the segment.
    /// </summary>
    public static EnvironmentId Reserved(string value)
    {
        var identifier = Identifier.ValidateSegment(value, nameof(value));
        if (identifier[0] != Identifier.ReservedPrefix)
        {
            throw new ArgumentException("A reserved environment starts with an underscore.", nameof(value));
        }

        return new EnvironmentId(identifier);
    }

    public static EnvironmentId Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out EnvironmentId result)
    {
        try
        {
            result = Parse(value!);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
    }

    public override string ToString() => Value;
}

public sealed record SecretPath : IParsable<SecretPath>
{
    public string Value { get; }

    private SecretPath(string value) => Value = value;

    public static SecretPath Parse(string? value)
    {
        var segments = (value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => !Identifier.IsValidSegment(segment)))
        {
            throw new ArgumentException("Secret path is invalid.", nameof(value));
        }

        return new SecretPath(string.Join('/', segments));
    }

    public static SecretPath Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out SecretPath result)
    {
        try
        {
            result = Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            result = null!;
            return false;
        }
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
    IReadOnlyList<string> Policies,
    string Username);

public sealed record Project(
    ProjectId Id,
    string Description,
    IReadOnlyList<ProjectEnvironment> Environments);

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

public sealed record DynamicDatabaseCredential(
    string Username,
    string Password,
    string LeaseId,
    DateTimeOffset ExpiresAt);

