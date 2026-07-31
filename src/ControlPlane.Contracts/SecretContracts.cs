namespace ControlPlane.Contracts;

public sealed record SecretDocumentRequest(
    IReadOnlyDictionary<string, string> Values,
    int? ExpectedVersion,
    string? Description = null);
public sealed record SecretDocumentResponse(
    IReadOnlyDictionary<string, string> Values,
    int Version,
    string? Description = null);
public sealed record SecretKeysResponse(IReadOnlyList<string> Keys, int Version);
public sealed record SecretVersionResponse(int Version, DateTimeOffset? DeletedAt, bool Destroyed);
public sealed record ImportSecretsRequest(
    IReadOnlyDictionary<string, string> Values,
    int? ExpectedVersion,
    string? Description = null);
public sealed record SecretVersionRequest(int Version);
public sealed record DatabaseCredentialResponse(string Username, string Password, string LeaseId, DateTimeOffset ExpiresAt);
