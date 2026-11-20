using System.Globalization;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Tray;

/// <summary>Overall state the icon shows: working, something needs a look, or the Ledger isn't answering.</summary>
internal enum TrayState { Starting, Healthy, Degraded, Down }

/// <summary>One provider on one machine as the menu shows it: a mark, a one-line summary and the facts behind it.</summary>
internal sealed record ProviderLine(string Provider, string Name, string Mark, string Summary, (string Label, string Value)[] Details, bool NeedsSignIn,
    string? Warning = null) {
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

internal sealed record MachineView(string Machine, string Reporter, ProviderLine[] Providers, string? Note);

/// <summary>What the tray shows for one overview. Built without any UI so it can be tested and printed.</summary>
internal sealed record TrayView(TrayState State, string Headline, string[] LedgerLines, MachineView? ThisMachine, MachineView[] Others,
    string[] Changes, string[] Problems) {
    public string Tooltip => "IX Ledger · " + Headline + (Problems.Length > 0 ? " · " + Problems[0] : "");

    /// <summary>A text version of the menu, for <c>--snapshot</c> and for comparing whether the menu needs rebuilding.</summary>
    public string Text() {
        var lines = new List<string> { State + ": " + Headline };
        lines.AddRange(LedgerLines.Select(line => "  " + line));
        foreach (var machine in new[] { ThisMachine }.OfType<MachineView>().Concat(Others)) {
            lines.Add(machine.Machine + (machine == ThisMachine ? " (this machine)" : "") + " · reporter " + machine.Reporter + (machine.Note is { } note ? " · " + note : ""));
            lines.AddRange(machine.Providers.Select(p => "  " + p.Mark + " " + p.Name + " " + p.Summary));
        }
        if (Problems.Length > 0) lines.Add("Needs a look: " + string.Join("; ", Problems));
        if (Changes.Length > 0) { lines.Add("Recent changes:"); lines.AddRange(Changes.Select(change => "  " + change)); }
        return string.Join(Environment.NewLine, lines);
    }

    internal const string Ok = "✓", Bad = "✗", Unknown = "?", Warn = "!", Missing = "–";

    public static TrayView Build(Overview overview, string? machine, DateTimeOffset now) {
        var problems = new List<string>();
        if (overview.Health is not { } health) {
            var why = overview.HealthError ?? "Ledger isn't answering";
            return new(TrayState.Down, why, Supervisor(overview.Local, now).ToArray(), null, [], [], [why]);
        }
        var ledger = new List<string>();
        var up = health.StartedAtUtc is { } started ? "up " + Span(now - started) : null;
        var commit = health.Commit ?? overview.Local?.Commit;
        var headline = health.Status == "ok" ? "Ledger working" : "Ledger stalled (" + (health.Reason ?? "unknown") + ")";
        if (health.Status != "ok") problems.Add(headline);
        ledger.Add(string.Join(" · ", new[] { up, commit is { Length: >= 7 } ? "build " + commit[..7] : null }.OfType<string>()));
        ledger.AddRange(Supervisor(overview.Local, now));

        var auth = overview.Auth;
        if (auth is null) problems.Add("Sign-in facts unavailable");
        var machines = (auth?.Machines ?? []).Select(m => Machine(m, overview, now, m.Machine == machine)).ToArray();
        foreach (var view in machines) {
            if (view.Reporter != "healthy") problems.Add(view.Machine + " reporter " + view.Reporter);
            foreach (var provider in view.Providers.Where(p => p.Mark == Bad))
                problems.Add(view.Machine + " " + provider.Name + " " + provider.Summary.Split(" · ")[0]);
            // A warning counts whatever the state: two versions with an unconfirmed sign-in still need a look.
            foreach (var provider in view.Providers.Where(p => p.Warning is not null))
                problems.Add(view.Machine + " " + provider.Name + ": " + provider.Warning);
        }
        if (overview.GitHub?.Windows.FirstOrDefault(w => w.Resource == "core") is { } core && core.Limit > 0 && core.Remaining < core.Limit / 10)
            problems.Add("GitHub REST " + core.Remaining.ToString("N0", CultureInfo.InvariantCulture) + " left");
        var changes = (auth?.Changes ?? []).Take(15).Select(change => Change(change, now)).ToArray();
        // A stalled health check is down, not just degraded: the service answers but can't reach its database.
        var state = health.Status != "ok" ? TrayState.Down : problems.Count == 0 ? TrayState.Healthy : TrayState.Degraded;
        return new(state, headline, ledger.Where(line => line.Length > 0).ToArray(), machines.FirstOrDefault(m => m.Machine == machine),
            machines.Where(m => m.Machine != machine).ToArray(), changes, problems.ToArray());
    }

    private static IEnumerable<string> Supervisor(Supervisor? local, DateTimeOffset now) {
        if (local is null) yield break;
        yield return "Supervisor " + (local.Verdict ?? "unknown") + (local.Failures > 0 ? ", " + local.Failures + " failed checks in a row" : "") +
            (local.CheckedAtUtc is { } at ? " · checked " + Ago(now - at) : "");
        if (local.LastRestartUtc is { } restart)
            yield return local.RestartsLastDay + " restart" + (local.RestartsLastDay == 1 ? "" : "s") + " in 24 h · last " + Ago(now - restart) +
                (local.LastRestartReason is { } reason ? " (" + reason + ")" : "");
    }

    private static MachineView Machine(AuthMachine machine, Overview overview, DateTimeOffset now, bool local) {
        var facts = machine.Providers.ToDictionary(p => p.Provider, StringComparer.Ordinal);
        var lines = new[] { "github", "codex", "claude" }.Select(provider =>
            Provider(provider, facts.GetValueOrDefault(provider), machine, overview, now)).ToArray();
        var note = machine.Providers.Length == 0 && machine.Reporter == "healthy" ? "no sign-in facts yet (its reporter is an older build)"
            : machine.LastSeenUtc is { } seen && now - seen > TimeSpan.FromMinutes(5) ? "last seen " + Ago(now - seen) : null;
        return new(machine.Machine, machine.Reporter, lines, note);
    }

    internal static string Name(string provider) => provider switch { "github" => "GitHub", "codex" => "Codex", "claude" => "Claude", _ => provider };

    private static ProviderLine Provider(string provider, AuthFact? fact, AuthMachine machine, Overview overview, DateTimeOffset now) {
        var name = Name(provider);
        var signIn = machine.SignIn.FirstOrDefault(s => s.Provider == provider);
        if (fact is null) {
            return signIn is null
                ? new(provider, name, Missing, "not reported", [], false)
                : new(provider, name, signIn.Required ? Bad : signIn.Reason == "unknown" ? Unknown : Ok,
                    signIn.Required ? Reason(signIn.Reason) : signIn.Reason == "unknown" ? "unknown" : "signed in", [("Agent", Reason(signIn.Reason))], signIn.Required);
        }
        var f = fact.Facts ?? [];
        var parts = new List<string>();
        var mark = fact.State switch { "signed-in" => Ok, "signed-out" => Bad, "not-installed" => Missing, _ => Unknown };
        parts.Add(fact.State switch { "signed-in" => "signed in", "signed-out" => "signed out", "not-installed" => "not installed", _ => "unknown" });
        if (f.GetValueOrDefault("agent-sign-in") is { } agent && agent is not ("ready" or "signed-out")) parts[0] += " (" + Reason(agent) + ")";
        string? warning = null;
        switch (provider) {
            case "github":
                if (f.GetValueOrDefault("account") is { } login) parts.Add(login);
                if (f.GetValueOrDefault("token-source") is { } source) parts.Add(source);
                if (f.GetValueOrDefault("scopes") is { } scopes) parts.Add(scopes.Split(',').Length + " scopes");
                if (overview.GitHub?.Windows.FirstOrDefault(w => w.Resource == "core") is { } core && core.Limit > 0)
                    parts.Add("REST " + core.Remaining.ToString("N0", CultureInfo.InvariantCulture) + " left");
                if (f.GetValueOrDefault("env-override") is { Length: > 0 } env) warning = env.Split(' ')[0] + " overrides the stored sign-in";
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
                if (f.GetValueOrDefault("versions") is { } versions) warning = versions.Split(',').Length + " versions installed";
                break;
        }
        if (warning is not null) { parts.Add(warning); if (mark == Ok) mark = Warn; }
        var details = f.OrderBy(pair => Order(pair.Key)).Select(pair => (Label(pair.Key), pair.Key is "binaries" or "env-override" ? pair.Value.Replace("; ", Environment.NewLine) : pair.Value)).ToList();
        details.Add(("Checked", Ago(now - fact.CheckedAtUtc)));
        return new(provider, name, mark, string.Join(" · ", parts), details.ToArray(), fact.State == "signed-out", warning);
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
            notes.Add((title, string.Join(Environment.NewLine, group.Take(4).Select(change => TrayView.Change(change, now).Split(" · ", 3)[2]))));
        }
        return notes;
    }

    private static string Key(AuthChange change) => change.Machine + "|" + change.Provider + "|" + change.Field + "|" + change.AtUtc.UtcTicks + "|" + change.After;
}
