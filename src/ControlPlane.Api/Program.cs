using System.Security.Claims;
using System.Threading.RateLimiting;
using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Infrastructure.OpenBao;
using ControlPlane.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

const string OpenBaoTokenClaim = "openbao_token";
const string OpenBaoExpirationClaim = "openbao_expires_at";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<OpenBaoOptions>(
    builder.Configuration.GetSection(OpenBaoOptions.SectionName));
builder.Services
    .AddHttpClient<ISessionService, OpenBaoSessionService>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<OpenBaoOptions>>().Value;
        client.BaseAddress = options.Address;
    })
    .AddStandardResilienceHandler();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOpenBaoTokenAccessor, HttpContextOpenBaoTokenAccessor>();
builder.Services
    .AddHttpClient<ISecretsEngine, OpenBaoSecretsEngine>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<OpenBaoOptions>>().Value;
        client.BaseAddress = options.Address;
    })
    .AddStandardResilienceHandler();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "openbao_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("wrapper-admin", policy => policy.RequireClaim("openbao_policy", "wrapper-admin", "root"));
builder.Services.AddHttpClient<OpenBaoAdministrativeClient>((serviceProvider, client) =>
{
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<OpenBaoOptions>>().Value.Address;
}).AddStandardResilienceHandler();
builder.Services.AddScoped<IProjectService, OpenBaoProjectService>();
builder.Services.AddScoped<IIdentityService, OpenBaoIdentityService>();
builder.Services.AddScoped<IPolicyService, OpenBaoPolicyService>();
builder.Services.AddScoped<IMachineIdentityService, OpenBaoMachineIdentityService>();

var app = builder.Build();
app.UseExceptionHandler(errorApplication =>
{
    errorApplication.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Request failed." });
    });
});
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.Use(async (context, next) =>
{
    var isUnsafeApiRequest = context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method)
        && !HttpMethods.IsTrace(context.Request.Method);

    if (isUnsafeApiRequest)
    {
        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet(
    "/api/auth/csrf",
    (IAntiforgery antiforgery, HttpContext context) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));

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
                identity.AddClaim(new Claim(OpenBaoTokenClaim, session.Token));
                foreach (var policy in session.Policies)
                {
                    identity.AddClaim(new Claim("openbao_policy", policy));
                }
                identity.AddClaim(
                    new Claim(OpenBaoExpirationClaim, session.ExpiresAt.ToUnixTimeSeconds().ToString()));

                await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        ExpiresUtc = session.ExpiresAt,
                        IsPersistent = false,
                    });

                return Results.Ok(new SessionResponse(session.ExpiresAt));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        })
    .RequireRateLimiting("login");

app.MapPost(
        "/api/auth/logout",
        async (ISessionService sessions, HttpContext context, CancellationToken cancellationToken) =>
        {
            var token = context.User.FindFirstValue(OpenBaoTokenClaim);
            if (token is not null)
            {
                await sessions.RevokeAsync(token, cancellationToken);
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
    .RequireAuthorization();

app.MapGet(
        "/api/auth/session",
        (HttpContext context) =>
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
                long.Parse(context.User.FindFirstValue(OpenBaoExpirationClaim)!));

            return Results.Ok(new SessionResponse(expiresAt));
        })
    .RequireAuthorization();

var administration = app.MapGroup("/api/admin")
    .RequireAuthorization("wrapper-admin");

administration.MapGet("/projects", async (IProjectService service, CancellationToken cancellationToken) =>
{
    var projects = await service.ListAsync(cancellationToken);
    return Results.Ok(projects.Select(project => new ProjectResponse(
        project.Id.Value,
        project.Description,
        project.Environments.Select(environment => environment.Value).ToList())));
});

administration.MapPost(
    "/projects/{project}",
    async (
        string project,
        CreateProjectRequest request,
        IProjectService service,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var result = await service.CreateAsync(
                ProjectId.Parse(project),
                request.Description,
                cancellationToken);
            return Results.Ok(new ProjectResponse(
                result.Id.Value,
                result.Description,
                result.Environments.Select(environment => environment.Value).ToList()));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

administration.MapDelete(
    "/projects/{project}",
    async (string project, IProjectService service, CancellationToken cancellationToken) =>
    {
        try
        {
            await service.DeleteAsync(ProjectId.Parse(project), cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

administration.MapGet("/members", async (IIdentityService service, CancellationToken cancellationToken) =>
{
    var members = await service.ListAsync(cancellationToken);
    return Results.Ok(members.Select(member => new MemberResponse(
        member.Username,
        member.EntityId,
        member.Disabled,
        member.Policies)));
});

administration.MapPost(
    "/members",
    async (CreateMemberRequest request, IIdentityService service, CancellationToken cancellationToken) =>
    {
        await service.CreateAsync(request.Username, request.Password, request.Policies, cancellationToken);
        return Results.NoContent();
    });

administration.MapPost(
    "/members/{username}/disable",
    async (string username, IIdentityService service, CancellationToken cancellationToken) =>
    {
        await service.DisableAsync(username, cancellationToken);
        return Results.NoContent();
    });

administration.MapDelete(
    "/members/{username}",
    async (string username, IIdentityService service, CancellationToken cancellationToken) =>
    {
        await service.DeleteAsync(username, cancellationToken);
        return Results.NoContent();
    });

administration.MapGet("/roles", async (IPolicyService service, CancellationToken cancellationToken) =>
{
    var roles = await service.ListAsync(cancellationToken);
    return Results.Ok(roles.Select(role => new RoleResponse(
        role.Name,
        role.Project,
        role.Environment,
        role.ReadOnly)));
});

administration.MapPost(
    "/roles",
    async (CreateRoleRequest request, IPolicyService service, CancellationToken cancellationToken) =>
    {
        await service.CreateRoleAsync(
            new Role(request.Name, request.Project, request.Environment, request.ReadOnly),
            cancellationToken);
        return Results.NoContent();
    });

administration.MapDelete(
    "/roles/{roleName}",
    async (string roleName, IPolicyService service, CancellationToken cancellationToken) =>
    {
        await service.DeleteRoleAsync(roleName, cancellationToken);
        return Results.NoContent();
    });

administration.MapPost(
    "/machine-identities",
    async (
        CreateMachineIdentityRequest request,
        IMachineIdentityService service,
        CancellationToken cancellationToken) =>
    {
        var identity = await service.CreateAsync(
            new MachineIdentity(
                request.Name,
                request.Name,
                request.Project,
                request.Environment,
                request.TokenTtlSeconds,
                request.TokenUses),
            cancellationToken);
        return Results.Ok(new MachineIdentityResponse(
            identity.Name,
            identity.RoleId,
            identity.Project,
            identity.Environment,
            identity.TokenTtlSeconds,
            identity.TokenUses));
    });

administration.MapPost(
    "/machine-identities/{roleName}/secret-id",
    async (string roleName, IMachineIdentityService service, CancellationToken cancellationToken) =>
        Results.Ok(new { secretId = await service.GenerateSecretIdAsync(roleName, cancellationToken) }));

administration.MapPost(
    "/machine-identities/{roleName}/secret-id/revoke",
    async (string roleName, IMachineIdentityService service, CancellationToken cancellationToken) =>
    {
        await service.RevokeSecretIdsAsync(roleName, cancellationToken);
        return Results.NoContent();
    });

var secrets = app.MapGroup("/api/projects/{project}/environments/{environment}/secrets")
    .RequireAuthorization();

secrets.MapGet(
    "/versions/{**path}",
    async (
        string project,
        string environment,
        string path,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var versions = await secretsEngine.ListVersionsAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                cancellationToken);
            return Results.Ok(versions.Select(version => new SecretVersionResponse(
                version.Version,
                version.DeletedAt,
                version.Destroyed)));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

secrets.MapPost(
    "/restore/{**path}",
    async (
        string project,
        string environment,
        string path,
        SecretVersionRequest request,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await secretsEngine.RestoreAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                request.Version,
                cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

secrets.MapPost(
    "/undelete/{**path}",
    async (
        string project,
        string environment,
        string path,
        SecretVersionRequest request,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await secretsEngine.UndeleteAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                request.Version,
                cancellationToken);
            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

secrets.MapGet(
    "/export/{**path}",
    async (
        string project,
        string environment,
        string path,
        string? format,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        var document = await secretsEngine.ReadAsync(
            ProjectId.Parse(project),
            EnvironmentId.Parse(environment),
            SecretPath.Parse(path),
            cancellationToken);
        if (document is null)
        {
            return Results.NotFound();
        }

        if (string.Equals(format, "env", StringComparison.OrdinalIgnoreCase))
        {
            var contents = string.Join(
                Environment.NewLine,
                document.Values.Select(pair => $"{pair.Key}={EscapeDotEnv(pair.Value)}"));
            return Results.Text(contents, "text/plain");
        }

        return Results.Json(document.Values);
    });

secrets.MapPost(
    "/import/{**path}",
    async (
        string project,
        string environment,
        string path,
        ImportSecretsRequest request,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        if (request.Values.Count == 0)
        {
            return Results.BadRequest();
        }

        await secretsEngine.WriteAsync(
            ProjectId.Parse(project),
            EnvironmentId.Parse(environment),
            SecretPath.Parse(path),
            new SecretDocument(request.Values, 0),
            request.ExpectedVersion,
            cancellationToken);
        return Results.NoContent();
    });

secrets.MapGet(
    "/{**path}",
    async (
        string project,
        string environment,
        string path,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var document = await secretsEngine.ReadAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                cancellationToken);

            return document is null
                ? Results.NotFound()
                : Results.Ok(new SecretDocumentResponse(document.Values, document.Version));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

secrets.MapPut(
    "/{**path}",
    async (
        string project,
        string environment,
        string path,
        SecretDocumentRequest request,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        if (request.Values.Count == 0 || request.Values.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
        {
            return Results.BadRequest();
        }

        try
        {
            await secretsEngine.WriteAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                new SecretDocument(request.Values, 0),
                request.ExpectedVersion,
                cancellationToken);

            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

secrets.MapDelete(
    "/{**path}",
    async (
        string project,
        string environment,
        string path,
        ISecretsEngine secretsEngine,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await secretsEngine.DeleteAsync(
                ProjectId.Parse(project),
                EnvironmentId.Parse(environment),
                SecretPath.Parse(path),
                cancellationToken);

            return Results.NoContent();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    });

app.Run();

static string EscapeDotEnv(string value) =>
    value.IndexOfAny([' ', '\t', '\n', '\r', '"', '\'']) >= 0
        ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")}\""
        : value;

public partial class Program
{
}
