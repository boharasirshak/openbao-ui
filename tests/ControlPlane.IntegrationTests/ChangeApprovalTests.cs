using ControlPlane.Api;
using ControlPlane.Application;
using ControlPlane.Domain;
using ControlPlane.Infrastructure.OpenBao;
using ControlPlane.IntegrationTests.Fixtures;
using Microsoft.Extensions.Options;

namespace ControlPlane.IntegrationTests;

/// <summary>
/// Approval is a workflow this application enforces, not something OpenBao knows about,
/// so it needs its own coverage: the rules only hold if the code holds them.
/// </summary>
[Collection(OpenBaoCollection.Name)]
public sealed class ChangeApprovalTests(OpenBaoFixture fixture)
{
    [Fact]
    public async Task A_proposed_change_only_reaches_the_secret_after_someone_else_approves_it()
    {
        var context = await SetupAsync();
        var path = SecretPath.Parse("service/config");

        var request = await context.Changes.ProposeAsync(
            context.Project,
            Production,
            path,
            new Dictionary<string, string> { ["API_KEY"] = "proposed-value" },
            description: null,
            reason: "rotating the key",
            expectedVersion: null,
            isDeletion: false,
            requestedBy: "dana",
            CancellationToken.None);

        // Nothing has been written to the real path yet.
        Assert.Null(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));
        Assert.Equal(ChangeRequestStatus.Pending, request.Status);
        Assert.Equal(["API_KEY"], request.KeysAffected);

        // The requester cannot wave their own change through.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Changes.ApplyAsync(
            context.Project,
            request.Id,
            "dana",
            note: null,
            CancellationToken.None));
        Assert.Null(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));

        var applied = await context.Changes.ApplyAsync(
            context.Project,
            request.Id,
            "erin",
            "looks right",
            CancellationToken.None);

        Assert.Equal(ChangeRequestStatus.Applied, applied.Status);
        var document = await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal("proposed-value", document.Values["API_KEY"]);

        // The pending copy is gone once the change has landed, so there is no second
        // readable copy of a production value lying around.
        Assert.Null(await context.Changes.ReadProposedAsync(applied, CancellationToken.None));
        Assert.Null(await context.Engine.ReadAsync(
            context.Project,
            EnvironmentId.Reserved(ChangeRequest.PendingEnvironment),
            applied.PendingPath,
            CancellationToken.None));
    }

    [Fact]
    public async Task A_rejected_change_never_touches_the_secret_and_cannot_be_reopened()
    {
        var context = await SetupAsync();
        var path = SecretPath.Parse("service/rejected");

        var request = await context.Changes.ProposeAsync(
            context.Project,
            Production,
            path,
            new Dictionary<string, string> { ["TOKEN"] = "nope" },
            description: null,
            reason: null,
            expectedVersion: null,
            isDeletion: false,
            requestedBy: "dana",
            CancellationToken.None);

        var rejected = await context.Changes.RejectAsync(
            context.Project,
            request.Id,
            "erin",
            "wrong environment",
            CancellationToken.None);

        Assert.Equal(ChangeRequestStatus.Rejected, rejected.Status);
        Assert.False(rejected.Reviews[0].Approved);
        Assert.Null(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Changes.ApplyAsync(
            context.Project,
            request.Id,
            "erin",
            note: null,
            CancellationToken.None));
        Assert.Null(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));
    }

    [Fact]
    public async Task A_deletion_can_be_proposed_and_removes_the_secret_once_approved()
    {
        var context = await SetupAsync();
        var path = SecretPath.Parse("service/retired");
        await context.Engine.WriteAsync(
            context.Project,
            Production,
            path,
            new SecretDocument(new Dictionary<string, string> { ["OLD"] = "value" }, 0),
            expectedVersion: null,
            CancellationToken.None);

        var request = await context.Changes.ProposeAsync(
            context.Project,
            Production,
            path,
            new Dictionary<string, string>(),
            description: null,
            reason: "service is gone",
            expectedVersion: null,
            isDeletion: true,
            requestedBy: "dana",
            CancellationToken.None);

        Assert.True(request.IsDeletion);
        Assert.Null(await context.Changes.ReadProposedAsync(request, CancellationToken.None));
        Assert.NotNull(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));

        await context.Changes.ApplyAsync(context.Project, request.Id, "erin", null, CancellationToken.None);
        Assert.Null(await context.Engine.ReadAsync(context.Project, Production, path, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_requester_can_withdraw_their_own_change()
    {
        var context = await SetupAsync();
        var request = await context.Changes.ProposeAsync(
            context.Project,
            Production,
            SecretPath.Parse("service/withdrawn"),
            new Dictionary<string, string> { ["KEY"] = "value" },
            description: null,
            reason: null,
            expectedVersion: null,
            isDeletion: false,
            requestedBy: "dana",
            CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Changes.WithdrawAsync(
            context.Project,
            request.Id,
            "erin",
            CancellationToken.None));

        var withdrawn = await context.Changes.WithdrawAsync(
            context.Project,
            request.Id,
            "dana",
            CancellationToken.None);
        Assert.Equal(ChangeRequestStatus.Withdrawn, withdrawn.Status);

        var listed = await context.Changes.ListAsync(context.Project, CancellationToken.None);
        Assert.Contains(listed, entry => entry.Id == request.Id && !entry.IsOpen);
    }

    /// <summary>
    /// The generated editor policy has to cover the pending path, or proposing a change
    /// would 403 for exactly the people who are supposed to use the workflow.
    /// </summary>
    [Fact]
    public async Task An_editor_can_propose_a_change_with_only_their_own_policy()
    {
        var context = await SetupAsync();
        var editor = await fixture.NewUserAsync(
            "editor-password",
            $"{context.Project}-{Production}-editor");

        using var client = fixture.CreateClient();
        var sessions = new OpenBaoSessionService(client, fixture.AnonymousOptions());
        var session = await sessions.LoginAsync(editor, "editor-password", CancellationToken.None);
        var scopedEngine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(session.Token));

        await scopedEngine.WriteAsync(
            context.Project,
            EnvironmentId.Reserved(ChangeRequest.PendingEnvironment),
            SecretPath.Parse($"{Production}/probe"),
            new SecretDocument(new Dictionary<string, string> { ["KEY"] = "value" }, 0),
            expectedVersion: null,
            CancellationToken.None);

        var stored = await scopedEngine.ReadAsync(
            context.Project,
            EnvironmentId.Reserved(ChangeRequest.PendingEnvironment),
            SecretPath.Parse($"{Production}/probe"),
            CancellationToken.None);
        Assert.Equal("value", stored?.Values["KEY"]);
    }

    /// <summary>
    /// A project created before this check existed has no read grant on its own record,
    /// and the API reads that record with the caller's token. Failing closed is the only
    /// safe direction: the alternative is treating "cannot tell" as "not protected".
    /// </summary>
    [Fact]
    public async Task A_stale_project_policy_blocks_the_write_rather_than_ignoring_protection()
    {
        var options = fixture.RootOptions();
        using var client = fixture.CreateClient();
        var project = ProjectId.Parse(OpenBaoFixture.NewName("stale"));
        var admin = new OpenBaoAdministrativeClient(client, options);
        await new OpenBaoProjectService(admin, options).CreateAsync(project, "Stale", CancellationToken.None);

        // The policy as it was generated before the control-plane grants existed.
        var legacy = await fixture.NewPolicyAsync(
            $"path \"{project}/data/production/*\" {{ capabilities = [\"create\", \"read\", \"update\", \"list\"] }}",
            "legacy");
        var user = await fixture.NewUserAsync("stale-password", legacy);
        var sessions = new OpenBaoSessionService(client, fixture.AnonymousOptions());
        var session = await sessions.LoginAsync(user, "stale-password", CancellationToken.None);

        var scoped = new OpenBaoProjectService(
            new OpenBaoAdministrativeClient(client, options, new FixedTokenAccessor(session.Token)),
            options);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scoped.IsProtectedAsync(project, Production, CancellationToken.None));
        Assert.Contains("out of date", failure.Message);
    }

    private static EnvironmentId Production => EnvironmentId.Parse("production");

    private async Task<(ProjectId Project, ISecretsEngine Engine, IChangeRequestService Changes)> SetupAsync()
    {
        var options = fixture.RootOptions();
        var client = fixture.CreateClient();
        var admin = new OpenBaoAdministrativeClient(client, options);
        var project = ProjectId.Parse(OpenBaoFixture.NewName("approvals"));
        await new OpenBaoProjectService(admin, options).CreateAsync(project, "Approvals", CancellationToken.None);

        var engine = new OpenBaoSecretsEngine(client, new FixedTokenAccessor(OpenBaoFixture.RootToken));
        return (project, engine, new OpenBaoChangeRequestService(admin, engine, options));
    }

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
