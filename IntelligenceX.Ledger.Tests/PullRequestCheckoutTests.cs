using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class PullRequestCheckoutTests {
    [Fact]
    public async Task CleanupPathsComeFromObservedLinksOrTheExactBranchCommitAndExcludeMissingWorkspaces() {
        var root = LedgerTestPaths.NewDirectory("pr-checkouts");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            var commit = new string('1', 40);
            var epoch = Guid.NewGuid();
            var entries = new[] { "linked", "exact", "other", "missing" }.Select((name, index) => new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = epoch, Sequence = index + 1,
                ItemId = name, MachineId = "synthetic-machine", ReporterId = "reporter", Provider = "codex", Source = "watcher", ObservedAtUtc = now,
                Session = new() { TurnOpen = true, LastActivityAtUtc = now, Repository = new("sample/project",
                    name == "linked" ? "feature/earlier" : "feature/current", "synthetic/" + name, "Synthetic " + name,
                    name == "linked" ? ["https://github.com/Sample/Project/pull/42"] : [], [], false, "origin/main",
                    name == "missing" ? "workspace-missing" : "observed", Commit: name == "other" ? new string('2', 40) : commit) }
            });
            await store.IngestAsync(entries.ToArray(), now);
            var paths = await store.PullRequestCheckoutsAsync("Sample/Project", 42, commit, "feature/current");
            Assert.Equal(new[] { "synthetic/exact", "synthetic/linked" }, paths.Select(value => value.Worktree));
            Assert.All(paths, value => Assert.Equal("synthetic-machine", value.Machine));
            Assert.Empty(await store.PullRequestCheckoutsAsync("Sample/Unrelated", 42, commit, "feature/current"));
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
