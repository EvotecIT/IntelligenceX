using System.Diagnostics;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CheckoutCensusTests : IDisposable {
    private readonly string _directory = LedgerTestPaths.NewDirectory("checkout-census");
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Repo = "synthetic/repository";

    [Fact]
    public async Task ScanFindsWorktreesUnpushedBranchesAndStashesButNotLineEndingNoise() {
        var root = Path.Combine(_directory, "repositories");
        var origin = Path.Combine(_directory, "origin.git");
        var main = Path.Combine(root, "repository");
        var worktree = Path.Combine(_directory, "worktrees", "feature");
        Directory.CreateDirectory(root);
        await GitAsync(_directory, "init", "--bare", "-b", "main", origin);
        await GitAsync(root, "clone", origin, main);
        await File.WriteAllTextAsync(Path.Combine(main, "line-endings.txt"), "one\ntwo\n");
        await CommitAsync(main, "Synthetic base");
        await GitAsync(main, "push", "origin", "main");
        // The scanner names the repository from its remote, so point origin at a GitHub address after pushing.
        await GitAsync(main, "remote", "set-url", "origin", "https://github.com/Synthetic/Repository.git");

        await GitAsync(main, "branch", "feature/pushed-elsewhere");
        await GitAsync(main, "worktree", "add", "-b", "feature/local", worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "work.cs"), "synthetic");
        await CommitAsync(worktree, "Synthetic local work");
        await File.WriteAllTextAsync(Path.Combine(worktree, "work.cs"), "synthetic change");
        await File.WriteAllTextAsync(Path.Combine(worktree, "new.cs"), "synthetic");

        await File.WriteAllTextAsync(Path.Combine(main, "stashed.txt"), "synthetic");
        await GitAsync(main, "stash", "push", "--include-untracked", "-m", "synthetic");
        // Same text with Windows line endings: status calls it modified, but it is not work anyone would lose.
        await File.WriteAllTextAsync(Path.Combine(main, "line-endings.txt"), "one\r\ntwo\r\n");

        var census = await CheckoutScanner.ScanAsync([root], Now, CancellationToken.None);

        Assert.False(census.Truncated);
        Assert.Equal(2, census.Checkouts.Length);
        var primary = census.Checkouts[0];
        Assert.True(primary.Primary);
        Assert.Equal((Repo, "observed", "main", 0, 0, 1), (primary.Repo, primary.State, primary.Branch, primary.ChangedFiles, primary.Ahead, primary.Stashes));
        Assert.Equal(["feature/local"], primary.UnpushedBranches!);
        var feature = census.Checkouts[1];
        Assert.False(feature.Primary);
        Assert.Equal(("feature/local", 1, 1), (feature.Branch, feature.ChangedFiles, feature.UntrackedFiles));
        Assert.Null(feature.UnpushedBranches);
        Assert.Null(feature.Upstream);
    }

    [Theory]
    [InlineData("https://github.com/Synthetic/Repository.git", "synthetic/repository")]
    [InlineData("git@github.com:Synthetic/Repository.git", "synthetic/repository")]
    [InlineData("ssh://git@github.com/Synthetic/Repository", "synthetic/repository")]
    [InlineData("https://example.test/group/Repository.git", "example.test/group/repository")]
    [InlineData("C:\\synthetic\\origin.git", null)]
    [InlineData("/synthetic/origin.git", null)]
    public void RemoteNamesTheRepositoryAndLocalPathsAreSkipped(string url, string? expected) =>
        Assert.Equal(expected, CheckoutScanner.RepoFromRemote(url));

    [Fact]
    public async Task RepoActivityListsEveryScannedMachineAndIgnoresAnOlderScan() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var work = new CheckoutObservation(Repo, "C:\\repos\\repository", "observed", true, "main", ChangedFiles: 0,
            UnpushedBranches: ["feature/local"], Stashes: 1);
        await store.UpdateReporterHealthAsync(Health("win-pc", Now, new(Now, ["C:\\repos"], [work, work with {
            Path = "D:\\worktrees\\feature", Primary = false, Branch = "feature/local", ChangedFiles = 2, UnpushedBranches = null, Stashes = null }])), now: Now);
        await store.UpdateReporterHealthAsync(Health("mac", Now, new(Now, ["/repos"],
            [new("synthetic/other", "/repos/other", "observed", true, "main")])), now: Now);
        // A scan taken before the stored one arrives late and must not bring back what the newer scan replaced.
        await store.UpdateReporterHealthAsync(Health("win-pc", Now.AddMinutes(1), new(Now.AddMinutes(-15), ["C:\\repos"], [])), now: Now);

        var activity = await store.RepositoryActivityAsync(Repo, Now.AddMinutes(1));

        Assert.Equal(["mac", "win-pc"], activity.CheckoutScans!.Select(scan => scan.Machine));
        Assert.Equal(2, activity.Checkouts!.Length);
        Assert.All(activity.Checkouts, row => Assert.Equal("win-pc", row.Machine));
        Assert.Contains("Checkouts: mac 0, win-pc 2.", activity.Summary);
        Assert.Contains("win-pc repository (unpushed feature/local, 1 stash)", activity.Summary);
        Assert.Contains("win-pc feature (2 changed)", activity.Summary);
        // The compact answer leaves out zero counts.
        Assert.Null(activity.Checkouts[0].Checkout.ChangedFiles);
    }

    [Fact]
    public void ServiceRejectsScansOutsideTheReporterBounds() {
        var valid = new CheckoutCensus(Now, ["C:\\repos"], [new(Repo, "C:\\repos\\repository", "observed", true)]);
        Assert.True(LedgerStore.ValidCheckoutCensus(valid, Now));
        Assert.True(LedgerStore.ValidCheckoutCensus(null, Now));
        Assert.False(LedgerStore.ValidCheckoutCensus(valid with { ObservedAtUtc = Now.AddMinutes(5) }, Now));
        Assert.False(LedgerStore.ValidCheckoutCensus(valid with { Checkouts = [valid.Checkouts[0] with { State = "unknown" }] }, Now));
        Assert.False(LedgerStore.ValidCheckoutCensus(valid with { Checkouts = [valid.Checkouts[0] with { Branch = "bad\nbranch" }] }, Now));
        Assert.False(LedgerStore.ValidCheckoutCensus(valid with { Checkouts = [valid.Checkouts[0] with { Ahead = -1 }] }, Now));
        Assert.False(LedgerStore.ValidCheckoutCensus(valid with {
            Checkouts = Enumerable.Repeat(valid.Checkouts[0], CheckoutScanner.MaximumCheckouts + 1).ToArray() }, Now));
    }

    private static ReporterHealth Health(string machine, DateTimeOffset at, CheckoutCensus census) =>
        new(machine, "reporter-" + machine, "synthetic", at, []) { Checkouts = census };

    private static async Task CommitAsync(string directory, string message) {
        await GitAsync(directory, "add", "--all");
        await GitAsync(directory, "-c", "user.name=Synthetic", "-c", "user.email=synthetic@example.test",
            "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", message);
    }

    private static async Task GitAsync(string directory, params string[] arguments) {
        using var process = new Process { StartInfo = new("git") { WorkingDirectory = directory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        Assert.True(process.ExitCode == 0, string.Join(' ', arguments) + ": " + await error);
    }

    public void Dispose() {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_directory, true);
    }
}
