namespace ControlPlane.Domain;

public enum ChangeRequestStatus
{
    Pending,
    Applied,
    Rejected,
    Withdrawn,
}

public sealed record ChangeReview(string Reviewer, bool Approved, DateTimeOffset At, string? Note);

/// <summary>
/// A proposed write to a protected environment, waiting on someone else's approval.
///
/// <para>
/// The proposed values are a production secret in their own right, so they are never
/// stored in the control plane's own mount. They live inside the target project's mount
/// under the reserved <c>_pending</c> prefix, which means the project's existing ACL
/// governs who can read them — exactly the same protection as the secret they will
/// become. Only this non-secret envelope is kept alongside the other app state.
/// </para>
///
/// <para>
/// Honest limitation: protection is enforced by this API, not by OpenBao. A project
/// admin talking to OpenBao directly can still write to a protected environment. This is
/// a review workflow, not an ACL boundary — OpenBao has no approval concept to lean on.
/// </para>
/// </summary>
public sealed record ChangeRequest(
    string Id,
    ProjectId Project,
    EnvironmentId Environment,
    SecretPath Path,
    IReadOnlyList<string> KeysAffected,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    ChangeRequestStatus Status,
    string? Reason,
    int? ExpectedVersion,
    IReadOnlyList<ChangeReview> Reviews,
    bool IsDeletion = false)
{
    public const string PendingEnvironment = "_pending";

    public static string NewId(DateTimeOffset at) =>
        $"{at.UtcDateTime.Ticks:D19}-{Guid.NewGuid():N}"[..32];

    /// <summary>
    /// Where the proposed values sit: under the reserved environment, keyed by the
    /// environment they are destined for so the ACL can be scoped per environment.
    /// </summary>
    public SecretPath PendingPath => SecretPath.Parse($"{Environment}/{Id}");

    public bool IsOpen => Status == ChangeRequestStatus.Pending;

    /// <summary>
    /// A deletion has no proposed values to store, so nothing is written under _pending
    /// and there is nothing for a reviewer to fetch.
    /// </summary>
    public bool HasProposedValues => !IsDeletion;

    /// <summary>
    /// Approving your own change would defeat the point, so the requester is excluded
    /// even when they otherwise hold the authority to apply it.
    /// </summary>
    public bool CanBeReviewedBy(string reviewer) =>
        IsOpen && !string.Equals(reviewer, RequestedBy, StringComparison.OrdinalIgnoreCase);
}
