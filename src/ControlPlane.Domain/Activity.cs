namespace ControlPlane.Domain;

public enum ActivityAction
{
    SecretSaved,
    SecretImported,
    SecretDeleted,
    SecretRestored,
    SecretUndeleted,
    VersionDestroyed,
    SecretPurged,
    SecretShared,
    FolderDeleted,
    ChangeProposed,
    ChangeApplied,
    ChangeRejected,
    ChangeWithdrawn,
    AccessChanged,
}

/// <summary>
/// A product-level record of who changed what. Deliberately not the OpenBao audit
/// device: that says "thorneai/data/development/backend", this says "Alice rotated
/// DATABASE_URL in production".
///
/// <para>
/// KeysAffected holds key <em>names</em> only. A value must never reach this record —
/// the activity feed is readable by anyone who can read the project.
/// </para>
/// </summary>
public sealed record ActivityEntry(
    DateTimeOffset At,
    string Actor,
    ActivityAction Action,
    string Project,
    string? Environment,
    string? Path,
    IReadOnlyList<string> KeysAffected,
    int? Version);

public static class ActivityId
{
    /// <summary>
    /// Sortable by time, so a prefix listing comes back in order, with random bits so
    /// two writes in the same tick cannot collide.
    /// </summary>
    public static string New(DateTimeOffset at) =>
        $"{at.UtcDateTime.Ticks:D19}-{Guid.NewGuid():N}"[..32];

    public static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd");
}
