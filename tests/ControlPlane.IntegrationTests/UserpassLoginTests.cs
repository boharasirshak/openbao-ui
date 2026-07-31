using System.Net.Http.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using ControlPlane.Infrastructure.OpenBao;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace ControlPlane.IntegrationTests;

public sealed class UserpassLoginTests : IAsyncLifetime
{
    private readonly IContainer _openBao = new ContainerBuilder("openbao/openbao:2.2.0")
        .WithPortBinding(8200, true)
        .WithCommand("server", "-dev", "-dev-root-token-id=test-root", "-dev-listen-address=0.0.0.0:8200")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8200).ForPath("/v1/sys/health")))
        .Build();
    private Uri _address = null!;

    public async Task InitializeAsync()
    {
        await _openBao.StartAsync();
        _address = new Uri($"http://{_openBao.Hostname}:{_openBao.GetMappedPublicPort(8200)}/");
        using var admin = new HttpClient { BaseAddress = _address };
        admin.DefaultRequestHeaders.Add("X-Vault-Token", "test-root");
        await admin.PostAsJsonAsync("v1/sys/auth/userpass", new { type = "userpass" });
        await admin.PostAsJsonAsync("v1/sys/mounts/thorneai", new { type = "kv", options = new { version = "2" } });
        await admin.PutAsJsonAsync(
            "v1/sys/policies/acl/dev-writer",
            new
            {
                policy = "path \"thorneai/data/development/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\"] }",
            });
        var create = await admin.PostAsJsonAsync(
            "v1/auth/userpass/users/alice",
            new { password = "correct-password", policies = new[] { "dev-writer" }, token_ttl = "10m" });
        create.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_returns_a_short_lived_openbao_session()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var service = new OpenBaoSessionService(client, Options.Create(new OpenBaoOptions { Address = _address }));
        var session = await service.LoginAsync("alice", "correct-password", CancellationToken.None);
        Assert.NotEmpty(session.Token);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Authorized_user_can_write_and_read_a_kv_v2_document()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var sessions = new OpenBaoSessionService(
            client,
            Options.Create(new OpenBaoOptions { Address = _address }));
        var session = await sessions.LoginAsync("alice", "correct-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await engine.WriteAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["DATABASE_URL"] = "secret-value" }, 0),
            expectedVersion: 0,
            cancellationToken: CancellationToken.None);
        var document = await engine.ReadAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("secret-value", document.Values["DATABASE_URL"]);
        Assert.Equal(1, document.Version);
    }

    [Fact]
    public async Task Development_policy_cannot_read_production()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var session = await new OpenBaoSessionService(
            client,
            Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync("alice", "correct-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.ReadAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("production"),
            SecretPath.Parse("backend"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_an_OpenBao_session()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var service = new OpenBaoSessionService(
            client,
            Options.Create(new OpenBaoOptions { Address = _address }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            "alice",
            "wrong-password",
            CancellationToken.None));
    }

    [Fact]
    public async Task Revoked_token_cannot_read_a_secret()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var sessions = new OpenBaoSessionService(
            client,
            Options.Create(new OpenBaoOptions { Address = _address }));
        var session = await sessions.LoginAsync("alice", "correct-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await sessions.RevokeAsync(session.Token, CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.ReadAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Disabled_user_cannot_log_in()
    {
        using var admin = new HttpClient { BaseAddress = _address };
        admin.DefaultRequestHeaders.Add("X-Vault-Token", "test-root");
        var disable = await admin.DeleteAsync("v1/auth/userpass/users/alice");
        disable.EnsureSuccessStatusCode();

        var service = new OpenBaoSessionService(
            new HttpClient { BaseAddress = _address },
            Options.Create(new OpenBaoOptions { Address = _address }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            "alice",
            "correct-password",
            CancellationToken.None));
    }

    [Fact]
    public async Task Project_creation_is_idempotent_and_creates_a_kv_mount()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var options = Options.Create(new OpenBaoOptions
        {
            Address = _address,
            ControlToken = "test-root",
        });
        var service = new OpenBaoProjectService(new OpenBaoAdministrativeClient(client, options), options);

        var first = await service.CreateAsync(
            ProjectId.Parse("job-engine"),
            "Job engine secrets",
            CancellationToken.None);
        var second = await service.CreateAsync(
            ProjectId.Parse("job-engine"),
            "Job engine secrets",
            CancellationToken.None);
        var projects = await service.ListAsync(CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Contains(projects, project => project.Id == ProjectId.Parse("job-engine"));
        Assert.Equal(3, first.Environments.Count);
    }

    [Fact]
    public async Task AppRole_machine_identity_gets_a_restricted_secret_id()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var options = Options.Create(new OpenBaoOptions
        {
            Address = _address,
            ControlToken = "test-root",
        });
        var admin = new OpenBaoAdministrativeClient(client, options);
        var policyService = new OpenBaoPolicyService(admin);
        var machines = new OpenBaoMachineIdentityService(admin, policyService);

        var identity = await machines.CreateAsync(
            new MachineIdentity("coolify-thorneai-prod", "coolify-thorneai-prod", "thorneai", "production", 60, 1),
            CancellationToken.None);
        var secretId = await machines.GenerateSecretIdAsync(identity.Name, CancellationToken.None);

        Assert.NotEqual(identity.Name, identity.RoleId);
        Assert.NotEmpty(secretId);
    }

    [Fact]
    public async Task User_creation_creates_an_identity_entity_and_offboarding_removes_login()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var options = Options.Create(new OpenBaoOptions
        {
            Address = _address,
            ControlToken = "test-root",
        });
        var identities = new OpenBaoIdentityService(new OpenBaoAdministrativeClient(client, options));

        await identities.CreateAsync("bob", "bob-password", ["default"], CancellationToken.None);
        var members = await identities.ListAsync(CancellationToken.None);
        var bob = Assert.Single(members, member => member.Username == "bob");
        Assert.NotEmpty(bob.EntityId);

        await identities.DisableAsync("bob", CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new OpenBaoSessionService(
                client,
                options)
            .LoginAsync("bob", "bob-password", CancellationToken.None));
    }

    public Task DisposeAsync() => _openBao.DisposeAsync().AsTask();

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
