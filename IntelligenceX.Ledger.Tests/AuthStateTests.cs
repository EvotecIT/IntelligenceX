using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class AuthStateTests : IDisposable {
    private readonly string _directory = LedgerTestPaths.NewDirectory("auth-state");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static ReporterHealth Health(DateTimeOffset at, params AuthFact[] facts) =>
        new("win-pc", "win-pc-watcher", "v1", at, []) { Auth = facts };

    private static AuthFact GitHub(DateTimeOffset at, string scopes, string state = "signed-in") =>
        new("github", state, at) { Facts = new() { ["account"] = "someone", ["scopes"] = scopes, ["token-source"] = "keyring" } };

    private static LedgerEvent SignIn(bool required, string reason, DateTimeOffset at, long sequence) => new() {
        EventId = Guid.NewGuid(), Epoch = new Guid("00000000-0000-0000-0000-000000000002"), Sequence = sequence, MachineId = "win-pc",
        ReporterId = "win-pc-watcher", ItemId = "win-pc:sign-in:codex", ItemKind = "sign-in", Provider = "codex", Source = "watcher",
        ObservedAtUtc = at, SignIn = new("codex", required, reason, "codex login", at, at)
    };

    // The reason for the history: a sudden sign-out or lost scope has to be traceable to when and what changed,
    // without the first reading after a deploy flooding it.
    [Fact]
    public async Task ChangesAreRecordedAgainstThePreviousReadingWithTheAgentsSignInFoldedIn() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var codex = new AuthFact("codex", "unknown", Now) { Facts = new() { ["version"] = "0.70.0" } };
        await store.IngestAsync([SignIn(false, "signed-in", Now, 1)], Now);
        await store.UpdateReporterHealthAsync(Health(Now, GitHub(Now, "repo, workflow"), codex));
        var expected = new Dictionary<string, string[]> { ["win-pc"] = ["win-pc-watcher"], ["mac"] = ["mac-watcher"] };
        var first = await store.AuthStateAsync(Now, expected, TimeSpan.FromMinutes(10), null, 50);
        Assert.Empty(first.Changes);
        Assert.Equal("signed-in", first.Machines.Single(m => m.Machine == "win-pc").Providers.Single(p => p.Provider == "codex").State);
        Assert.Contains("no-auth-facts:mac", first.Coverage);

        var later = Now.AddMinutes(5);
        await store.IngestAsync([SignIn(true, "signed-out", later, 2)], later);
        await store.UpdateReporterHealthAsync(Health(later, GitHub(later, "repo"),
            codex with { CheckedAtUtc = later, Facts = new() { ["version"] = "0.70.0", ["versions"] = "0.64.0, 0.70.0" } }));
        var view = await store.AuthStateAsync(later, expected, TimeSpan.FromMinutes(10), "win-pc", 50);
        var changes = view.Changes.Select(c => (c.Provider, c.Field, c.Before, c.After)).ToArray();
        Assert.Contains(("github", "scopes", "repo, workflow", "repo"), changes);
        Assert.Contains(("codex", "state", "signed-in", "signed-out"), changes);
        Assert.Contains(("codex", "agent-sign-in", "ready", "signed-out"), changes);
        Assert.Contains(("codex", "versions", null, "0.64.0, 0.70.0"), changes);
        Assert.Equal(4, changes.Length);
        var machine = Assert.Single(view.Machines);
        Assert.Equal(("healthy", "v1"), (machine.Reporter, machine.SourceVersion));
        Assert.True(Assert.Single(machine.SignIn).Required);

        // An unchanged reading adds nothing.
        await store.UpdateReporterHealthAsync(Health(later.AddMinutes(1), GitHub(later.AddMinutes(1), "repo"),
            codex with { CheckedAtUtc = later, Facts = new() { ["version"] = "0.70.0", ["versions"] = "0.64.0, 0.70.0" } }));
        Assert.Equal(4, (await store.AuthStateAsync(later, expected, TimeSpan.FromMinutes(10), null, 50)).Changes.Length);
    }

    [Theory]
    [InlineData("usage-limit", "signed-in")]
    [InlineData("expired", "signed-out")]
    [InlineData("unknown", "unknown")]
    public void AgentReasonsMapToAState(string reason, string state) {
        var merged = LedgerStore.Merge(new("claude", "unknown", Now), new("claude", true, reason, "claude auth login", Now, Now));
        Assert.Equal((state, reason), (merged.State, merged.Facts!["agent-sign-in"]));
        var inconclusive = LedgerStore.Merge(new("codex", "signed-in", Now), new("codex", false, "unknown", "codex login", Now, Now));
        Assert.Equal(("unknown", "unknown"), (inconclusive.State, inconclusive.Facts!["agent-sign-in"]));
        Assert.Equal("not-installed", LedgerStore.Merge(new("claude", "not-installed", Now), new("claude", false, "signed-in", "x", Now, Now)).State);
    }

    [Fact]
    public void GitHubStatusGivesAccountScopesAndTokenSourceWithoutAToken() {
        var facts = new Dictionary<string, string>();
        var state = AuthStateCollector.ParseGitHub("""
            {"hosts":{"github.com":[{"state":"success","active":true,"host":"github.com","login":"someone","tokenSource":"GH_TOKEN",
            "scopes":"workflow, repo, admin:org","gitProtocol":"https"},{"state":"success","active":false,"login":"bot"}]}}
            """, facts);
        Assert.Equal("signed-in", state);
        Assert.Equal(("someone", "GH_TOKEN", "admin:org, repo, workflow", "bot, someone"),
            (facts["account"], facts["token-source"], facts["scopes"], facts["accounts"]));

        Assert.Equal("signed-out", AuthStateCollector.ParseGitHub("""{"hosts":{"github.com":[{"state":"error","active":true,"login":"someone"}]}}""", facts = []));
        Assert.Equal("error", facts["check"]);
        Assert.Equal("signed-out", AuthStateCollector.ParseGitHub("""{"hosts":{}}""", []));
        Assert.Equal("unknown", AuthStateCollector.ParseGitHub("""{"hosts":{"github.com":[{"state":"timeout","active":true}]}}""", []));
        Assert.Equal("unknown", AuthStateCollector.ParseGitHub("not json", []));
    }

    [Fact]
    public async Task CollectorListsEveryBinaryAndVersionAndRereadsOnlyWhenSomethingChanges() {
        var home = Path.Combine(_directory, "home");
        var bin = Path.Combine(_directory, "bin");
        var other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(Path.Combine(home, ".codex"));
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(other);
        string Tool(string folder, string name) {
            var path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
            File.WriteAllText(path, folder);
            return path;
        }
        var gh = Tool(bin, "gh");
        var codexNew = Tool(bin, "codex");
        var codexOld = Tool(other, "codex");
        File.WriteAllText(Path.Combine(home, ".codex", "auth.json"), "credential-value");
        var runs = new List<string>();
        var clock = Now;
        var paths = new AuthPaths(home, (name, target) => name == "PATH" && target == EnvironmentVariableTarget.Process ? bin + Path.PathSeparator + other
            : name == "GH_TOKEN" && target == EnvironmentVariableTarget.Process ? "value-never-read-out" : null, installFolders: false);
        var collector = new AuthStateCollector(paths, (path, args, _) => {
            runs.Add(Path.GetFileName(Path.GetDirectoryName(path)) + "/" + string.Join(' ', args));
            return Task.FromResult<(int?, string)>(args[0] == "--version" ? (0, path == codexOld ? "codex-cli 0.64.0" : "codex-cli 0.70.1-alpha.2")
                : (0, """{"hosts":{"github.com":[{"state":"success","active":true,"login":"someone","scopes":"repo"}]}}"""));
        }, () => clock);

        var facts = await collector.CollectAsync(CancellationToken.None);
        var codex = facts.Single(f => f.Provider == "codex");
        Assert.Equal(("unknown", "0.70.1-alpha.2", "0.64.0, 0.70.1-alpha.2", "file"),
            (codex.State, codex.Facts!["version"], codex.Facts["versions"], codex.Facts["credentials"]));
        Assert.Equal("not-installed", facts.Single(f => f.Provider == "claude").State);
        var github = facts.Single(f => f.Provider == "github");
        Assert.Equal(("signed-in", "GH_TOKEN (process)"), (github.State, github.Facts!["env-override"]));
        Assert.DoesNotContain(facts.SelectMany(f => f.Facts!.Values), value => value.Contains("credential-value") || value.Contains("value-never-read-out"));
        Assert.True(AuthStateValidation.Valid(facts, Now));

        var count = runs.Count;
        clock = Now.AddMinutes(1);
        await collector.CollectAsync(CancellationToken.None);
        Assert.Equal(count, runs.Count);

        // Signing out of Codex removes auth.json; the next minute's check notices and reads again at once.
        File.Delete(Path.Combine(home, ".codex", "auth.json"));
        clock = Now.AddMinutes(2);
        facts = await collector.CollectAsync(CancellationToken.None);
        Assert.Equal("signed-out", facts.Single(f => f.Provider == "codex").State);
        Assert.Equal(count + 1, runs.Count); // gh status again; versions come from the cache.
    }

    [Fact]
    public void ValidationKeepsFactsSmallAndNamed() {
        var fact = new AuthFact("github", "signed-in", Now) { Facts = new() { ["scopes"] = "repo" } };
        Assert.True(AuthStateValidation.Valid([fact], Now));
        Assert.False(AuthStateValidation.Valid([fact, fact], Now));
        Assert.False(AuthStateValidation.Valid([fact with { State = "fine" }], Now));
        Assert.False(AuthStateValidation.Valid([fact with { Facts = new() { ["Not A Key"] = "x" } }], Now));
        Assert.False(AuthStateValidation.Valid([fact with { Facts = new() { ["scopes"] = new string('x', AuthStateValidation.MaximumValue + 1) } }], Now));
        Assert.False(AuthStateValidation.Valid([fact with { CheckedAtUtc = Now.AddHours(1) }], Now));
    }

    [Fact]
    public void CredentialStoreIsReadOnlyFromTheTopLevel() {
        var config = Path.Combine(_directory, "config.toml");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(config, "model = \"x\"\ncli_auth_credentials_store = \"keyring\" # here\n[profiles.a]\ncli_auth_credentials_store = \"file\"\n");
        Assert.Equal("keyring", AuthStateCollector.CredentialStore(config));
        File.WriteAllText(config, "[profiles.a]\ncli_auth_credentials_store = \"file\"\n");
        Assert.Null(AuthStateCollector.CredentialStore(config));
        Assert.Equal(12, AuthStateCollector.Fingerprint(config)!.Length);
    }

    public void Dispose() {
        try { Directory.Delete(_directory, recursive: true); } catch (DirectoryNotFoundException) { }
    }
}
