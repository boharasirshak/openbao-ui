using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
                policy = "path \"thorneai/data/development/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\"] }\npath \"thorneai/metadata/development/*\" { capabilities = [\"read\", \"list\", \"update\"] }",
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
        Assert.Contains("dev-writer", session.Policies);
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
            new SecretDocument(
                new Dictionary<string, string> { ["DATABASE_URL"] = "secret-value" },
                0,
                "Backend development credentials"),
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
        Assert.Equal("Backend development credentials", document.Description);
        var entries = await engine.ListAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            folder: null,
            CancellationToken.None);
        Assert.Contains(entries, entry => entry.Name == "backend" && !entry.IsFolder);
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
    public async Task Restricted_user_cannot_list_production_metadata_or_mounts()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var session = await new OpenBaoSessionService(
            client,
            Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync("alice", "correct-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.ListAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("production"),
            folder: null,
            cancellationToken: CancellationToken.None));

        using var mountsRequest = new HttpRequestMessage(HttpMethod.Get, "v1/sys/mounts");
        mountsRequest.Headers.Add("X-Vault-Token", session.Token);
        using var mountsResponse = await client.SendAsync(mountsRequest);
        Assert.Equal(HttpStatusCode.Forbidden, mountsResponse.StatusCode);
    }

    [Fact]
    public async Task Production_viewer_cannot_write_production()
    {
        using var admin = new HttpClient { BaseAddress = _address };
        admin.DefaultRequestHeaders.Add("X-Vault-Token", "test-root");
        var policyName = $"prod-reader-{Guid.NewGuid():N}";
        var username = $"engineer-{Guid.NewGuid():N}";
        await admin.PutAsJsonAsync(
            $"v1/sys/policies/acl/{policyName}",
            new { policy = "path \"thorneai/data/production/*\" { capabilities = [\"read\"] }" });
        await admin.PostAsJsonAsync(
            $"v1/auth/userpass/users/{username}",
            new { password = "engineer-password", policies = new[] { policyName } });

        using var client = new HttpClient { BaseAddress = _address };
        var session = await new OpenBaoSessionService(client, Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync(username, "engineer-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.WriteAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("production"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "value" }, 0),
            expectedVersion: 0,
            CancellationToken.None));
    }

    [Fact]
    public async Task Restricted_user_cannot_grant_themselves_policies()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var session = await new OpenBaoSessionService(
                client,
                Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync("alice", "correct-password", CancellationToken.None);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/userpass/users/alice")
        {
            Content = JsonContent.Create(new { policies = new[] { "wrapper-admin" } }),
        };
        request.Headers.Add("X-Vault-Token", session.Token);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
            new MachineIdentity("coolify-thorneai-prod", "coolify-thorneai-prod", "thorneai", "production", true, 60, 1),
            CancellationToken.None);
        var secretId = await machines.GenerateSecretIdAsync(identity.Name, CancellationToken.None);
        using var loginResponse = await client.PostAsJsonAsync(
            "v1/auth/approle/login",
            new { role_id = identity.RoleId, secret_id = secretId });
        loginResponse.EnsureSuccessStatusCode();
        using var loginPayload = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var machineToken = loginPayload.RootElement.GetProperty("auth").GetProperty("client_token").GetString()!;
        var machineEngine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(machineToken));

        Assert.NotEqual(identity.Name, identity.RoleId);
        Assert.NotEmpty(secretId);
        await Assert.ThrowsAsync<HttpRequestException>(() => machineEngine.ReadAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            CancellationToken.None));
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

        var bobSession = await new OpenBaoSessionService(client, options)
            .LoginAsync("bob", "bob-password", CancellationToken.None);
        await identities.DisableAsync("bob", CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new OpenBaoSessionService(
                client,
                options)
            .LoginAsync("bob", "bob-password", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => new OpenBaoSecretsEngine(
                client,
                new FixedTokenAccessor(bobSession.Token))
            .ReadAsync(
                ProjectId.Parse("thorneai"),
                EnvironmentId.Parse("development"),
                SecretPath.Parse("backend"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Cli_login_and_export_do_not_print_secret_values_to_stderr()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var admin = new OpenBaoAdministrativeClient(client, Options.Create(new OpenBaoOptions
        {
            Address = _address,
            ControlToken = "test-root",
        }));
        await admin.PostAsync(
            "v1/sys/mounts/cli-project",
            new { type = "kv", options = new { version = "2" } },
            CancellationToken.None);
        await admin.PutAsync(
            "v1/sys/policies/acl/cli-reader",
            new { policy = "path \"cli-project/data/development/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\"] }" },
            CancellationToken.None);
        await admin.PostAsync(
            "v1/auth/userpass/users/cli-user",
            new { password = "cli-password", policies = new[] { "cli-reader" } },
            CancellationToken.None);
        var cliSession = await new OpenBaoSessionService(
                client,
                Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync("cli-user", "cli-password", CancellationToken.None);
        await new OpenBaoSecretsEngine(client, new FixedTokenAccessor(cliSession.Token)).WriteAsync(
            ProjectId.Parse("cli-project"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["CLI_SECRET"] = "secret-value" }, 0),
            expectedVersion: 0,
            cancellationToken: CancellationToken.None);
        var tokenFile = Path.Combine(Path.GetTempPath(), $"secrets-token-{Guid.NewGuid():N}");
        try
        {
            var login = await RunCliAsync(tokenFile, "login", "--username", "cli-user", "--password", "cli-password");
            Assert.Equal(0, login.ExitCode);
            var export = await RunCliAsync(tokenFile, "export", "--project", "cli-project", "--env", "development", "--path", "backend");
            Assert.Equal(0, export.ExitCode);
            Assert.Contains("CLI_SECRET=secret-value", export.StandardOutput);
            Assert.DoesNotContain("secret-value", export.StandardError);
            var run = await RunCliAsync(
                tokenFile,
                "run",
                "--project",
                "cli-project",
                "--env",
                "development",
                "--path",
                "backend",
                "--",
                "sh",
                "-c",
                "printf '%s' \"$CLI_SECRET\"");
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("secret-value", run.StandardOutput);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliAsync(
        string tokenFile,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = AppContext.BaseDirectory,
            ArgumentList = { "ControlPlane.Cli.dll" },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            Environment =
            {
                ["OPENBAO_ADDR"] = _address.ToString(),
                ["SECRETS_TOKEN_FILE"] = tokenFile,
            },
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start CLI.");
        await process.WaitForExitAsync();
        return (
            process.ExitCode,
            await process.StandardOutput.ReadToEndAsync(),
            await process.StandardError.ReadToEndAsync());
    }

    public Task DisposeAsync() => _openBao.DisposeAsync().AsTask();

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
