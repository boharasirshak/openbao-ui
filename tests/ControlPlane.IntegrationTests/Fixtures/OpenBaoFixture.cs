using System.Net.Http.Json;
using ControlPlane.Infrastructure.OpenBao;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace ControlPlane.IntegrationTests.Fixtures;

/// <summary>
/// One OpenBao container shared by every test in the collection. It used to be an
/// instance field with IAsyncLifetime, and xUnit builds a fresh test-class instance per
/// fact, so a class of fourteen facts started fourteen containers.
///
/// Sharing a container means tests must not fight over names. Use <see cref="NewName"/>
/// and the New*Async helpers for anything a test mutates or deletes; only read from the
/// Shared* baseline.
/// </summary>
public sealed class OpenBaoFixture : IAsyncLifetime
{
    public const string RootToken = "test-root";
    public const string SharedProject = "thorneai";
    public const string SharedUser = "alice";
    public const string SharedPassword = "correct-password";
    public const string SharedPolicy = "dev-writer";

    private readonly IContainer _container = new ContainerBuilder("openbao/openbao:2.2.0")
        .WithPortBinding(8200, true)
        .WithCommand("server", "-dev", $"-dev-root-token-id={RootToken}", "-dev-listen-address=0.0.0.0:8200")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(8200).ForPath("/v1/sys/health")))
        .Build();

    public Uri Address { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Address = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8200)}/");

        using var root = CreateRootClient();
        await root.PostAsJsonAsync("v1/sys/auth/userpass", new { type = "userpass" });
        await root.PostAsJsonAsync(
            $"v1/sys/mounts/{SharedProject}",
            new { type = "kv", options = new { version = "2" } });

        // Mirrors the editor policy the product generates. "patch" is required on
        // metadata because annotations are merge-patched and "update" does not cover
        // PATCH; "scan" is a separate capability gating recursive listing.
        await root.PutAsJsonAsync(
            $"v1/sys/policies/acl/{SharedPolicy}",
            new
            {
                policy =
                    $"path \"{SharedProject}/data/development/*\" {{ capabilities = [\"create\", \"read\", \"update\", \"patch\", \"delete\"] }}\n"
                    + $"path \"{SharedProject}/metadata/development/*\" {{ capabilities = [\"create\", \"read\", \"update\", \"patch\", \"list\", \"scan\"] }}",
            });

        var created = await root.PostAsJsonAsync(
            $"v1/auth/userpass/users/{SharedUser}",
            new { password = SharedPassword, policies = new[] { SharedPolicy }, token_ttl = "10m" });
        created.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public HttpClient CreateClient() => new() { BaseAddress = Address };

    public HttpClient CreateRootClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", RootToken);
        return client;
    }

    public IOptions<OpenBaoOptions> RootOptions() =>
        Options.Create(new OpenBaoOptions { Address = Address, ControlToken = RootToken });

    public IOptions<OpenBaoOptions> AnonymousOptions() =>
        Options.Create(new OpenBaoOptions { Address = Address });

    /// <summary>A name no other test will touch. Valid as a mount, user or policy name.</summary>
    public static string NewName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public async Task<string> NewPolicyAsync(string hcl, string prefix = "policy")
    {
        var name = NewName(prefix);
        using var root = CreateRootClient();
        var response = await root.PutAsJsonAsync($"v1/sys/policies/acl/{name}", new { policy = hcl });
        response.EnsureSuccessStatusCode();
        return name;
    }

    public async Task<string> NewUserAsync(string password, params string[] policies)
    {
        var username = NewName("user");
        using var root = CreateRootClient();
        var response = await root.PostAsJsonAsync(
            $"v1/auth/userpass/users/{username}",
            new { password, policies, token_ttl = "10m" });
        response.EnsureSuccessStatusCode();
        return username;
    }

    public async Task<string> NewKvMountAsync(string prefix = "project")
    {
        var mount = NewName(prefix);
        using var root = CreateRootClient();
        var response = await root.PostAsJsonAsync(
            $"v1/sys/mounts/{mount}",
            new { type = "kv", options = new { version = "2" } });
        response.EnsureSuccessStatusCode();
        return mount;
    }
}
