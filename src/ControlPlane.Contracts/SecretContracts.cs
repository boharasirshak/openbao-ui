namespace ControlPlane.Contracts;

public sealed record SecretDocumentRequest(
    IReadOnlyDictionary<string, string> Values,
    int? ExpectedVersion,
    string? Description = null);
public sealed record SecretDocumentResponse(
    IReadOnlyDictionary<string, string> Values,
    int Version,
    string? Description = null);
public sealed record SecretEntryResponse(string Name, bool IsFolder);
public sealed record SecretVersionResponse(int Version, DateTimeOffset? DeletedAt, bool Destroyed);
public sealed record ImportSecretsRequest(
    IReadOnlyDictionary<string, string> Values,
    int? ExpectedVersion,
    string? Description = null);
public sealed record SecretVersionRequest(int Version);
public sealed record DatabaseCredentialResponse(string Username, string Password, string LeaseId, DateTimeOffset ExpiresAt);

/* ---------- annotations and retention ---------- */

public sealed record SecretAnnotationsPayload(
    string? Description,
    IReadOnlyList<string>? Tags,
    string? Comment);

public sealed record SecretRetentionPayload(int? MaxVersions, int? DeleteVersionAfterSeconds);

public sealed record SecretMetadataResponse(
    SecretAnnotationsPayload Annotations,
    SecretRetentionPayload Retention,
    int CurrentVersion,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SecretVersionResponse> Versions);

public sealed record UpdateSecretMetadataRequest(
    SecretAnnotationsPayload? Annotations,
    SecretRetentionPayload? Retention);

public sealed record DestroyVersionsRequest(IReadOnlyList<int> Versions);

/* ---------- search and compare ---------- */

public sealed record SecretSearchHit(string Environment, string Path);
public sealed record SecretSearchResponse(IReadOnlyList<SecretSearchHit> Hits, bool Truncated);

/// <summary>
/// One environment's view of a path. Values are only present when the caller may read
/// them; Accessible is false when OpenBao refused, and Exists is false when there is
/// simply no document there.
/// </summary>
public sealed record EnvironmentSnapshot(
    string Environment,
    bool Accessible,
    bool Exists,
    int Version,
    IReadOnlyDictionary<string, string> Values);

public sealed record CompareResponse(string Path, IReadOnlyList<EnvironmentSnapshot> Environments);

/* ---------- folders ---------- */

public sealed record MoveFolderRequest(string Destination);
public sealed record FolderOperationResponse(int SecretsAffected);

/* ---------- change requests ---------- */

public sealed record ChangeReviewPayload(string Reviewer, bool Approved, DateTimeOffset At, string? Note);

/// <summary>
/// Deliberately carries key names and not values. Values are fetched separately, so a
/// reviewer who may not read the environment still sees what a change touches.
/// </summary>
public sealed record ChangeRequestResponse(
    string Id,
    string Project,
    string Environment,
    string Path,
    IReadOnlyList<string> Keys,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    string Status,
    string? Reason,
    int? ExpectedVersion,
    IReadOnlyList<ChangeReviewPayload> Reviews,
    bool CanReview,
    bool IsDeletion);

public sealed record CreateChangeRequest(
    string Environment,
    string Path,
    IReadOnlyDictionary<string, string> Values,
    string? Reason,
    int? ExpectedVersion,
    string? Description = null,
    bool Delete = false);

public sealed record ReviewChangeRequest(string? Note);

/* ---------- one-time share links ---------- */

public sealed record CreateShareRequest(IReadOnlyDictionary<string, string> Values, int TtlSeconds);
public sealed record CreateShareResponse(string Token, DateTimeOffset ExpiresAt);
public sealed record ShareResponse(IReadOnlyDictionary<string, string> Values);
