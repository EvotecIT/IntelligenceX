using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AgentPullRequestAssociationTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-pr-association", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-07T08:00:00Z");
    private const string Target = "https://github.com/Sample/Project/pull/17";
    private string Log => Path.Combine(_directory, "synthetic.jsonl");
    private SourceRootRecord Root => new("synthetic-root", "claude", UsageSourceKind.LocalLogs, _directory);
    private const string Initial = """
        {"type":"user","sessionId":"synthetic","cwd":"/unavailable/synthetic","gitBranch":"main","timestamp":"2026-11-07T07:59:00Z","message":{"content":"https://github.com/Sample/Project/pull/99"}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderAssociation_LinksDifferentBranchAfterTailAndCacheReuseWithoutInventingActivity(bool largeRecord) {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Log, Initial + "\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore(), ActiveWithin = null };
        var collection = new AgentLocalCollection(options);
        collection.Reconcile(new[] { Root }, Now, default);
        var before = Assert.Single(collection.Snapshot(Array.Empty<AgentSessionState>(), Now).Sessions);
        Assert.Empty(Link(before).Sessions.Single().PullRequests);
        var record = JsonSerializer.Serialize(new { type = "pr-link", sessionId = "synthetic", prNumber = 17,
            prRepository = "Sample/Project", prUrl = Target, timestamp = Now.ToString("O"),
            padding = largeRecord ? "private sentinel " + new string('x', 300 * 1024) : "private sentinel" });
        File.AppendAllText(Log, record + "\n" + record + "\n");
        Assert.True(collection.Update(Root.Id, Log, default));
        var snapshot = collection.Snapshot(Array.Empty<AgentSessionState>(), Now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("main", session.Branch);
        Assert.Equal(before.LastActivityAtUtc, session.LastActivityAtUtc);
        Assert.Equal(Target, Assert.Single(session.ProviderPullRequestUrls));
        Assert.Equal(Target, Assert.Single(Assert.Single(Link(session).Sessions).PullRequests).Url);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
        Assert.True(snapshot.Scan!.FilesTailed > 0);
        // Reconcile flushes append state through the normal persistent-cache path.
        collection.Reconcile(new[] { Root }, Now, default);
        var restored = LocalAgentWorkStateService.ReadLocal(new[] { Root }, Now, options: options);
        Assert.Equal(1, restored.Scan!.FilesReused);
        Assert.Equal(Target, Assert.Single(Assert.Single(restored.Sessions).ProviderPullRequestUrls));
        Assert.Empty(restored.Diagnostics);
        var closed = AgentWorkStateRules.Evaluate(restored.Sessions,
            new[] { new AgentPullRequestState { Url = Target, IsOpen = false, IsMerged = true } }, Now);
        Assert.Empty(Assert.Single(closed.Sessions).PullRequests);
    }

    [Theory]
    [InlineData("other-session", 17, "Sample/Project", Target)]
    [InlineData("synthetic", 18, "Sample/Project", Target)]
    [InlineData("synthetic", 17, "Other/Project", Target)]
    [InlineData("synthetic", 17, "Sample/Project", "https://private-sentinel@github.com/Sample/Project/pull/17")]
    public void ProviderAssociation_RejectsForeignIdentityAndInconsistentOrCredentialBearingLinks(string id, int number, string repository, string url) {
        Directory.CreateDirectory(_directory);
        var link = JsonSerializer.Serialize(new { type = "pr-link", sessionId = id, prNumber = number, prRepository = repository, prUrl = url });
        File.WriteAllText(Log, Initial + "\n" + link + "\n");
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root }, Now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("synthetic", session.Id);
        Assert.Empty(session.ProviderPullRequestUrls);
        Assert.Empty(Assert.Single(Link(session).Sessions).PullRequests);
        Assert.DoesNotContain("private-sentinel", AgentWorkJson.Serialize(snapshot));
    }

    private static AgentWorkSnapshot Link(AgentSessionState session) => AgentWorkStateRules.Evaluate(new[] { session },
        new[] { new AgentPullRequestState { Url = Target, HeadRepository = "Sample/Project", Branch = "feature/worktree" },
            new AgentPullRequestState { Url = "https://github.com/Sample/Project/pull/99", HeadRepository = "Sample/Project", Branch = "unrelated" } }, Now);

    [Theory]
    [InlineData("synthetic", true)]
    [InlineData("other-session", false)]
    public void EarlyProviderAssociation_UsesRecordedSessionIdentityAcrossRenamedFileAndCachedTail(string recordedId, bool matches) {
        Directory.CreateDirectory(_directory);
        var renamed = Path.Combine(_directory, "other-session.jsonl");
        var link = JsonSerializer.Serialize(new { type = "pr-link", sessionId = recordedId, prNumber = 17,
            prRepository = "Sample/Project", prUrl = Target });
        File.WriteAllText(renamed, link + "\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore(), ActiveWithin = null };
        LocalAgentWorkStateService.ReadLocal(new[] { Root }, Now, options: options);
        File.AppendAllText(renamed, Initial + "\n");
        var tailed = LocalAgentWorkStateService.ReadLocal(new[] { Root }, Now, options: options);
        Assert.Equal(1, tailed.Scan!.FilesTailed);
        var session = Assert.Single(tailed.Sessions);
        Assert.Equal("synthetic", session.Id);
        Assert.Equal(matches ? new[] { Target } : Array.Empty<string>(), session.ProviderPullRequestUrls);
        Assert.Equal(matches ? 1 : 0, Assert.Single(Link(session).Sessions).PullRequests.Count);
        var cold = LocalAgentWorkStateService.ReadLocal(new[] { Root }, Now);
        Assert.Equal(session.ProviderPullRequestUrls, Assert.Single(cold.Sessions).ProviderPullRequestUrls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"providerPullRequestUrls\":null")]
    public void OlderSnapshotWithoutProviderAssociations_StillLinksByBranch(string optionalProperty) {
        var json = "{\"sessions\":[{\"provider\":\"codex\",\"id\":\"synthetic\",\"repository\":\"Sample/Project\",\"branch\":\"feature/worktree\""
            + optionalProperty + "}]}";
        var snapshot = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
        var session = Assert.Single(snapshot.Sessions);
        Assert.Empty(session.ProviderPullRequestUrls);
        Assert.Empty(session.PendingInputs);
        Assert.Empty(session.ResolvedInputs);
        Assert.Empty(session.PullRequests);
        Assert.Equal(Target, Assert.Single(Assert.Single(Link(session).Sessions).PullRequests).Url);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
