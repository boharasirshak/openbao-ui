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
    public async Task Audit_projection_never_returns_secret_payloads()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                "{\"time\":\"2026-01-01T00:00:00Z\",\"type\":\"request\",\"auth\":{\"display_name\":\"alice\"},\"request\":{\"operation\":\"read\",\"path\":\"thorneai/data/development/backend\",\"data\":{\"API_KEY\":\"secret-value\"}}}");
            var service = new OpenBaoAuditService(Options.Create(new OpenBaoOptions
            {
                Address = new Uri("http://openbao/"),
                AuditLogPath = path,
            }));

            var events = await service.RecentAsync(10, CancellationToken.None);

            var auditEvent = Assert.Single(events);
            Assert.Equal("thorneai/data/development/backend", auditEvent.Path);
            Assert.DoesNotContain("secret-value", auditEvent.ToString());
        }
        finally
        {
            File.Delete(path);
        }
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
