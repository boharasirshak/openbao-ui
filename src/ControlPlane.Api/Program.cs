using System.Net;
using System.Threading.RateLimiting;
using ControlPlane.Api;
using ControlPlane.Api.Endpoints;
using ControlPlane.Application;
using ControlPlane.Infrastructure.OpenBao;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var isLocalDevelopment = builder.Environment.IsEnvironment("LocalDevelopment");

builder.Services.AddOpenApi();
builder.Services.Configure<OpenBaoOptions>(
    builder.Configuration.GetSection(OpenBaoOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOpenBaoTokenAccessor, HttpContextOpenBaoTokenAccessor>();

builder.Services.AddOpenBaoClient<ISessionService, OpenBaoSessionService>();
builder.Services.AddOpenBaoClient<ISecretsEngine, OpenBaoSecretsEngine>();
builder.Services.AddOpenBaoClient<IDatabaseCredentialService, OpenBaoDatabaseCredentialService>();
builder.Services.AddOpenBaoClient<ISecretShareService, OpenBaoSecretShareService>();
builder.Services.AddHttpClient<OpenBaoAdministrativeClient>((serviceProvider, client) =>
{
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<OpenBaoOptions>>().Value.Address;
}).AddStandardResilienceHandler();

builder.Services.AddScoped<IProjectService, OpenBaoProjectService>();
builder.Services.AddScoped<IIdentityService, OpenBaoIdentityService>();
builder.Services.AddScoped<IPolicyService, OpenBaoPolicyService>();
builder.Services.AddScoped<IMachineIdentityService, OpenBaoMachineIdentityService>();
builder.Services.AddScoped<IActivityLog, OpenBaoActivityLog>();
builder.Services.AddScoped<ITeamService, OpenBaoTeamService>();
builder.Services.AddScoped<IAccessRoleService, OpenBaoAccessRoleService>();
builder.Services.AddScoped<ICapabilityService, OpenBaoCapabilityService>();
builder.Services.AddScoped<IChangeRequestService, OpenBaoChangeRequestService>();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = isLocalDevelopment
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "openbao_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = isLocalDevelopment
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = false;
        // This is an API, so both of these must answer with a status code. Cookie auth
        // redirects by default, which is right for a server-rendered site and wrong
        // here: a signed-in member hitting an administrator endpoint got a 302 to a
        // login page, so the client saw 200 and some HTML instead of 403. Every
        // "you cannot see this, here is the fallback" path in the dashboard depends on
        // reading a real 403, and all of them silently showed nothing.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        ApiClaims.AdminPolicy,
        policy => policy.RequireClaim(ApiClaims.OpenBaoPolicy, ApiClaims.AdminPolicy, "root"));

var app = builder.Build();

app.UseExceptionHandler(errorApplication =>
{
    errorApplication.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => StatusCodes.Status403Forbidden,
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } => StatusCodes.Status404NotFound,
            HttpRequestException { StatusCode: HttpStatusCode.Conflict } => StatusCodes.Status409Conflict,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

        // OpenBao's own error bodies never reach the client.
        await context.Response.WriteAsJsonAsync(new { error = "Request failed." });
    });
});

if (!isLocalDevelopment)
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});

// Antiforgery is enforced here rather than per endpoint so a new route cannot forget it.
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

// Served in every non-production environment, including LocalDevelopment: the
// dashboard's type generation reads this document, and gating it on IsDevelopment
// alone made `npm run generate-api` fail against the documented local setup.
if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
}

app.MapSessionEndpoints();
app.MapProjectEndpoints();
app.MapAccessEndpoints();
// Literal segments before the secrets group's {**path} catch-all, so /metadata,
// /destroy, /purge and /folders are not swallowed by it.
app.MapSecretMetadataEndpoints();
app.MapFolderEndpoints();
app.MapSecretEndpoints();
app.MapDiscoveryEndpoints();
app.MapShareEndpoints();
app.MapActivityEndpoints();
app.MapChangeRequestEndpoints();
app.MapProjectMemberEndpoints();
app.MapTeamEndpoints();
app.MapAccessRoleEndpoints();
app.MapPermissionEndpoints();
app.MapDatabaseEndpoints();

app.Run();

public partial class Program
{
}
