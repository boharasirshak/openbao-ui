using System.Net;
using System.Net.Http.Json;
using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ControlPlane.IntegrationTests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task Login_requires_csrf_and_returns_a_secure_http_only_session_cookie()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionService>();
                services.AddSingleton<ISessionService, FakeSessionService>();
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        var csrfCookie = Assert.Single(
            csrfResponse.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", csrfCookie, StringComparison.OrdinalIgnoreCase);

        using var withoutCsrf = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("alice", "password"));
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("alice", "password")),
        };
        login.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        using var response = await client.SendAsync(login);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var sessionCookie = Assert.Single(cookies, cookie => cookie.StartsWith("openbao_session="));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);

        using var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        // Signed in, but without the administrator policy. This has to be a 403 and not
        // cookie auth's default redirect: the dashboard decides whether to show a member
        // fallback by reading the status, and a 302 to a login page reads as success. The
        // whole member experience was silently blank because of it.
        using var forbidden = await client.GetAsync("/api/admin/teams");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    /// <summary>An unauthenticated API call answers 401, never a redirect.</summary>
    [Fact]
    public async Task Unauthenticated_api_calls_answer_401_rather_than_redirecting()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CsrfResponse(string Token);

    private sealed class FakeSessionService : ISessionService
    {
        public Task<OpenBaoSession> LoginAsync(string username, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenBaoSession(
                "fake-token",
                "fake-accessor",
                DateTimeOffset.UtcNow.AddMinutes(5),
                ["default"],
                username));

        public Task RevokeAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
