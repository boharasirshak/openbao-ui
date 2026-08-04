namespace ControlPlane.Domain;

/// <summary>
/// Owns the KV-v2 path layout: a project is the mount, the environment is the first
/// path segment beneath the operation. Paths are logical — callers prepend the API
/// version — so the same builder serves both HTTP requests and ACL policy patterns.
/// </summary>
public readonly record struct SecretLocation(ProjectId Project, EnvironmentId Environment, SecretPath Path)
{
    public string Data => $"{DataPrefix(Project.Value, Environment.Value)}/{Path}";
    public string Metadata => $"{MetadataPrefix(Project.Value, Environment.Value)}/{Path}";
    public string Subkeys => $"{Project}/subkeys/{Environment}/{Path}";
    public string Delete => $"{Project}/delete/{Environment}/{Path}";
    public string Undelete => $"{Project}/undelete/{Environment}/{Path}";
    public string Destroy => $"{Project}/destroy/{Environment}/{Path}";

    /// <summary>
    /// Prefixes take strings rather than the value objects because ACL policies use
    /// "*" as a wildcard in both positions, which is not a valid identifier.
    /// </summary>
    public static string DataPrefix(string project, string environment) =>
        $"{project}/data/{environment}";

    public static string MetadataPrefix(string project, string environment) =>
        $"{project}/metadata/{environment}";
}

/// <summary>
/// A folder is addressed by its metadata path, which is what KV-v2 lists and scans.
/// A null folder is the environment root.
/// </summary>
public readonly record struct FolderLocation(ProjectId Project, EnvironmentId Environment, SecretPath? Folder)
{
    public string Metadata
    {
        get
        {
            var prefix = SecretLocation.MetadataPrefix(Project.Value, Environment.Value);
            return Folder is null ? prefix : $"{prefix}/{Folder}";
        }
    }
}
