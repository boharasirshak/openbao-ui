namespace ControlPlane.Domain;

/// <summary>
/// An environment inside a project. The id is the path segment secrets live under;
/// the display name is only ever shown to people.
/// </summary>
/// <param name="Protected">
/// Changes to a protected environment go through approval instead of landing straight
/// away. Production is protected by default.
/// </param>
public sealed record ProjectEnvironment(
    EnvironmentId Id,
    string DisplayName,
    bool Protected = false,
    int Position = 0)
{
    public static ProjectEnvironment Default(string id, int position, bool isProtected = false) =>
        new(EnvironmentId.Parse(id), Capitalise(id), isProtected, position);

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
