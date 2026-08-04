using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Application;
using ControlPlane.Domain;
using ControlPlane.Infrastructure.OpenBao;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests;

public sealed class DomainAndSecretsEngineTests
{
    [Theory]
    [InlineData("../prod")]
    [InlineData("prod/%2e%2e/dev")]
    [InlineData("prod path")]
    [InlineData("")]
    public void Secret_paths_reject_traversal_and_invalid_segments(string value)
    {
        Assert.Throws<ArgumentException>(() => SecretPath.Parse(value));
    }

    [Fact]
    public void Project_and_environment_ids_reject_slashes()
    {
        Assert.Throws<ArgumentException>(() => ProjectId.Parse("project/other"));
        Assert.Throws<ArgumentException>(() => EnvironmentId.Parse("prod/other"));
    }

    [Theory]
    [InlineData("sys")]
    [InlineData("auth")]
    [InlineData("identity")]
    [InlineData("wrapper-metadata")]
    public void Project_ids_reject_reserved_openbao_paths(string value)
    {
        Assert.Throws<ArgumentException>(() => ProjectId.Parse(value));
    }

    [Fact]
    public async Task Read_maps_kv_v2_metadata_without_logging_secret_values()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":{\"data\":{\"API_KEY\":\"not-for-logs\"},\"metadata\":{\"version\":3}}}",
                System.Text.Encoding.UTF8,
                "application/json"),
        });
        var engine = CreateEngine(handler);

        var document = await engine.ReadAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(3, document.Version);
        Assert.Equal("not-for-logs", document.Values["API_KEY"]);
        Assert.DoesNotContain("not-for-logs", handler.LastRequest?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Write_sends_check_and_set_value()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(handler);

        await engine.WriteAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "value" }, 0),
            expectedVersion: 7,
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        var payload = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(7, payload.RootElement.GetProperty("options").GetProperty("cas").GetInt32());
    }

    [Fact]
    public async Task Write_merges_custom_metadata_rather_than_replacing_it()
    {
        // A POST to the metadata path replaces the whole custom_metadata map, so
        // saving a description used to wipe every tag and comment on the secret.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(handler);

        await engine.WriteAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "value" }, 0, "a description"),
            expectedVersion: null,
            cancellationToken: CancellationToken.None);

        var metadataRequest = Assert.Single(
            handler.Requests,
            request => request.RequestUri!.AbsolutePath.Contains("/metadata/"));
        Assert.Equal(HttpMethod.Patch, metadataRequest.Method);
        Assert.Equal(
            "application/merge-patch+json",
            metadataRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Restore_leaves_custom_metadata_untouched()
    {
        // custom_metadata belongs to the secret, not to a version, so a rollback
        // restores values only. Rewriting it from the historical read dropped
        // every annotation other than the description.
        var handler = new RecordingHandler(request =>
            request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"data":{"data":{"KEY":"old"},"metadata":{"version":1,"custom_metadata":{"description":"d","tags":"a,b"}}}}
                        """),
                }
                : new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(handler);

        await engine.RestoreAsync(
            ProjectId.Parse("thorneai"),
            EnvironmentId.Parse("development"),
            SecretPath.Parse("backend"),
            version: 1,
            cancellationToken: CancellationToken.None);

        Assert.DoesNotContain(
            handler.Requests,
            request => request.RequestUri!.AbsolutePath.Contains("/metadata/"));
    }

    [Fact]
    public void Activity_entries_carry_key_names_but_never_values()
    {
        // The feed is readable by anyone who can read the project, so a value must
        // never reach it. Only key names are recorded.
        var entry = new ActivityEntry(
            DateTimeOffset.UtcNow,
            "alice",
            ActivityAction.SecretSaved,
            "thorneai",
            "production",
            "backend",
            ["API_KEY"],
            3);

        Assert.Equal(["API_KEY"], entry.KeysAffected);

        // Structural guarantee rather than a string check: the record has no field a
        // secret value could be put into, so it cannot leak one by accident later.
        Assert.DoesNotContain(
            typeof(ActivityEntry).GetProperties(),
            property => property.Name.Contains("Value", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    private static OpenBaoSecretsEngine CreateEngine(RecordingHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://openbao/") },
            new FixedTokenAccessor("restricted-token"));

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            if (LastBody is not null)
            {
                Bodies.Add(LastBody);
            }
            return responder(request);
        }
    }
}
