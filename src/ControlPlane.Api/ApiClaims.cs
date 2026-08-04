namespace ControlPlane.Api;

/// <summary>
/// Claim names carried on the session cookie. The OpenBao token lives here rather than
/// in a server-side store, so the cookie is the whole session.
/// </summary>
public static class ApiClaims
{
    public const string OpenBaoToken = "openbao_token";
    public const string OpenBaoExpiration = "openbao_expires_at";
    public const string OpenBaoPolicy = "openbao_policy";

    /// <summary>Who this is. Needed to attribute activity and to stop self-approval.</summary>
    public const string Username = "openbao_username";

    /// <summary>Authorization policy gating the organisation-wide endpoints.</summary>
    public const string AdminPolicy = "wrapper-admin";
}
