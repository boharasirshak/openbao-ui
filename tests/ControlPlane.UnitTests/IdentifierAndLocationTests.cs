using ControlPlane.Domain;

namespace ControlPlane.UnitTests;

public sealed class IdentifierAndLocationTests
{
    [Theory]
    [InlineData("_pending")]
    [InlineData("_")]
    public void Environments_reject_the_reserved_underscore_prefix(string value)
    {
        // Control-plane paths such as pending approvals sit in the environment position,
        // so a real environment must never be able to collide with them.
        Assert.Throws<ArgumentException>(() => EnvironmentId.Parse(value));
    }

    [Fact]
    public void Environments_allow_an_underscore_that_is_not_leading()
    {
        Assert.Equal("pre_prod", EnvironmentId.Parse("pre_prod").Value);
    }

    [Theory]
    [InlineData("sys")]
    [InlineData("auth")]
    [InlineData("identity")]
    [InlineData("cubbyhole")]
    [InlineData("wrapper-metadata")]
    [InlineData("SYS")]
    public void Project_ids_reject_reserved_mounts_case_insensitively(string value)
    {
        Assert.Throws<ArgumentException>(() => ProjectId.Parse(value));
    }

    [Theory]
    [InlineData("DATABASE_URL", true)]
    [InlineData("_PRIVATE", true)]
    [InlineData("a1", true)]
    [InlineData("1LEADING_DIGIT", false)]
    [InlineData("HAS-DASH", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void Secret_keys_follow_the_environment_variable_rule(string key, bool expected)
    {
        Assert.Equal(expected, Identifier.IsValidSecretKey(key));
    }

    [Theory]
    [InlineData("../prod")]
    [InlineData("prod/%2e%2e/dev")]
    [InlineData("a/../b")]
    [InlineData("")]
    public void Try_parse_reports_failure_instead_of_throwing(string value)
    {
        // Minimal APIs bind through TryParse, so a bad path must answer 400 rather
        // than surfacing as an unhandled exception.
        Assert.False(SecretPath.TryParse(value, null, out _));
        Assert.False(ProjectId.TryParse(value, null, out _));
        Assert.False(EnvironmentId.TryParse(value, null, out _));
    }

    [Fact]
    public void Try_parse_succeeds_on_valid_input()
    {
        Assert.True(SecretPath.TryParse("services/api", null, out var path));
        Assert.Equal("services/api", path.Value);
        Assert.True(ProjectId.TryParse("checkout", null, out var project));
        Assert.Equal("checkout", project.Value);
        Assert.True(EnvironmentId.TryParse("production", null, out var environment));
        Assert.Equal("production", environment.Value);
    }

    [Fact]
    public void Secret_location_builds_every_kv_v2_path_from_one_template()
    {
        var at = new SecretLocation(
            ProjectId.Parse("checkout"),
            EnvironmentId.Parse("production"),
            SecretPath.Parse("services/api"));

        Assert.Equal("checkout/data/production/services/api", at.Data);
        Assert.Equal("checkout/metadata/production/services/api", at.Metadata);
        Assert.Equal("checkout/subkeys/production/services/api", at.Subkeys);
        Assert.Equal("checkout/delete/production/services/api", at.Delete);
        Assert.Equal("checkout/undelete/production/services/api", at.Undelete);
        Assert.Equal("checkout/destroy/production/services/api", at.Destroy);
    }

    [Fact]
    public void Secret_paths_normalise_before_reaching_a_location()
    {
        var at = new SecretLocation(
            ProjectId.Parse("checkout"),
            EnvironmentId.Parse("production"),
            SecretPath.Parse("//services///api//"));

        Assert.Equal("checkout/data/production/services/api", at.Data);
    }

    [Fact]
    public void Folder_location_addresses_the_environment_root_when_no_folder_is_given()
    {
        var project = ProjectId.Parse("checkout");
        var environment = EnvironmentId.Parse("production");

        Assert.Equal(
            "checkout/metadata/production",
            new FolderLocation(project, environment, null).Metadata);
        Assert.Equal(
            "checkout/metadata/production/services",
            new FolderLocation(project, environment, SecretPath.Parse("services")).Metadata);
    }

    [Fact]
    public void Policy_prefixes_accept_the_wildcard_that_identifiers_reject()
    {
        // ACL policies use "*" in both positions, which is not a valid identifier,
        // so the prefix helpers take strings.
        Assert.Equal("checkout/data/*", SecretLocation.DataPrefix("checkout", "*"));
        Assert.Equal("*/metadata/*", SecretLocation.MetadataPrefix("*", "*"));
    }
}
