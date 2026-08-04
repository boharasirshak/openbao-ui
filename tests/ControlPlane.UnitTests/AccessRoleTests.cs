using ControlPlane.Domain;

namespace ControlPlane.UnitTests;

public sealed class AccessRoleTests
{
    private const string Mount = "wrapper-metadata";

    /// <summary>
    /// The API checks "is this environment protected" by reading the project record with
    /// the caller's own token, so a role that cannot read it cannot save anything. This
    /// broke every member write once and is invisible until you try it as a non-admin.
    /// </summary>
    [Fact]
    public void Every_role_can_read_the_project_record_it_belongs_to()
    {
        var policy = Role(RolePermissions.Auditor, "production").ToPolicy(Mount);
        Assert.Contains($"path \"{Mount}/data/projects/checkout\"", policy);
    }

    [Fact]
    public void Only_a_role_that_can_write_secrets_can_raise_a_change_request()
    {
        var editor = Role(RolePermissions.Editor, "production").ToPolicy(Mount);
        var auditor = Role(RolePermissions.Auditor, "production").ToPolicy(Mount);

        Assert.Contains($"path \"{Mount}/data/changes/checkout/*\" {{ capabilities = [\"create\"", editor);
        Assert.Contains($"path \"{Mount}/data/changes/checkout/*\" {{ capabilities = [\"list\", \"read\"] }}", auditor);
        Assert.Contains("checkout/data/_pending/production/*", editor);
        Assert.DoesNotContain("_pending", auditor);
    }


    private static AccessRole Role(RolePermissions permissions, params string[] environments) =>
        new(
            "custom",
            ProjectId.Parse("checkout"),
            environments.Select(EnvironmentId.Parse).ToList(),
            permissions);

    [Fact]
    public void Describe_alone_never_reaches_a_value()
    {
        // The narrowest useful grant: you can see the secret exists, its keys, tags and
        // when it changed, but the data path is absent so no value is reachable.
        var policy = Role(RolePermissions.Auditor, "production").ToPolicy(Mount);

        Assert.Contains("checkout/metadata/production/*", policy);
        Assert.DoesNotContain("checkout/data/production/*", policy);
    }

    [Fact]
    public void Reading_values_also_grants_describe_because_the_ui_needs_both()
    {
        var policy = Role(new RolePermissions(ReadValues: true), "production").ToPolicy(Mount);

        Assert.Contains("checkout/data/production/*", policy);
        Assert.Contains("checkout/metadata/production/*", policy);
    }

    [Fact]
    public void A_viewer_can_read_but_never_write()
    {
        var policy = Role(RolePermissions.Viewer, "production").ToPolicy(Mount);

        Assert.Contains("path \"checkout/data/production/*\"", policy);
        Assert.Contains("\"read\"", policy);
        Assert.DoesNotContain("\"create\"", policy);
        Assert.DoesNotContain("\"update\"", policy);
        Assert.DoesNotContain("\"delete\"", policy);
        // Destroy is its own path and must not appear at all.
        Assert.DoesNotContain("/destroy/", policy);
    }

    [Fact]
    public void An_editor_gets_patch_because_annotations_are_merge_patched()
    {
        var policy = Role(RolePermissions.Editor, "development").ToPolicy(Mount);
        Assert.Contains("\"patch\"", policy);
        // Editors manage details but must not be able to wipe the whole history.
        Assert.DoesNotContain("/destroy/", policy);
    }

    [Fact]
    public void Only_a_role_with_destroy_reaches_the_destroy_path()
    {
        Assert.Contains("checkout/destroy/production/*", Role(RolePermissions.Owner, "production").ToPolicy(Mount));
    }

    [Fact]
    public void Describe_grants_metadata_read_without_data_access()
    {
        var policy = Role(new RolePermissions(Describe: true), "staging").ToPolicy(Mount);

        Assert.Contains("checkout/metadata/staging/*", policy);
        Assert.DoesNotContain("checkout/data/staging/*", policy);
    }

    [Fact]
    public void Every_selected_environment_gets_its_own_paths()
    {
        var policy = Role(RolePermissions.Editor, "development", "staging").ToPolicy(Mount);

        Assert.Contains("checkout/data/development/*", policy);
        Assert.Contains("checkout/data/staging/*", policy);
        Assert.DoesNotContain("production", policy);
    }

    [Fact]
    public void The_policy_name_is_namespaced_so_it_cannot_shadow_a_system_policy()
    {
        Assert.Equal("checkout-role-custom", Role(RolePermissions.Viewer, "development").PolicyName);
    }

    [Fact]
    public void A_role_that_grants_nothing_is_recognised()
    {
        Assert.True(new RolePermissions().GrantsNothing);
        Assert.False(RolePermissions.Viewer.GrantsNothing);
    }
}
