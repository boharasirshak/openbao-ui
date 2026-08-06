namespace ControlPlane.Domain;

/// <summary>
/// The names of the policies a project generates, and how to read one back into a
/// human label. The naming used to be retyped in the project service, the role
/// service and the dashboard; a rename drifting in one place would silently strip
/// or widen access, so it lives here once.
/// </summary>
public static class ProjectPolicy
{
    public static string Admin(ProjectId project) => $"{project}-admin";

    public static string Environment(ProjectId project, EnvironmentId environment, bool readOnly) =>
        $"{project}-{environment}-{(readOnly ? "viewer" : "editor")}";

    public static string CustomRolePrefix(ProjectId project) => $"{project}-role-";

    /// <summary>
    /// Whether a policy name is one of this project's own. Environment names can
    /// contain dashes, so this checks against the project's actual environments
    /// rather than guessing from the shape of the string.
    /// </summary>
    // ponytail: a policy for an environment that was deleted later is not recognised
    // and lingers on the user. Harmless — the policy itself was deleted, so it grants
    // nothing — but strip them manually if the list bothers you.
    public static bool BelongsTo(
        ProjectId project,
        IReadOnlyList<ProjectEnvironment> environments,
        string policy) =>
        policy == Admin(project)
        || policy.StartsWith(CustomRolePrefix(project), StringComparison.Ordinal)
        || environments.Any(environment =>
            policy == Environment(project, environment.Id, readOnly: true)
            || policy == Environment(project, environment.Id, readOnly: false));

    /// <summary>Plain words for a policy name, for role pickers and member lists.</summary>
    public static string Label(
        ProjectId project,
        IReadOnlyList<ProjectEnvironment> environments,
        string policy)
    {
        if (policy == Admin(project))
        {
            return "Project admin";
        }

        if (policy.StartsWith(CustomRolePrefix(project), StringComparison.Ordinal))
        {
            return policy[CustomRolePrefix(project).Length..];
        }

        foreach (var environment in environments)
        {
            if (policy == Environment(project, environment.Id, readOnly: true))
            {
                return $"{environment.DisplayName} · read";
            }

            if (policy == Environment(project, environment.Id, readOnly: false))
            {
                return $"{environment.DisplayName} · edit";
            }
        }

        return policy;
    }
}
