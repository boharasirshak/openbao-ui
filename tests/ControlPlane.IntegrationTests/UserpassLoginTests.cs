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

    public Task DisposeAsync() => _openBao.DisposeAsync().AsTask();

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
