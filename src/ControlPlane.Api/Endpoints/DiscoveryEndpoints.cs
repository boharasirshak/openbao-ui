using ControlPlane.Application;
using ControlPlane.Contracts;
using ControlPlane.Domain;

namespace ControlPlane.Api.Endpoints;

/// <summary>
/// Cross-environment views: searching a whole project and comparing one path across
/// environments. Both compose several OpenBao reads, which is why they live server-side
/// rather than as a fan-out from the browser.
/// </summary>
public static class DiscoveryEndpoints
{
    private const int MaxHits = 200;

    public static void MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var project = app.MapGroup("/api/projects/{project}").RequireAuthorization();

        project.MapGet(
                "/search",
                async (
                    ProjectId project,
                    string? q,
                    IProjectService projects,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    var query = (q ?? string.Empty).Trim();
                    if (query.Length == 0)
                    {
                        return Results.Ok(new SecretSearchResponse([], false));
                    }

                    var hits = new List<SecretSearchHit>();
                    var truncated = false;

                    foreach (var environment in await EnvironmentsAsync(projects, project, cancellationToken))
                    {
                        IReadOnlyList<string> paths;
                        try
                        {
                            paths = await engine.ScanAsync(project, environment, null, cancellationToken);
                        }
                        catch (HttpRequestException)
                        {
                            // No read access to this environment. Skipping it is the
                            // correct answer: search must never reveal a path the
                            // caller could not have listed.
                            continue;
                        }

                        foreach (var path in paths)
                        {
                            if (!path.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (hits.Count == MaxHits)
                            {
                                truncated = true;
                                break;
                            }

                            hits.Add(new SecretSearchHit(environment.Value, path));
                        }
                    }

                    return Results.Ok(new SecretSearchResponse(hits, truncated));
                })
            .Produces<SecretSearchResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        project.MapGet(
                "/compare",
                async (
                    ProjectId project,
                    string path,
                    string? environments,
                    IProjectService projects,
                    ISecretsEngine engine,
                    CancellationToken cancellationToken) =>
                {
                    if (!SecretPath.TryParse(path, null, out var secretPath))
                    {
                        return Results.BadRequest();
                    }

                    var requested = ParseEnvironments(environments);
                    var candidates = requested.Count > 0
                        ? requested
                        : await EnvironmentsAsync(projects, project, cancellationToken);

                    var snapshots = new List<EnvironmentSnapshot>();
                    foreach (var environment in candidates)
                    {
                        snapshots.Add(await SnapshotAsync(engine, project, environment, secretPath, cancellationToken));
                    }

                    return Results.Ok(new CompareResponse(secretPath.Value, snapshots));
                })
            .Produces<CompareResponse>()
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<EnvironmentSnapshot> SnapshotAsync(
        ISecretsEngine engine,
        ProjectId project,
        EnvironmentId environment,
        SecretPath path,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await engine.ReadAsync(project, environment, path, cancellationToken);
            return document is null
                ? new EnvironmentSnapshot(environment.Value, true, false, 0, new Dictionary<string, string>())
                : new EnvironmentSnapshot(environment.Value, true, true, document.Version, document.Values);
        }
        catch (HttpRequestException)
        {
            // A locked environment is a first-class result, not an error: the compare
            // view shows it as inaccessible and excludes it from the diff.
            return new EnvironmentSnapshot(environment.Value, false, false, 0, new Dictionary<string, string>());
        }
    }

    private static List<EnvironmentId> ParseEnvironments(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => EnvironmentId.TryParse(name, null, out var parsed) ? parsed : (EnvironmentId?)null)
            .OfType<EnvironmentId>()
            .ToList();

    /// <summary>
    /// Listing projects is administrator-only, so a member falls back to the standard
    /// environment set rather than being told nothing exists.
    /// </summary>
    private static async Task<IReadOnlyList<EnvironmentId>> EnvironmentsAsync(
        IProjectService projects,
        ProjectId project,
        CancellationToken cancellationToken)
    {
        try
        {
            var known = await projects.ListAsync(cancellationToken);
            var match = known.FirstOrDefault(candidate => candidate.Id == project);
            if (match is not null && match.Environments.Count > 0)
            {
                return match.Environments.Select(environment => environment.Id).ToList();
            }
        }
        catch (HttpRequestException)
        {
            // Falls through to the defaults below.
        }

        return
        [
            EnvironmentId.Parse("development"),
            EnvironmentId.Parse("staging"),
            EnvironmentId.Parse("production"),
        ];
    }
}
