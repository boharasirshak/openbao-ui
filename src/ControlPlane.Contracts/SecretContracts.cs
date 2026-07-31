namespace ControlPlane.Contracts;

public sealed record SecretDocumentRequest(IReadOnlyDictionary<string, string> Values, int? ExpectedVersion);
public sealed record SecretDocumentResponse(IReadOnlyDictionary<string, string> Values, int Version);
public sealed record SecretKeysResponse(IReadOnlyList<string> Keys, int Version);
public sealed record SecretVersionResponse(int Version, DateTimeOffset? DeletedAt, bool Destroyed);
public sealed record ImportSecretsRequest(IReadOnlyDictionary<string, string> Values, int? ExpectedVersion);
public sealed record SecretVersionRequest(int Version);
