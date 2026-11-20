using System.Globalization;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Tray;

/// <summary>Overall state the icon shows: working, something needs a look, or the Ledger isn't answering.</summary>
internal enum TrayState { Starting, Healthy, Degraded, Down }

/// <summary>One provider on one machine: a mark, a short label for its card, a one-line summary for the menu and the
/// facts behind it, and what needs doing about it, if anything.</summary>
internal sealed record ProviderLine(string Provider, string Name, string Mark, string Summary, (string Label, string Value)[] Details, bool NeedsSignIn,
    string? Warning = null) {
    /// <summary>A word or two for the machine card: signed in, signed out, 2 versions, can't tell.</summary>
    public string Short { get; init; } = "";
    /// <summary>Everything that needs doing about this provider; a sign-out and an overriding token are separate actions.</summary>
    public Issue[] Issues { get; init; } = [];

    /// <summary>The state and the one thing most worth seeing (the warning, or else the next fact), short enough for a
    /// menu line that sits near a screen edge. The full summary and facts stay in the submenu and details window.</summary>
    public string MenuSummary {
        get {
            var parts = Summary.Split(" · ");
            var next = Warning ?? (parts.Length > 1 ? parts[1] : null);
            return next is null ? parts[0] : parts[0] + " · " + next;
        }
    }
}

/// <summary>Something that needs Przemek: what is wrong and where, what it means, and the one thing to do. Bad is
/// broken now; warn is worth a look. A command is the exact text to run on that machine.</summary>
internal sealed record Issue(string Severity, string Machine, string? Provider, string Title, string Meaning, string Action, string? Command = null);

internal sealed record MachineView(string Machine, string Reporter, ProviderLine[] Providers, string? Note);

/// <summary>One change in the history, worded for reading: "GitHub signed out", "Codex config.toml rewritten".</summary>
internal sealed record TimelineEntry(DateTimeOffset AtUtc, string When, string Machine, string Text, string Tone);

/// <summary>What the tray shows for one overview. Built without any UI so it can be tested and printed.</summary>
internal sealed record TrayView(TrayState State, string Headline, string[] LedgerLines, MachineView? ThisMachine, MachineView[] Others,
    string[] Changes, Issue[] Issues) {
    public string[] Problems => Issues.Select(issue => issue.Title).ToArray();
    public TimelineEntry[] Timeline { get; init; } = [];
    /// <summary>The Ledger's own health in one line: uptime, build, supervisor.</summary>
    public string Footer => string.Join(" · ", LedgerLines);
    public string Tooltip => "IX Ledger · " + Headline + (Issues.Length > 0 ? " · " + Issues[0].Title : "");

    /// <summary>A text version of the menu, for <c>--snapshot</c> and for comparing whether the menu needs rebuilding.</summary>
    public string Text() {
        var lines = new List<string> { State + ": " + Headline };
        lines.AddRange(LedgerLines.Select(line => "  " + line));
        foreach (var machine in new[] { ThisMachine }.OfType<MachineView>().Concat(Others)) {
            lines.Add(machine.Machine + (machine == ThisMachine ? " (this machine)" : "") + " · reporter " + machine.Reporter + (machine.Note is { } note ? " · " + note : ""));
            lines.AddRange(machine.Providers.Select(p => "  " + p.Mark + " " + p.Name + " " + p.Summary));
        }
        foreach (var issue in Issues) lines.Add("Needs you: " + issue.Title + " - " + issue.Meaning + " " + issue.Action);
        if (Changes.Length > 0) { lines.Add("Recent changes:"); lines.AddRange(Changes.Select(change => "  " + change)); }
        return string.Join(Environment.NewLine, lines);
    }

    internal const string Ok = "✓", Bad = "✗", Unknown = "?", Warn = "!", Missing = "–";

    /// <summary>The one-line answer at the top: all good, or how many things need Przemek.</summary>
    internal static string Answer(int issues) => issues switch { 0 => "All good", 1 => "1 thing needs you", _ => issues + " things need you" };

    public static TrayView Build(Overview overview, string? machine, DateTimeOffset now) {
        var issues = new List<Issue>();
        if (overview.Health is not { } health) {
            var why = overview.HealthError ?? "The Ledger isn't answering";
            return new(TrayState.Down, "The Ledger isn't answering", Supervisor(overview.Local, now).ToArray(), null, [], [],
                [new("bad", "", null, why, "Nothing here can refresh until it answers. Its supervisor restarts it after three failed checks.",
                    "If it stays down, check the Ledger service on the Ledger host.")]);
        }
        var ledger = new List<string>();
        if (health.StartedAtUtc is { } started) ledger.Add("Ledger up " + Span(now - started));
        if ((health.Commit ?? overview.Local?.Commit) is { Length: >= 7 } commit) ledger.Add("build " + commit[..7]);
        ledger.AddRange(Supervisor(overview.Local, now));
        if (health.Status != "ok")
            issues.Add(new("bad", "", null, "The Ledger is stalled (" + (health.Reason ?? "unknown") + ")",
                "It answers but can't reach its database, so nothing new is recorded.", "Its supervisor restarts it after three failed checks; wait a few minutes."));

        var auth = overview.Auth;
        if (auth is null) issues.Add(new("warn", "", null, "Sign-in facts didn't load", "The Ledger answered its health check but not the sign-in read.", "Refresh in a minute."));
        var machines = (auth?.Machines ?? []).Select(m => Machine(m, overview, now)).ToArray();
        foreach (var view in machines) {
            if (view.Reporter != "healthy")
                issues.Add(new("bad", view.Machine, null, view.Reporter == "not-enrolled" ? view.Machine + " has never reported" : view.Machine + " isn't reporting",
                    "The Ledger hasn't heard from its reporter, so that machine's sessions and sign-ins may be out of date.",
                    "Check the Ledger reporter on " + view.Machine + "; it usually comes back after a restart or sign-in."));
            issues.AddRange(view.Providers.SelectMany(p => p.Issues));
        }
        if (overview.GitHub?.Windows.FirstOrDefault(w => w.Resource == "core") is { } core && core.Limit > 0 && core.Remaining < core.Limit / 10)
            issues.Add(new("warn", "", "github", "GitHub REST budget is low: " + core.Remaining.ToString("N0", CultureInfo.InvariantCulture) + " left",
                "Every tool on this GitHub account shares 5,000 requests an hour; when it runs out, gh and the reviewers fail until " +
                core.ResetAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + ".", "See who is spending it with github_budget."));
        var history = auth?.Changes ?? [];
        var changes = history.Take(15).Select(change => Change(change, now)).ToArray();
        var timeline = history.Select(change => Describe(change, now)).OfType<TimelineEntry>().Take(15).ToArray();
        // A stalled health check is down, not just degraded: the service answers but can't reach its database.
        var state = health.Status != "ok" ? TrayState.Down : issues.Count == 0 ? TrayState.Healthy : TrayState.Degraded;
        var ordered = issues.OrderBy(issue => issue.Severity == "bad" ? 0 : 1).ToArray();
        return new(state, health.Status != "ok" ? "The Ledger is stalled" : Answer(ordered.Length), ledger.ToArray(),
            machines.FirstOrDefault(m => m.Machine == machine), machines.Where(m => m.Machine != machine).ToArray(), changes, ordered) { Timeline = timeline };
    }

    private static IEnumerable<string> Supervisor(Supervisor? local, DateTimeOffset now) {
        if (local is null) yield break;
        yield return "supervisor " + (local.Verdict ?? "unknown") + (local.Failures > 0 ? ", " + local.Failures + " failed checks in a row" : "");
        if (local.LastRestartUtc is { } restart && now - restart < TimeSpan.FromDays(1))
            yield return local.RestartsLastDay + " restart" + (local.RestartsLastDay == 1 ? "" : "s") + " today, last " + Ago(now - restart) +
                (local.LastRestartReason is { } reason ? " (" + reason + ")" : "");
    }

    private static MachineView Machine(AuthMachine machine, Overview overview, DateTimeOffset now) {
        var facts = machine.Providers.ToDictionary(p => p.Provider, StringComparer.Ordinal);
        var lines = new[] { "github", "codex", "claude" }.Select(provider =>
            Provider(provider, facts.GetValueOrDefault(provider), machine, overview, now)).ToArray();
        var note = machine.Providers.Length == 0 && machine.Reporter == "healthy" ? "Older reporter: only the agent's sign-in shows."
            : machine.LastSeenUtc is { } seen && now - seen > TimeSpan.FromMinutes(5) ? "Last heard " + Ago(now - seen) + "." : null;
        return new(machine.Machine, machine.Reporter, lines, note);
    }

    internal static string Name(string provider) => provider switch { "github" => "GitHub", "codex" => "Codex", "claude" => "Claude", _ => provider };

    /// <summary>What a provider is used for, so a problem says what it breaks.</summary>
    private static string Breaks(string provider) => provider switch {
        "github" => "gh, git over HTTPS and the Ledger's GitHub reads fail there",
        "codex" => "Codex sessions and the agent's Codex work can't run there",
        _ => "Claude sessions and deliveries to Claude wait there"
    };

    internal static string SignInCommand(string provider) => provider switch { "github" => "gh auth login", "claude" => "claude auth login", _ => "codex login" };

    private static ProviderLine Provider(string provider, AuthFact? fact, AuthMachine machine, Overview overview, DateTimeOffset now) {
        var name = Name(provider);
        var where = machine.Machine;
        var signIn = machine.SignIn.FirstOrDefault(s => s.Provider == provider);
        Issue SignedOut() => new("bad", where, provider, name + " is signed out on " + where, "Until it is signed in again, " + Breaks(provider) + ".",
            "Sign in on " + where + ".", SignInCommand(provider));
        if (fact is null) {
            if (signIn is null) return new(provider, name, Missing, "not reported", [], false) { Short = "no data" };
            var unknown = !signIn.Required && signIn.Reason == "unknown";
            return new(provider, name, signIn.Required ? Bad : unknown ? Unknown : Ok,
                signIn.Required ? Reason(signIn.Reason) : unknown ? "unknown" : "signed in", [("Agent", Reason(signIn.Reason))], signIn.Required) {
                Short = signIn.Required ? Reason(signIn.Reason) : unknown ? "can't tell" : "signed in", Issues = signIn.Required ? [SignedOut()] : []
            };
        }
        var f = fact.Facts ?? [];
        var parts = new List<string>();
        var mark = fact.State switch { "signed-in" => Ok, "signed-out" => Bad, "not-installed" => Missing, _ => Unknown };
        var label = fact.State switch { "signed-in" => "signed in", "signed-out" => "signed out", "not-installed" => "not installed", _ => "can't tell" };
        parts.Add(fact.State switch { "signed-in" => "signed in", "signed-out" => "signed out", "not-installed" => "not installed", _ => "unknown" });
        if (f.GetValueOrDefault("agent-sign-in") is { } agent && agent is not ("ready" or "signed-out")) parts[0] += " (" + Reason(agent) + ")";
        string? warning = null;
        var found = new List<Issue>();
        if (fact.State == "signed-out") found.Add(SignedOut());
        switch (provider) {
            case "github":
                if (f.GetValueOrDefault("account") is { } login) parts.Add(login);
                if (f.GetValueOrDefault("token-source") is { } source) parts.Add(source);
                if (f.GetValueOrDefault("scopes") is { } scopes) parts.Add(scopes.Split(',').Length + " scopes");
                if (overview.GitHub?.Windows.FirstOrDefault(w => w.Resource == "core") is { } core && core.Limit > 0)
                    parts.Add("REST " + core.Remaining.ToString("N0", CultureInfo.InvariantCulture) + " left");
                if (f.GetValueOrDefault("check") == "token-unreadable")
                    found.Add(new("warn", where, provider, where + " can't read GitHub's sign-in",
                        "The background reporter couldn't read gh's token, usually because the keychain is locked. gh in your own terminal is probably fine.",
                        "Unlock " + where + ", or run gh auth status there to confirm.", "gh auth status"));
                if (f.GetValueOrDefault("env-override") is { Length: > 0 } env) {
                    var variable = env.Split(' ')[0];
                    warning = variable + " overrides the stored sign-in";
                    found.Add(new("warn", where, provider, variable + " overrides gh on " + where,
                        "gh uses the token in " + env + " instead of your stored sign-in, so its account and scopes may not be the ones you expect.",
                        "Remove " + variable + " from that environment unless you set it on purpose."));
                }
                break;
            case "codex":
            case "claude":
                if (f.GetValueOrDefault("version") is { } version) parts.Add(version);
                var account = overview.Budget?.Accounts.FirstOrDefault(a => a.Provider == provider && (a.Machine is null || a.Machine == machine.Machine));
                if (account?.Plan is { } plan) parts.Add(plan);
                if (account?.ShortUsedPercent is { } shortUse) parts.Add("5 h " + shortUse.ToString("0", CultureInfo.InvariantCulture) + "%");
                if (account?.UsedPercent is { } used)
                    parts.Add((account.WindowMinutes is >= 10080 ? "week " : account.WindowMinutes is { } minutes ? Span(TimeSpan.FromMinutes(minutes)) + " " : "") +
                        used.ToString("0", CultureInfo.InvariantCulture) + "%");
                if (f.GetValueOrDefault("versions") is { } versions) {
                    // Only copies Przemek installed himself are worth a card; an app's own bundled copy updates with the app.
                    if (Versions(where, provider, name, versions, f.GetValueOrDefault("binaries")) is { } extra) {
                        warning = versions.Split(',').Length + " versions installed";
                        found.Add(extra);
                    }
                }
                break;
        }
        if (warning is not null) {
            parts.Add(warning);
            if (mark == Ok) mark = Warn;
            if (fact.State != "signed-out") label = warning.EndsWith(" installed", StringComparison.Ordinal) ? warning[..^" installed".Length] : "overridden";
        }
        var details = f.Where(pair => pair.Key != "config-written").OrderBy(pair => Order(pair.Key))
            .Select(pair => (Label(pair.Key), pair.Key is "binaries" or "env-override" ? pair.Value.Replace("; ", Environment.NewLine) : pair.Value)).ToList();
        details.Add(("Checked", Ago(now - fact.CheckedAtUtc)));
        return new(provider, name, mark, string.Join(" · ", parts), details.ToArray(), fact.State == "signed-out", warning) { Short = label, Issues = found.ToArray() };
    }

    /// <summary>
    /// Two or more versions of one CLI: which ones, why it matters and which older copies can go. Copies an app ships
    /// and updates itself (inside ChatGPT.app, the Codex app's managed service, WindowsApps) are left out, and with
    /// nothing else older there is nothing to do, so no issue.
    /// </summary>
    internal static Issue? Versions(string machine, string provider, string name, string versions, string? binaries) {
        var all = versions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var newest = all.OrderBy(v => v, Comparer<string>.Create(CompareVersions)).Last();
        // Only a copy whose version was read and is older than the newest is offered for removal; "?" means the probe failed.
        var older = (binaries ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries)
            .Select(row => (Path: row[..Math.Max(0, row.LastIndexOf(' '))], Version: row[(row.LastIndexOf(' ') + 1)..]))
            .Where(row => row.Path.Length > 0 && row.Version != "?" && CompareVersions(row.Version, newest) < 0 && !AppManaged(row.Path))
            .Select(row => row.Path + " (" + row.Version + ")").ToArray();
        var title = machine + " has " + all.Length + " " + name + " versions: " + string.Join(" and ", all);
        const string meaning = "Which one runs depends on PATH and on which app starts it, so a session can pick up the older copy, with its own sign-in and behaviour.";
        // Without the list of binaries there's no telling which copy is which.
        if (string.IsNullOrEmpty(binaries)) return new("warn", machine, provider, title, meaning, "Keep one install and remove the others.");
        if (older.Length == 0) return null;
        return new("warn", machine, provider, title, meaning,
            older.Length == 1 ? "Remove the older copy if nothing uses it:" : "Remove the older copies if nothing uses them:", string.Join(Environment.NewLine, older));
    }

    /// <summary>A copy that an app installs and updates itself, which isn't Przemek's to remove.</summary>
    internal static bool AppManaged(string path) =>
        path.Contains(".app/Contents/", StringComparison.Ordinal) || path.Contains("app-server-daemon", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);

    /// <summary>Semantic-version order: numbers first, a release above its prereleases, and prerelease identifiers
    /// compared one by one, numbers numerically (alpha.10 after alpha.9). Build metadata after + is ignored.</summary>
    internal static int CompareVersions(string left, string right) {
        static (Version Number, string[] Pre) Parse(string text) {
            var core = text.Split('+')[0];
            var dash = core.IndexOf('-');
            var number = dash < 0 ? core : core[..dash];
            return (Version.TryParse(number, out var parsed) ? parsed : new Version(0, 0), dash < 0 ? [] : core[(dash + 1)..].Split('.'));
        }
        var (a, aPre) = Parse(left);
        var (b, bPre) = Parse(right);
        var compared = a.CompareTo(b);
        if (compared != 0) return compared;
        if (aPre.Length == 0 || bPre.Length == 0) return bPre.Length.CompareTo(aPre.Length); // A release outranks a prerelease.
        for (var index = 0; index < Math.Min(aPre.Length, bPre.Length); index++) {
            var aNumber = int.TryParse(aPre[index], out var x);
            var bNumber = int.TryParse(bPre[index], out var y);
            var step = aNumber && bNumber ? x.CompareTo(y) : aNumber ? -1 : bNumber ? 1 : string.CompareOrdinal(aPre[index], bPre[index]);
            if (step != 0) return step;
        }
        return aPre.Length.CompareTo(bPre.Length);
    }

    /// <summary>One change worded for the timeline, or null for a detail the history keeps but the timeline doesn't
    /// need (a file time that comes with a content change, the agent's own field next to the state).</summary>
    internal static TimelineEntry? Describe(AuthChange change, DateTimeOffset now) {
        var name = Name(change.Provider);
        var (text, tone) = change.Field switch {
            "state" => (name + " " + change.After switch {
                "signed-in" => change.Before == "signed-out" ? "signed in again" : "signed in",
                "signed-out" => "signed out", "not-installed" => "removed", _ => "can't be checked"
            }, change.After == "signed-out" ? "bad" : change.After == "signed-in" ? "good" : "warn"),
            "scopes" => (ScopeChange(name, change.Before, change.After), "warn"),
            "account" => (name + " account is now " + (change.After ?? "none"), "warn"),
            "token-source" => (name + " token now comes from " + (change.After ?? "nowhere"), "warn"),
            "env-override" => (change.After is null ? "Token variable removed: " + change.Before : "Token variable set: " + change.After, change.After is null ? "good" : "warn"),
            "versions" => (change.After is null ? name + " is down to one version" : name + " versions now " + change.After, change.After is null ? "good" : "warn"),
            "version" => (name + " updated to " + change.After, "neutral"),
            "binaries" => (name + " binaries changed", "neutral"),
            "config-fingerprint" => (name + " config.toml rewritten", "warn"),
            "credentials" => (name + (change.After == "none" ? " sign-in file removed" : " sign-in file is back"), change.After == "none" ? "bad" : "neutral"),
            "credentials-written" => (name + " sign-in file rewritten", "neutral"),
            "hosts-written" => ("gh sign-in file (hosts.yml) rewritten", "neutral"),
            "credential-store" => (name + " now keeps its sign-in in " + (change.After ?? "the default store"), "warn"),
            "check" when change.After == "token-unreadable" => (name + " sign-in couldn't be read", "warn"),
            _ => ((string?)null, "")
        };
        if (text is null) return null;
        var when = change.AtUtc.ToLocalTime().ToString(now - change.AtUtc < TimeSpan.FromHours(20) ? "HH:mm" : "MMM d, HH:mm", CultureInfo.InvariantCulture);
        return new(change.AtUtc, when, change.Machine, text.Replace("; ", ", ", StringComparison.Ordinal), tone);
    }

    private static string ScopeChange(string name, string? before, string? after) {
        static HashSet<string> Set(string? value) =>
            (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var old = Set(before);
        var current = Set(after);
        var lost = old.Except(current).Order(StringComparer.Ordinal).ToArray();
        var gained = current.Except(old).Order(StringComparer.Ordinal).ToArray();
        if (lost.Length + gained.Length == 0) return name + " scopes changed";
        return name + (lost.Length > 0 ? " lost " + string.Join(", ", lost) : "") + (lost.Length > 0 && gained.Length > 0 ? " and" : "") +
            (gained.Length > 0 ? " gained " + string.Join(", ", gained) : "");
    }

    private static int Order(string key) => Array.IndexOf(KeyOrder, key) is var index and >= 0 ? index : KeyOrder.Length;

    private static readonly string[] KeyOrder = ["account", "accounts", "token-source", "scopes", "env-override", "agent-sign-in", "version", "versions",
        "binary", "binaries", "credential-store", "credentials", "credentials-written", "config-written", "config-fingerprint", "hosts-written", "check"];

    internal static string Label(string key) => key switch {
        "account" => "Account", "accounts" => "Accounts", "token-source" => "Token from", "scopes" => "Scopes",
        "env-override" => "Token variables", "agent-sign-in" => "Agent sign-in", "version" => "Version", "versions" => "Versions",
        "binary" => "Binary", "binaries" => "Binaries", "credential-store" => "Credential store", "credentials" => "Credentials",
        "credentials-written" => "Credentials written", "config-written" => "config.toml written", "config-fingerprint" => "config.toml hash",
        "hosts-written" => "gh hosts.yml written", "git-protocol" => "Git protocol", "check" => "Last check", "state" => "State", _ => key
    };

    private static string Reason(string reason) => reason switch {
        "ready" or "signed-in" => "ready", "signed-out" => "signed out", "expired" => "sign-in expired", "usage-limit" => "at usage limit", _ => reason
    };

    /// <summary>One change as a line: when, where, what and from what to what.</summary>
    internal static string Change(AuthChange change, DateTimeOffset now) {
        var when = change.AtUtc.ToLocalTime().ToString(now - change.AtUtc < TimeSpan.FromHours(20) ? "HH:mm" : "MMM d HH:mm", CultureInfo.InvariantCulture);
        var what = change.Field == "state" ? "" : " " + Label(change.Field).ToLowerInvariant();
        return when + " · " + change.Machine + " · " + Name(change.Provider) + what + ": " + Short(change.Before ?? "none") + " → " + Short(change.After ?? "none");
    }

    private static string Short(string value) => value.Length <= 60 ? value : value[..57] + "…";

    internal static string Ago(TimeSpan span) => span < TimeSpan.FromMinutes(1) ? "just now" : Span(span) + " ago";

    internal static string Span(TimeSpan span) => span.TotalMinutes < 60 ? Math.Max(1, (int)span.TotalMinutes) + " min"
        : span.TotalHours < 48 ? (int)span.TotalHours + " h" : (int)span.TotalDays + " days";

    /// <summary>Fields whose change is worth a notification. File times and version upgrades are kept in the history only.</summary>
    internal static readonly HashSet<string> NotifyFields = new(StringComparer.Ordinal) {
        "state", "account", "scopes", "token-source", "env-override", "versions", "agent-sign-in", "credentials", "config-fingerprint", "credential-store"
    };
}

/// <summary>
/// Decides what is worth a notification. Changes the Ledger recorded are real changes already, so each new one in a
/// notable field is announced once. The Ledger going down or stalling is announced only after two polls in a row fail, and its
/// recovery only after a down was announced, so one slow answer doesn't raise anything.
/// </summary>
internal sealed class ChangeNotifier {
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private bool _primed;
    private int _failures;
    private bool _downAnnounced;

    public IReadOnlyList<(string Title, string Body)> Next(Overview overview, DateTimeOffset now) {
        var notes = new List<(string, string)>();
        // A stalled health check is a failed poll too: the service answers but can't do its work.
        if (overview.Health is not { Status: "ok" }) {
            var why = overview.Health is { } stalled ? "The Ledger is stalled (" + (stalled.Reason ?? "unknown") + ")." : overview.HealthError ?? "No answer from the Ledger.";
            if (++_failures == 2 && !_downAnnounced) { _downAnnounced = true; notes.Add(("IX Ledger isn't working", why)); }
            return notes;
        }
        _failures = 0;
        if (_downAnnounced) { _downAnnounced = false; notes.Add(("IX Ledger is working again", "The Ledger service is back.")); }
        // Without a history read there is nothing to compare; priming on nothing would announce all of history later.
        if (overview.Auth is not { } auth) return notes;
        var fresh = auth.Changes.Where(change => _seen.Add(Key(change))).ToArray();
        if (!_primed) { _primed = true; return notes; } // What was there at start is history, not news.
        var notable = fresh.Where(change => TrayView.NotifyFields.Contains(change.Field)).OrderBy(change => change.AtUtc).ToArray();
        foreach (var group in notable.GroupBy(change => (change.Machine, change.Provider))) {
            var title = TrayView.Name(group.Key.Provider) + " changed on " + group.Key.Machine;
            notes.Add((title, string.Join(Environment.NewLine, group.Take(4).Select(change => TrayView.Describe(change, now)?.Text ?? TrayView.Change(change, now).Split(" · ", 3)[2]))));
        }
        return notes;
    }

    private static string Key(AuthChange change) => change.Machine + "|" + change.Provider + "|" + change.Field + "|" + change.AtUtc.UtcTicks + "|" + change.After;
}
