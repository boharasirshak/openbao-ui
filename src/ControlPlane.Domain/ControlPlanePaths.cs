namespace ControlPlane.Domain;

/// <summary>
/// The control plane keeps its own state — project records, change requests — in a KV
/// mount of its own, and reads it with the caller's token rather than a privileged one.
/// That means the generated policies have to grant access to these paths, so the paths
/// live here: if the policy and the code that reads it drift apart, the symptom is a 403
/// on an ordinary save.
/// </summary>
public static class ControlPlanePaths
{
    public static string ProjectRecord(string mount, string project) =>
        $"{mount}/data/projects/{project}";

    public static string ProjectRecordMetadata(string mount, string project) =>
        $"{mount}/metadata/projects/{project}";

    public static string Changes(string mount, string project) =>
        $"{mount}/data/changes/{project}";

    /// <summary>Listing change requests is a LIST on the metadata path, not the data one.</summary>
    public static string ChangesMetadata(string mount, string project) =>
        $"{mount}/metadata/changes/{project}";

    public static string Activity(string mount, string project) =>
        $"{mount}/data/activity/{project}";

    public static string ActivityMetadata(string mount, string project) =>
        $"{mount}/metadata/activity/{project}";
}
