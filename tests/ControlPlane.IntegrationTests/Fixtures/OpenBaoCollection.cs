namespace ControlPlane.IntegrationTests.Fixtures;

/// <summary>
/// Groups every test that needs a live OpenBao so they share one container.
/// Tests in this collection run sequentially, which is what keeps the shared
/// baseline stable.
/// </summary>
[CollectionDefinition(Name)]
public sealed class OpenBaoCollection : ICollectionFixture<OpenBaoFixture>
{
    public const string Name = "openbao";
}
