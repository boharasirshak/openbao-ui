namespace ControlPlane.Domain;

/// <summary>
/// A team. Backed by an OpenBao identity group, so membership is enforced by OpenBao:
/// a member of the group inherits its roles on their next login, and this application
/// never has to evaluate team membership itself.
/// </summary>
public sealed record Team(
    string Name,
    string Id,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> MemberEntityIds);

/// <summary>
/// What a role may do. Each flag maps onto specific OpenBao capabilities rather than a
/// vague tier, so a role can be exactly as narrow as intended.
///
/// <para>
/// <see cref="Describe"/> and <see cref="ReadValues"/> are deliberately separate. KV-v2
/// keeps a secret's existence, keys, tags and version history on the metadata path and
/// the values themselves on the data path, so "you may see that this exists and when it
/// last changed, but not what it is" is a real, enforceable grant rather than a
/// convention. It is the narrowest useful level of access and the right default for
/// auditors and on-call responders.
/// </para>
/// </summary>
public sealed record RolePermissions(
    bool Describe = false,
    bool ReadValues = false,
    bool WriteSecrets = false,
    bool DeleteSecrets = false,
    bool ManageDetails = false,
    bool RollBack = false,
    bool Destroy = false)
{
    /// <summary>Existence, keys, tags and history — never a value.</summary>
    public static readonly RolePermissions Auditor = new(Describe: true);

    public static readonly RolePermissions Viewer = new(Describe: true, ReadValues: true);

    public static readonly RolePermissions Editor = new(
        Describe: true,
        ReadValues: true,
        WriteSecrets: true,
        DeleteSecrets: true,
        ManageDetails: true,
        RollBack: true);

    public static readonly RolePermissions Owner = Editor with { Destroy = true };

    public bool GrantsNothing =>
        !Describe && !ReadValues && !WriteSecrets && !DeleteSecrets && !ManageDetails
        && !RollBack && !Destroy;
}

/// <summary>
/// A role scoped to one project and a chosen set of its environments. The generated ACL
/// policy is derived from this, and the definition is stored so it can be edited later —
/// a policy document alone cannot be reliably read back into checkboxes.
/// </summary>
public sealed record AccessRole(
    string Name,
    ProjectId Project,
    IReadOnlyList<EnvironmentId> Environments,
    RolePermissions Permissions,
    string? Description = null)
{
    /// <summary>The ACL policy name. Prefixed so a role cannot shadow a system policy.</summary>
    public string PolicyName => $"{Project}-role-{Name}";

    /// <summary>
    /// Builds the policy. Capabilities are grouped by path because KV-v2 splits data
    /// and metadata: reading values needs the data path, history needs metadata, and
    /// destroy is a path of its own.
    /// </summary>
    /// <param name="controlMount">
    /// Where the control plane keeps its own records. Needed because the API reads them
    /// with the caller's token, so a role that cannot read them cannot save a secret.
    /// </param>
    public string ToPolicy(string controlMount)
    {
        var lines = new List<string>();

        // Everyone needs the project record: it holds which environments are protected.
        lines.Add(Path(ControlPlanePaths.ProjectRecord(controlMount, Project.Value), ["read"]));
        lines.Add(Path(ControlPlanePaths.ProjectRecordMetadata(controlMount, Project.Value), ["read"]));

        var changeCapabilities = Permissions.WriteSecrets
            ? new[] { "create", "read", "update", "list" }
            : ["read", "list"];
        lines.Add(Path($"{ControlPlanePaths.Changes(controlMount, Project.Value)}/*", changeCapabilities));
        lines.Add(Path(ControlPlanePaths.ChangesMetadata(controlMount, Project.Value), ["read", "list"]));
        lines.Add(Path($"{ControlPlanePaths.ChangesMetadata(controlMount, Project.Value)}/*", ["read", "list"]));

        // Append-only by design: "create" without "update" is what stops an entry being
        // rewritten later. Anyone who can change a secret must be able to record it, or
        // the feed quietly misses exactly the changes that matter.
        lines.Add(Path(
            $"{ControlPlanePaths.Activity(controlMount, Project.Value)}/*",
            Permissions.WriteSecrets ? ["create", "read", "list"] : ["read", "list"]));
        lines.Add(Path(
            $"{ControlPlanePaths.ActivityMetadata(controlMount, Project.Value)}/*",
            ["read", "list", "scan"]));

        foreach (var environment in Environments)
        {
            var data = SecretLocation.DataPrefix(Project.Value, environment.Value);
            var metadata = SecretLocation.MetadataPrefix(Project.Value, environment.Value);

            // Values live on the data path. Nothing here is granted by Describe alone,
            // which is what makes "see it exists but not its value" enforceable.
            var dataCapabilities = new List<string>();
            if (Permissions.ReadValues) dataCapabilities.Add("read");
            if (Permissions.WriteSecrets) dataCapabilities.AddRange(["create", "update", "patch"]);
            if (Permissions.DeleteSecrets) dataCapabilities.Add("delete");
            // Rolling back reads a historical version and writes it back.
            if (Permissions.RollBack) dataCapabilities.AddRange(["read", "create", "update"]);
            if (dataCapabilities.Count > 0)
            {
                lines.Add(Path($"{data}/*", [..dataCapabilities, "list"]));
            }

            // Existence, keys, tags and history live on the metadata path.
            var metadataCapabilities = new List<string>();
            if (Permissions.Describe || Permissions.ReadValues)
            {
                metadataCapabilities.AddRange(["read", "list", "scan"]);
            }

            if (Permissions.ManageDetails) metadataCapabilities.AddRange(["create", "update", "patch"]);
            if (Permissions.Destroy) metadataCapabilities.Add("delete");
            if (metadataCapabilities.Count > 0)
            {
                lines.Add(Path($"{metadata}/*", metadataCapabilities));
            }

            // Version-level operations each live on their own KV-v2 path.
            if (Permissions.DeleteSecrets)
            {
                lines.Add(Path($"{Project}/delete/{environment}/*", ["update"]));
            }

            if (Permissions.RollBack)
            {
                lines.Add(Path($"{Project}/undelete/{environment}/*", ["update"]));
            }

            if (Permissions.Destroy)
            {
                lines.Add(Path($"{Project}/destroy/{environment}/*", ["update"]));
            }

            // A change awaiting approval holds the same values as the secret it will
            // become, so it is stored inside this project's mount under _pending and
            // covered by the same grant. Anyone who can write this environment can
            // propose a change to it and review someone else's.
            if (Permissions.WriteSecrets)
            {
                var pending = $"{ChangeRequest.PendingEnvironment}/{environment}";
                lines.Add(Path(
                    $"{SecretLocation.DataPrefix(Project.Value, pending)}/*",
                    ["create", "read", "update", "delete", "list"]));
                lines.Add(Path(
                    $"{SecretLocation.MetadataPrefix(Project.Value, pending)}/*",
                    ["create", "read", "update", "delete", "list"]));
                lines.Add(Path($"{Project}/delete/{pending}/*", ["update"]));
            }
        }

        return string.Join('\n', lines);
    }

    private static string Path(string path, IEnumerable<string> capabilities)
    {
        var unique = capabilities.Distinct().Order().Select(capability => $"\"{capability}\"");
        return $"path \"{path}\" {{ capabilities = [{string.Join(", ", unique)}] }}";
    }
}
