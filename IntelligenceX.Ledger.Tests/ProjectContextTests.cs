using System.Text.Json;
using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ProjectContextTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledger-context-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private string CatalogPath => Path.Combine(_root, "catalog.json");

    public ProjectContextTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SearchReturnsCitedRejectedExperimentAndIsolatesProjectSources() {
        var rejected = Source("rejected", "# Rejected experiment\r\nBuffered encoding reduced one case but increased other allocations.", "rejected");
        var other = Source("other", "# Other experiment\nBuffered encoding is a private result belonging to a different project.");
        Save(Project("first", rejected), Project("second", other));
        var service = new ProjectContextService(CatalogPath, _clock);

        var result = await service.SearchAsync("first", "buffered encoding");

        var hit = Assert.Single(result!.Hits);
        Assert.Equal("first", hit.ProjectId);
        Assert.Equal("rejected", hit.Outcome);
        Assert.Equal("Rejected experiment", hit.Section);
        Assert.Contains("increased other allocations", hit.Excerpt);
        Assert.Equal("https://example.test/source/rejected", hit.SourceRef);
        Assert.Equal(64, hit.Revision.Length);
        Assert.DoesNotContain("different project", hit.Excerpt);
        Assert.Null(await service.SearchAsync("unknown", "buffered"));
    }

    [Fact]
    public async Task ExcerptIncludesTheDecisionAfterALongMeasurementTable() {
        Save(Project("first", Source("one", "# Export experiment\n" +
            string.Concat(Enumerable.Repeat("| Plain export | timing | 25 ms |\n", 50)) +
            "The plain export target was 50 percent; the measured result falls short. Keep the current default.")));
        var result = await new ProjectContextService(CatalogPath, _clock).SearchAsync("first", "plain export target");
        var hit = Assert.Single(result!.Hits);
        Assert.Contains("falls short", hit.Excerpt);
        Assert.Contains("Keep the current default", hit.Excerpt);
        Assert.True(hit.Excerpt.Length <= 480);
    }

    [Fact]
    public async Task SourceChangesDeletionAndSupersessionReplaceCachedSearchEvidence() {
        var source = Source("current", "# Trial\nFirst algorithm retained the original result.");
        var superseded = Source("old", "# Trial\nFirst algorithm should be retried.") with { SupersededBy = "current" };
        Save(Project("first", source, superseded));
        var service = new ProjectContextService(CatalogPath, _clock);
        var first = Assert.Single((await service.SearchAsync("first", "algorithm"))!.Hits);
        File.WriteAllText(source.Path, "# Trial\nSecond algorithm has a corrected result.");
        _clock.Now += TimeSpan.FromSeconds(31);

        var corrected = Assert.Single((await service.SearchAsync("first", "algorithm"))!.Hits);

        Assert.NotEqual(first.Revision, corrected.Revision);
        Assert.Contains("corrected result", corrected.Excerpt);
        File.Delete(source.Path);
        _clock.Now += TimeSpan.FromSeconds(31);
        var unavailable = await service.SearchAsync("first", "algorithm");
        Assert.Empty(unavailable!.Hits);
        Assert.Contains("first:source-unavailable:current", unavailable.Coverage);
    }

    [Fact]
    public async Task InvalidCatalogFailsClosedAndDoesNotRetainPreviouslyReadableSources() {
        Save(Project("first", Source("one", "# Evidence\nRetained experiment.")));
        var service = new ProjectContextService(CatalogPath, _clock);
        Assert.Single((await service.SearchAsync("first", "experiment"))!.Hits);
        File.WriteAllText(CatalogPath, "{\"schemaVersion\":99,\"projects\":null}");
        _clock.Now += TimeSpan.FromSeconds(31);

        var directory = await service.ProjectsAsync();
        Assert.Empty(directory.Projects);
        Assert.Contains("project-directory-unavailable", directory.Coverage);
        await Assert.ThrowsAsync<ProjectDirectoryUnavailableException>(() => service.SearchAsync("first", "experiment"));
    }

    [Fact]
    public async Task OversizedSourceAndStaleDirectoryRemainExplicitCoverage() {
        var source = Source("large", new string('a', 1024 * 1024 + 1));
        Save(Project("first", source));
        _clock.Now += TimeSpan.FromMinutes(11);
        var service = new ProjectContextService(CatalogPath, _clock);

        var project = await service.ProjectAsync("first", "a");

        Assert.Empty(project!.Knowledge);
        Assert.Contains("first:source-unavailable:large", project.Coverage);
        Assert.Contains("directory-source-stale", project.Coverage);
        Assert.Equal(_clock.Now.AddMinutes(-11), project.AsOfUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    public async Task SearchRejectsQueriesWithoutSearchableTerms(string query) {
        Save(Project("first", Source("one", "# Evidence\nExperiment.")));
        var service = new ProjectContextService(CatalogPath, _clock);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync("first", query));
    }

    [Fact]
    public void GeneratedClientContractReadsWorkWithoutReflection() {
        var now = _clock.Now;
        var view = JsonSerializer.Deserialize("""
            {"asOfUtc":"2026-01-12T12:00:00Z","rulesVersion":2,"machines":[],
             "counts":{"working":1},"items":[],"coverage":["synthetic-coverage"],"truncated":false}
            """, LedgerReadJson.Default.LedgerWorkView);
        Assert.Equal(now, view!.AsOfUtc);
        Assert.Equal(1, view.Counts["working"]);
        Assert.Equal("synthetic-coverage", Assert.Single(view.Coverage));
    }

    [Theory]
    [InlineData("http://192.0.2.1/")]
    [InlineData("https://example.test/path")]
    [InlineData("https://user@example.test/")]
    [InlineData("https://example.test/?token=value")]
    public void ClientRejectsUnsafeOrAmbiguousCredentialDestinations(string endpoint) {
        Assert.Throws<InvalidDataException>(() => new LedgerClientSettings { Endpoint = endpoint, ReadToken = new('r', 32) }.Validate());
    }

    private KnowledgeSource Source(string id, string content, string outcome = "reference") {
        var path = Path.Combine(_root, id + ".md");
        File.WriteAllText(path, content);
        return new() { Id = id, Title = "Synthetic " + id, Path = path, SourceRef = "https://example.test/source/" + id, Outcome = outcome };
    }

    private static ProjectDefinition Project(string id, params KnowledgeSource[] sources) =>
        new() { Id = id, Name = "Synthetic " + id, Repository = "example/" + id, Sources = sources };

    private void Save(params ProjectDefinition[] projects) => File.WriteAllText(CatalogPath,
        JsonSerializer.Serialize(new ProjectCatalog { ObservedAtUtc = _clock.Now, Projects = projects }, LedgerJson.Options));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Clock : TimeProvider {
        internal DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
