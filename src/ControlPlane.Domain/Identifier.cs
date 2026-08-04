namespace ControlPlane.Domain;

/// <summary>
/// The single place identifier rules live. These were previously file-scoped inside
/// Secrets.cs, which is why the same rule ended up retyped in the API, the CLI and the
/// database credential service. Every caller now shares one definition.
/// </summary>
public static class Identifier
{
    /// <summary>
    /// Mount names OpenBao owns itself, plus the control plane's own metadata mount.
    /// A project is a KV mount, so these can never be project names.
    /// </summary>
    private static readonly string[] ReservedProjectNames =
    [
        "auth",
        "cubbyhole",
        "identity",
        "sys",
        "wrapper-metadata",
    ];

    /// <summary>
    /// A leading underscore is reserved for control-plane paths that sit in the
    /// environment position, such as pending changes awaiting approval. Reserving it
    /// keeps those paths impossible to collide with a real environment.
    /// </summary>
    public const char ReservedPrefix = '_';

    public static string ValidateProject(string? value, string parameterName = "value")
    {
        var identifier = ValidateSegment(value, parameterName);
        if (ReservedProjectNames.Contains(identifier, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Project name is reserved.", parameterName);
        }

        return identifier;
    }

    public static string ValidateEnvironment(string? value, string parameterName = "value")
    {
        var identifier = ValidateSegment(value, parameterName);
        if (identifier[0] == ReservedPrefix)
        {
            throw new ArgumentException("Environment names cannot start with an underscore.", parameterName);
        }

        return identifier;
    }

    public static string ValidateSegment(string? value, string parameterName = "value")
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

    /// <summary>
    /// Keys become environment variables, so they follow the shell rule: a letter or
    /// underscore first, then letters, digits and underscores.
    /// </summary>
    public static bool IsValidSecretKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && (char.IsLetter(key[0]) || key[0] == '_')
        && key.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}
