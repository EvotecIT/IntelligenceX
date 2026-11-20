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
        Assert.Equal("Ledger up 3 h · build 71b01fd", view.Footer);
        Assert.Equal("someone · 2 scopes", string.Join(" · ", view.ThisMachine!.Providers[0].Summary.Split(" · ").Skip(1)));

        // Signed out anywhere, or a token variable overriding gh's stored sign-in, needs a look.
        var mac = Machine("mac", Fact("github", "signed-in", new() { ["env-override"] = "GH_TOKEN (user)" }), Fact("codex", "signed-out"), Fact("claude", "unknown"));
        view = TrayView.Build(Overview([good, mac]), "win-pc", Now);
        Assert.Equal(TrayState.Degraded, view.State);
        Assert.Equal("2 things need you", view.Headline);
        var signedOut = view.Issues.Single(issue => issue.Provider == "codex");
        Assert.Equal(("bad", "Codex is signed out on mac", "codex login"), (signedOut.Severity, signedOut.Title, signedOut.Command));
        Assert.Contains("GH_TOKEN overrides gh on mac", view.Problems);
        Assert.Equal("bad", view.Issues[0].Severity); // Broken things come before things worth a look.
        // A usage limit from the agent is no sign-out: no card and no sign-in command.
        var limited = new AuthMachine("studio", "healthy", Now, "v1", [], [new("claude", true, "usage-limit", "claude auth login", Now, Now)]);
        var limitedView = TrayView.Build(Overview([limited]), "win-pc", Now);
        Assert.Empty(limitedView.Issues);
        Assert.Equal("at usage limit", limitedView.Others[0].Providers.Single(p => p.Provider == "claude").Short);
        // A sign-out and an overriding token are two separate things to do, and both count.
        var both = Machine("mac", Fact("github", "signed-out", new() { ["env-override"] = "GH_TOKEN (user)" }));
        Assert.Equal(["GitHub is signed out on mac", "GH_TOKEN overrides gh on mac"], TrayView.Build(Overview([both]), "win-pc", Now).Problems);
        Assert.Equal([TrayView.Warn, TrayView.Bad, TrayView.Unknown], Assert.Single(view.Others).Providers.Select(p => p.Mark));
        Assert.True(view.Others[0].Providers[1].NeedsSignIn);
        // The menu line keeps the state and the warning; the rest stays in the submenu.
        Assert.Equal("signed in · GH_TOKEN overrides the stored sign-in", view.Others[0].Providers[0].MenuSummary);
        Assert.Equal("signed in · someone", view.ThisMachine!.Providers[0].MenuSummary);

        // Two versions are worth a look even when the sign-in state is unconfirmed.
        var unconfirmed = Machine("win-pc", Fact("codex", "unknown", new() { ["versions"] = "0.130.0, 0.170.0" }));
        view = TrayView.Build(Overview([unconfirmed]), "win-pc", Now);
        Assert.Equal(TrayState.Degraded, view.State);
        Assert.Contains("win-pc has 2 Codex versions: 0.130.0 and 0.170.0", view.Problems);
        Assert.Equal("2 versions", view.ThisMachine!.Providers.Single(p => p.Provider == "codex").Short);

        var stalled = TrayView.Build(Overview([good]) with { Health = new("stalled", "write-gate-busy", Now.AddHours(-3)) }, "win-pc", Now);
        Assert.Equal((TrayState.Down, "The Ledger is stalled"), (stalled.State, stalled.Headline));
        Assert.Contains("write-gate-busy", stalled.Issues[0].Title);

        var down = TrayView.Build(new(Now, null, "Ledger isn't answering", null, null, null, null), "win-pc", Now);
        Assert.Equal((TrayState.Down, "The Ledger isn't answering"), (down.State, down.Headline));
        Assert.Equal("All good", TrayView.Build(Overview([good]), "win-pc", Now).Headline);
    }

    // The card for two versions names the copy to remove, and never offers to remove the one an app ships itself.
    [Fact]
    public void OlderCopiesToRemoveLeaveAppBundledOnesAlone() {
        var issue = TrayView.Versions("win-pc", "codex", "Codex", "0.130.0-alpha.5, 0.170.0",
            @"C:\npm\codex.cmd 0.170.0; C:\Local\OpenAI\Codex\bin\codex.exe 0.130.0-alpha.5")!;
        Assert.Equal(@"C:\Local\OpenAI\Codex\bin\codex.exe (0.130.0-alpha.5)", issue.Command);
        Assert.Null(TrayView.Versions("mac", "codex", "Codex", "0.171.0, 0.171.0-alpha.13",
            "/Users/me/.local/bin/codex 0.171.0; /Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex 0.171.0-alpha.13"));
        // Prereleases order like semantic versions; a copy whose version couldn't be read is never offered.
        Assert.True(TrayView.CompareVersions("0.170.0-alpha.10", "0.170.0-alpha.9") > 0);
        Assert.True(TrayView.CompareVersions("0.170.0", "0.170.0-alpha.10") > 0);
        Assert.True(TrayView.CompareVersions("0.170.0-alpha", "0.170.0-alpha.1") < 0);
        Assert.Equal("/a (0.170.0-alpha.9)", TrayView.Versions("x", "codex", "Codex", "0.170.0-alpha.10, 0.170.0-alpha.9",
            "/a 0.170.0-alpha.9; /b 0.170.0-alpha.10")!.Command);
        Assert.Equal("/a (0.164.0)", TrayView.Versions("x", "codex", "Codex", "0.164.0, 0.170.0", "/a 0.164.0; /b 0.170.0; /c ?")!.Command);
        // The Codex app's own CLI is never offered, wherever its package is installed.
        Assert.Null(TrayView.Versions("x", "codex", "Codex", "0.169.0, 0.170.0", @"E:\Apps\Codex\codex.exe 0.169.0; C:\npm\codex.cmd 0.170.0",
            appBinary: @"E:\Apps\Codex\codex.exe"));
        // Every registered package's copy is the app's, not just the newest one.
        Assert.Null(TrayView.Versions("x", "codex", "Codex", "0.168.0, 0.169.0", @"E:\Old\codex.exe 0.168.0; E:\New\codex.exe 0.169.0",
            appBinary: @"E:\New\codex.exe; E:\Old\codex.exe"));
        // A prerelease of the same number is the older one.
        Assert.Contains("(0.2.0-beta)", TrayView.Versions("x", "codex", "Codex", "0.2.0-beta, 0.2.0", "/a 0.2.0-beta; /b 0.2.0")!.Command);
    }

    [Fact]
    public void TheTimelineReadsAsWhatHappened() {
        string? Text(string field, string? before, string? after, string provider = "github") =>
            TrayView.Describe(new("mac", provider, Now.AddMinutes(-3), field, before, after), Now)?.Text;
        Assert.Equal("GitHub signed out", Text("state", "signed-in", "signed-out"));
        Assert.Equal("GitHub signed in again", Text("state", "signed-out", "signed-in"));
        Assert.Equal("GitHub lost gist and gained project", Text("scopes", "gist, repo", "project, repo"));
        Assert.Equal("Codex config.toml rewritten", Text("config-fingerprint", "a", "b", "codex"));
        Assert.Null(Text("config-written", "a", "b", "codex"));
        Assert.Null(Text("agent-sign-in", "ready", "signed-out", "codex"));
        // A removed sign-in file is said once, by "credentials", not also as "rewritten".
        Assert.Null(Text("credentials-written", "2026-11-20T08:17:04Z", null, "codex"));
        Assert.Equal("Codex sign-in file removed", Text("credentials", "file", "none", "codex"));
        Assert.Null(Text("hosts-written", "2026-10-21T15:52:55Z", null));
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
        Assert.Contains("GitHub lost workflow", note.Body);
        Assert.Empty(notifier.Next(Overview([], [scopes, written, old]), Now));

        var failed = new Overview(Now, null, "Ledger isn't answering", null, null, null, null);
        Assert.Empty(notifier.Next(failed, Now));
        Assert.Equal("IX Ledger isn't working", Assert.Single(notifier.Next(failed, Now)).Title);
        Assert.Empty(notifier.Next(failed, Now));
        Assert.Equal("IX Ledger is working again", Assert.Single(notifier.Next(Overview([], [scopes, old]), Now)).Title);

        // A stall counts as a failed poll, and without a history read nothing is primed or announced.
        var stall = Overview([]) with { Health = new("stalled", "database-slow", Now) };
        var later = new ChangeNotifier();
        Assert.Empty(later.Next(stall, Now));
        Assert.Equal("IX Ledger isn't working", Assert.Single(later.Next(stall, Now)).Title);
        Assert.Single(later.Next(Overview([]) with { Auth = null }, Now)); // Working again; history not read yet.
        Assert.Empty(later.Next(Overview([], [old]), Now)); // The first history read is the baseline.

        var single = new ChangeNotifier();
        single.Next(Overview([]), Now);
        Assert.Empty(single.Next(failed, Now));
        Assert.Empty(single.Next(Overview([]), Now)); // One slow answer raises nothing either way.
    }

    // The next login has to start the same tray, reading the same Ledger folder.
    [Fact]
    public void LoginAgentKeepsTheLedgerFolder() {
        var plist = LoginItem.Plist("/apps/tray", ["--ledger-dir", "/data/a&b"]);
        Assert.Contains("<string>/apps/tray</string><string>--ledger-dir</string><string>/data/a&amp;b</string>", plist);
        // On Windows a trailing backslash would otherwise escape the closing quote of the Run value's argument.
        Assert.Equal("\"D:\\\\\"", LoginItem.QuoteArgument("D:\\"));
        Assert.Equal("\"D:\\a\\\\\\\\\"", LoginItem.QuoteArgument("D:\\a\\\\"));
        Assert.Equal("\"C:\\Ledger data\"", LoginItem.QuoteArgument("C:\\Ledger data"));
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
