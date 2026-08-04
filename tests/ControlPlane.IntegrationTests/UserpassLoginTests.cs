using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using ControlPlane.Infrastructure.OpenBao;
using ControlPlane.IntegrationTests.Fixtures;
using Microsoft.Extensions.Options;

namespace ControlPlane.IntegrationTests;

[Collection(OpenBaoCollection.Name)]
public sealed class UserpassLoginTests(OpenBaoFixture fixture)
{
    private Uri _address => fixture.Address;

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
        // Its own user: deleting the shared one would break every sibling test now
        // that the container is shared.
        var username = await fixture.NewUserAsync("disabled-password", OpenBaoFixture.SharedPolicy);
        using var admin = fixture.CreateRootClient();
        var disable = await admin.DeleteAsync($"v1/auth/userpass/users/{username}");
        disable.EnsureSuccessStatusCode();

        var service = new OpenBaoSessionService(fixture.CreateClient(), fixture.AnonymousOptions());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            username,
            "disabled-password",
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
        var name = ProjectId.Parse(OpenBaoFixture.NewName("job-engine"));

        var first = await service.CreateAsync(name, "Job engine secrets", CancellationToken.None);
        var second = await service.CreateAsync(name, "Job engine secrets", CancellationToken.None);
        var projects = await service.ListAsync(CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Contains(projects, project => project.Id == name);
        Assert.Equal(3, first.Environments.Count);
    }

    [Fact]
    public async Task Project_admin_policy_cannot_modify_another_project()
    {
        using var client = new HttpClient { BaseAddress = _address };
        var options = Options.Create(new OpenBaoOptions
        {
            Address = _address,
            ControlToken = "test-root",
        });
        var admin = new OpenBaoAdministrativeClient(client, options);
        var projectService = new OpenBaoProjectService(admin, options);
        var scopedProject = ProjectId.Parse(OpenBaoFixture.NewName("scoped"));
        await projectService.CreateAsync(scopedProject, "Scoped", CancellationToken.None);
        var scopedUser = OpenBaoFixture.NewName("scoped-admin");
        await admin.PostAsync(
            $"v1/auth/userpass/users/{scopedUser}",
            new { password = "scoped-password", policies = new[] { $"{scopedProject}-admin" } },
            CancellationToken.None);

        var session = await new OpenBaoSessionService(client, options)
            .LoginAsync(scopedUser, "scoped-password", CancellationToken.None);
        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await engine.WriteAsync(
            scopedProject,
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "scoped" }, 0),
            expectedVersion: 0,
            CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.WriteAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "cross-project" }, 0),
            expectedVersion: 0,
            CancellationToken.None));
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
        var machineName = OpenBaoFixture.NewName("runner");
        var identity = await machines.CreateAsync(
            new MachineIdentity(
                machineName,
                machineName,
                OpenBaoFixture.SharedProject,
                "production",
                true,
                60,
                1),
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

        var username = OpenBaoFixture.NewName("member");
        await identities.CreateAsync(username, "member-password", ["default"], CancellationToken.None);
        var members = await identities.ListAsync(CancellationToken.None);
        var member = Assert.Single(members, candidate => candidate.Username == username);
        Assert.NotEmpty(member.EntityId);

        var memberSession = await new OpenBaoSessionService(client, options)
            .LoginAsync(username, "member-password", CancellationToken.None);
        await identities.DisableAsync(username, CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new OpenBaoSessionService(
                client,
                options)
            .LoginAsync(username, "member-password", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => new OpenBaoSecretsEngine(
                client,
                new FixedTokenAccessor(memberSession.Token))
            .ReadAsync(
                ProjectId.Parse(OpenBaoFixture.SharedProject),
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
        var cliProject = OpenBaoFixture.NewName("cli-project");
        var cliPolicy = OpenBaoFixture.NewName("cli-reader");
        var cliUser = OpenBaoFixture.NewName("cli-user");
        await admin.PostAsync(
            $"v1/sys/mounts/{cliProject}",
            new { type = "kv", options = new { version = "2" } },
            CancellationToken.None);
        await admin.PutAsync(
            $"v1/sys/policies/acl/{cliPolicy}",
            new { policy = $"path \"{cliProject}/data/development/*\" {{ capabilities = [\"create\", \"read\", \"update\", \"delete\"] }}" },
            CancellationToken.None);
        await admin.PostAsync(
            $"v1/auth/userpass/users/{cliUser}",
            new { password = "cli-password", policies = new[] { cliPolicy } },
            CancellationToken.None);
        var cliSession = await new OpenBaoSessionService(
                client,
                Options.Create(new OpenBaoOptions { Address = _address }))
            .LoginAsync(cliUser, "cli-password", CancellationToken.None);
        await new OpenBaoSecretsEngine(client, new FixedTokenAccessor(cliSession.Token)).WriteAsync(
            ProjectId.Parse(cliProject),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["CLI_SECRET"] = "secret-value" }, 0),
            expectedVersion: 0,
            cancellationToken: CancellationToken.None);
        var tokenFile = Path.Combine(Path.GetTempPath(), $"secrets-token-{Guid.NewGuid():N}");
        try
        {
            var login = await RunCliWithInputAsync(
                tokenFile,
                "cli-password\n",
                "login",
                "--username",
                cliUser,
                "--password-stdin");
            Assert.Equal(0, login.ExitCode);
            var export = await RunCliAsync(tokenFile, "export", "--project", cliProject, "--env", "development", "--path", "backend");
            Assert.Equal(0, export.ExitCode);
            Assert.Contains("CLI_SECRET=secret-value", export.StandardOutput);
            Assert.DoesNotContain("secret-value", export.StandardError);
            var run = await RunCliAsync(
                tokenFile,
                "run",
                "--project",
                cliProject,
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
        => await RunCliWithInputAsync(tokenFile, null, arguments);

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCliWithInputAsync(
        string tokenFile,
        string? standardInput,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = AppContext.BaseDirectory,
            ArgumentList = { "ControlPlane.Cli.dll" },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = standardInput is not null,
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
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }
        await process.WaitForExitAsync();
        return (
            process.ExitCode,
            await process.StandardOutput.ReadToEndAsync(),
            await process.StandardError.ReadToEndAsync());
    }

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
