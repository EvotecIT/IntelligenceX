using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Tray;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerTrayTests {
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static AuthMachine Machine(string id, params AuthFact[] facts) => new(id, "healthy", Now, "v1", facts, []);

    private static AuthFact Fact(string provider, string state, Dictionary<string, string>? facts = null) => new(provider, state, Now) { Facts = facts ?? [] };

    private static Overview Overview(AuthMachine[] machines, AuthChange[]? changes = null) => new(Now, new("ok", null, Now.AddHours(-3)) { Commit = "71b01fdf96b0" },
        null, new(Now, machines, changes ?? [], []), null, null, null);

    [Fact]
    public void AllSignedInIsHealthyAndTheIconsStatesFollowWhatNeedsALook() {
        var good = Machine("win-pc", Fact("github", "signed-in", new() { ["account"] = "someone", ["scopes"] = "repo, workflow" }),
            Fact("codex", "signed-in"), Fact("claude", "signed-in"));
        var view = TrayView.Build(Overview([good]), "win-pc", Now);
        Assert.Equal(TrayState.Healthy, view.State);
        Assert.Contains("build 71b01fd", view.LedgerLines[0]);
        Assert.Equal("someone · 2 scopes", string.Join(" · ", view.ThisMachine!.Providers[0].Summary.Split(" · ").Skip(1)));

        // Signed out anywhere, or a token variable overriding gh's stored sign-in, needs a look.
        var mac = Machine("mac", Fact("github", "signed-in", new() { ["env-override"] = "GH_TOKEN (user)" }), Fact("codex", "signed-out"), Fact("claude", "unknown"));
        view = TrayView.Build(Overview([good, mac]), "win-pc", Now);
        Assert.Equal(TrayState.Degraded, view.State);
        Assert.Contains("mac Codex signed out", view.Problems);
        Assert.Contains("mac GitHub: GH_TOKEN overrides the stored sign-in", view.Problems);
        Assert.Equal([TrayView.Warn, TrayView.Bad, TrayView.Unknown], Assert.Single(view.Others).Providers.Select(p => p.Mark));
        Assert.True(view.Others[0].Providers[1].NeedsSignIn);

        var down = TrayView.Build(new(Now, null, "Ledger isn't answering", null, null, null, null), "win-pc", Now);
        Assert.Equal((TrayState.Down, "Ledger isn't answering"), (down.State, down.Headline));
    }

    [Fact]
    public void ChangesReadAsWhatChangedFromWhatToWhat() {
        var change = new AuthChange("mac", "github", Now.AddMinutes(-5), "scopes", "gist, repo", "repo");
        Assert.EndsWith("mac · GitHub scopes: gist, repo → repo", TrayView.Change(change, Now));
        Assert.EndsWith("mac · Codex: signed-in → signed-out", TrayView.Change(change with { Provider = "codex", Field = "state", Before = "signed-in", After = "signed-out" }, Now));
    }

    // Notifications are for real changes only: nothing for what was already recorded at start, nothing for file times,
    // and a Ledger outage only after two failed polls in a row.
    [Fact]
    public void NotifierAnnouncesNewNotableChangesOnceAndDebouncesAnOutage() {
        var notifier = new ChangeNotifier();
        var old = new AuthChange("mac", "codex", Now.AddDays(-1), "state", "signed-in", "signed-out");
        Assert.Empty(notifier.Next(Overview([], [old]), Now));

        var written = new AuthChange("mac", "codex", Now, "config-written", "a", "b");
        var scopes = new AuthChange("win-pc", "github", Now, "scopes", "repo, workflow", "repo");
        var notes = notifier.Next(Overview([], [scopes, written, old]), Now);
        var note = Assert.Single(notes);
        Assert.Equal("GitHub changed on win-pc", note.Title);
        Assert.Contains("scopes: repo, workflow → repo", note.Body);
        Assert.Empty(notifier.Next(Overview([], [scopes, written, old]), Now));

        var failed = new Overview(Now, null, "Ledger isn't answering", null, null, null, null);
        Assert.Empty(notifier.Next(failed, Now));
        Assert.Equal("IX Ledger isn't answering", Assert.Single(notifier.Next(failed, Now)).Title);
        Assert.Empty(notifier.Next(failed, Now));
        Assert.Equal("IX Ledger is answering again", Assert.Single(notifier.Next(Overview([], [scopes, old]), Now)).Title);

        var single = new ChangeNotifier();
        single.Next(Overview([]), Now);
        Assert.Empty(single.Next(failed, Now));
        Assert.Empty(single.Next(Overview([]), Now)); // One slow answer raises nothing either way.
    }

    [Fact]
    public void SupervisorFilesGiveRestartsAndTheDeployedCommit() {
        var directory = LedgerTestPaths.NewDirectory("tray-supervisor");
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "service-health.json"), """{"checkedAtUtc":"2026-11-20T09:59:00Z","failures":1,"verdict":"ok"}""");
            File.WriteAllText(Path.Combine(directory, "deployment.json"), """{"sourceCommit":"71b01fdf96b00043"}""");
            File.WriteAllLines(Path.Combine(directory, "service-restarts.jsonl"), [
                """{"atUtc":"2026-11-18T08:00:00Z","reason":"stalled"}""", """{"atUtc":"2026-11-20T07:00:00Z","reason":"write-gate-busy"}""", "not json"]);
            var supervisor = Supervisor.Read(directory, Now)!;
            Assert.Equal(("ok", 1, "71b01fdf96b00043", 1, "write-gate-busy"),
                (supervisor.Verdict, supervisor.Failures, supervisor.Commit, supervisor.RestartsLastDay, supervisor.LastRestartReason));
            Assert.Null(Supervisor.Read(Path.Combine(directory, "missing"), Now));
        } finally { Directory.Delete(directory, recursive: true); }
    }
}
