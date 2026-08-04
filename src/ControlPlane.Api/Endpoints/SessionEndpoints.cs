using System.Security.Claims;
using ControlPlane.Application;
using ControlPlane.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace ControlPlane.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/auth/csrf",
                (IAntiforgery antiforgery, HttpContext context) =>
                    Results.Ok(new CsrfTokenResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .Produces<CsrfTokenResponse>();

        app.MapPost(
                "/api/auth/login",
                async (
                    LoginRequest request,
                    ISessionService sessions,
                    HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                    {
                        return Results.BadRequest();
                    }

                    try
                    {
                        var session = await sessions.LoginAsync(request.Username, request.Password, cancellationToken);
                        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
                        identity.AddClaim(new Claim(ApiClaims.OpenBaoToken, session.Token));
                        foreach (var policy in session.Policies)
                        {
                            identity.AddClaim(new Claim(ApiClaims.OpenBaoPolicy, policy));
                        }

                        identity.AddClaim(new Claim(
                            ApiClaims.OpenBaoExpiration,
                            session.ExpiresAt.ToUnixTimeSeconds().ToString()));
                        identity.AddClaim(new Claim(ApiClaims.Username, session.Username));
                        identity.AddClaim(new Claim(ClaimTypes.Name, session.Username));

                        await context.SignInAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            new ClaimsPrincipal(identity),
                            new AuthenticationProperties
                            {
                                ExpiresUtc = session.ExpiresAt,
                                IsPersistent = false,
                            });

                        return Results.Ok(new SessionResponse(session.ExpiresAt, session.Policies, session.Username));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return Results.Unauthorized();
                    }
                })
            .RequireRateLimiting("login")
            .Produces<SessionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost(
                "/api/auth/logout",
                async (ISessionService sessions, HttpContext context, CancellationToken cancellationToken) =>
                {
                    var token = context.User.FindFirstValue(ApiClaims.OpenBaoToken);
                    if (token is not null)
                    {
                        await sessions.RevokeAsync(token, cancellationToken);
                    }

                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return Results.NoContent();
                })
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet(
                "/api/auth/session",
                (HttpContext context) =>
                {
                    var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
                        long.Parse(context.User.FindFirstValue(ApiClaims.OpenBaoExpiration)!));

                    // Return the policies the login response already sends, so a page
                    // refresh does not silently drop administrative access in the UI.
                    var policies = context.User.FindAll(ApiClaims.OpenBaoPolicy)
                        .Select(claim => claim.Value)
                        .ToList();

                    return Results.Ok(new SessionResponse(
                        expiresAt,
                        policies,
                        context.User.FindFirstValue(ApiClaims.Username)));
                })
            .RequireAuthorization()
            .Produces<SessionResponse>();
    }
}
