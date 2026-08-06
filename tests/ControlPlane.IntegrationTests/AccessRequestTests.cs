using ControlPlane.Application;
using ControlPlane.Domain;
using ControlPlane.Infrastructure.OpenBao;
using ControlPlane.IntegrationTests.Fixtures;

namespace ControlPlane.IntegrationTests;

/// <summary>
/// The interesting property is the member-base grant: someone with no project access
/// at all must be able to file a request with their own token, and must not be able
/// to read anyone else's. That is enforced by OpenBao, so it needs a live check.
/// </summary>
[Collection(OpenBaoCollection.Name)]
public sealed class AccessRequestTests(OpenBaoFixture fixture)
{
    [Fact]
    public async Task Someone_with_no_access_can_ask_and_approval_grants_exactly_the_asked_roles()
    {
        var options = fixture.RootOptions();
        var rootClient = fixture.CreateClient();
        var admin = new OpenBaoAdministrativeClient(rootClient, options);
        var identity = new OpenBaoIdentityService(admin, options);
        var project = ProjectId.Parse(OpenBaoFixture.NewName("askable"));
        await new OpenBaoProjectService(admin, options).CreateAsync(project, "Askable", CancellationToken.None);

        // A brand-new account with no roles anywhere. CreateAsync gives it member-base.
        var username = OpenBaoFixture.NewName("newcomer");
        await identity.CreateAsync(username, "newcomer-password", [], CancellationToken.None);

        // The requester acts with their own token, not a privileged one.
        using var requesterClient = fixture.CreateClient();
        var sessions = new OpenBaoSessionService(requesterClient, fixture.AnonymousOptions());
        var session = await sessions.LoginAsync(username, "newcomer-password", CancellationToken.None);
        var requesterAdmin = new OpenBaoAdministrativeClient(
            requesterClient,
            fixture.AnonymousOptions(),
            new FixedTokenAccessor(session.Token));
        var requesterService = new OpenBaoAccessRequestService(
            requesterAdmin,
            identity,
            fixture.AnonymousOptions());

        var wanted = ProjectPolicy.Environment(project, EnvironmentId.Parse("staging"), readOnly: true);
        await requesterService.SubmitAsync(
            new AccessRequest(
                project,
                username,
                [wanted],
                "need to read staging for on-call",
                DateTimeOffset.UtcNow,
                AccessRequestStatus.Pending),
            CancellationToken.None);

        // Write-only means exactly that: the requester cannot read back even their own
        // request, let alone anyone else's.
        await Assert.ThrowsAsync<HttpRequestException>(() => requesterAdmin.GetAsync(
            $"v1/{ControlPlanePaths.AccessRequest("wrapper-metadata", project.Value, username)}",
            CancellationToken.None));

        // The reviewer sees it and cannot be the requester.
        var reviewerService = new OpenBaoAccessRequestService(admin, identity, options);
        var pending = await reviewerService.ListAsync(project, CancellationToken.None);
        var request = Assert.Single(pending);
        Assert.Equal(username, request.Username);
        Assert.True(request.IsOpen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reviewerService.ApproveAsync(project, username, username, CancellationToken.None));

        var approved = await reviewerService.ApproveAsync(project, username, "admin", CancellationToken.None);
        Assert.Equal(AccessRequestStatus.Approved, approved.Status);

        // Approval merged the asked role into the account without dropping member-base.
        var member = (await identity.ListAsync(CancellationToken.None))
            .Single(candidate => candidate.Username == username);
        Assert.Contains(wanted, member.Policies);
        Assert.Contains(MemberBasePolicy.Name, member.Policies);

        // Closed is closed.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reviewerService.ApproveAsync(project, username, "admin", CancellationToken.None));
    }

    private sealed class FixedTokenAccessor(string token) : IOpenBaoTokenAccessor
    {
        public string GetRequiredToken() => token;
    }
}
