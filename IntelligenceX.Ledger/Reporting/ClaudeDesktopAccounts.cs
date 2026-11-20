using System.Text.Json;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Which Claude account a Claude Code session belongs to. The desktop app files each session it knows under the account
/// that ran it, so after switching accounts the old sessions stay readable here even though the app lists only the
/// signed-in account's. Read-only: only the session's ids, its parent's id and the folder name are used, never
/// credentials. A session the desktop app doesn't know (one started from the CLI) belongs to the account the CLI is
/// signed in to. The app's archive flag comes along, since nothing else records that a Claude session was archived, and
/// so does <c>spawnedFrom</c>, the desktop session this one was spawned from.
/// </summary>
internal sealed class ClaudeDesktopAccounts(string? sessionsRoot = null, string? cliSettings = null, TimeProvider? clock = null) {
    internal static readonly TimeSpan RereadAfter = TimeSpan.FromMinutes(2);
    private readonly string _root = sessionsRoot ?? DefaultRoot();
    private readonly string _cli = cliSettings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Dictionary<string, string> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _archived = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (string Parent, bool SideSession)> _parents = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DesktopSession> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One desktop session file: the CLI session id, its account, the archive flag, the app's own local id, the
    /// local id of the session it was spawned from, and whether that session started it on its own as a side session
    /// (rather than a person starting it from a suggestion card).</summary>
    internal sealed record DesktopSession(string Session, string Account, bool Archived, string? LocalId = null, string? SpawnedFrom = null,
        bool SideSession = false);
    private string? _cliAccount;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    internal static string DefaultRoot() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude-code-sessions")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");

    /// <summary>The account id for a Claude Code session id, or null when neither the desktop app nor the CLI says. The CLI's
    /// signed-in account applies only to a session from the CLI's default profile (<paramref name="cliProfile"/>).</summary>
    public string? For(string sessionId, bool cliProfile = true) {
        if (_clock.GetUtcNow() - _readAt >= RereadAfter) Reread();
        return _accounts.TryGetValue(sessionId, out var account) ? account : cliProfile ? _cliAccount : null;
    }

    /// <summary>Whether the desktop app archived the session; null when the app doesn't know it.</summary>
    public bool? Archived(string sessionId) {
        if (_clock.GetUtcNow() - _readAt >= RereadAfter) Reread();
        return _accounts.ContainsKey(sessionId) ? _archived.Contains(sessionId) : null;
    }

    /// <summary>The CLI session id of the desktop session this one was spawned from, and whether it was a side session that
    /// session started itself. Null when the app doesn't know the session, it wasn't spawned from another, or the parent's
    /// file is gone.</summary>
    public (string Parent, bool SideSession)? Parent(string sessionId) {
        if (_clock.GetUtcNow() - _readAt >= RereadAfter) Reread();
        return _parents.TryGetValue(sessionId, out var parent) ? parent : null;
    }

    private void Reread() {
        _readAt = _clock.GetUtcNow();
        _files = ReadFiles(_root, _files);
        _accounts = new(StringComparer.OrdinalIgnoreCase);
        _archived = new(StringComparer.OrdinalIgnoreCase);
        _parents = Parents(_files.Values);
        foreach (var file in _files.Values) {
            _accounts[file.Session] = file.Account;
            if (file.Archived) _archived.Add(file.Session);
        }
        _cliAccount = CliAccount(_cli);
    }

    /// <summary>Child CLI session id to parent CLI session id, through the app's local ids.</summary>
    internal static Dictionary<string, (string Parent, bool SideSession)> Parents(IEnumerable<DesktopSession> files) {
        var all = files.ToArray();
        var byLocal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in all) if (file.LocalId is { } local) byLocal[local] = file.Session;
        var parents = new Dictionary<string, (string Parent, bool SideSession)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in all)
            if (file.SpawnedFrom is { } from && byLocal.TryGetValue(from, out var parent) &&
                !string.Equals(parent, file.Session, StringComparison.OrdinalIgnoreCase))
                parents[file.Session] = (parent, file.SideSession);
        return parents;
    }

    /// <summary>
    /// Session id to account from <c>claude-code-sessions/&lt;account&gt;/&lt;organization&gt;/local_*.json</c>, and the
    /// sessions the app has archived.
    /// </summary>
    internal static (Dictionary<string, string> Accounts, HashSet<string> Archived) ReadWithArchive(string root) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ReadFiles(root, []).Values) {
            map[file.Session] = file.Account;
            if (file.Archived) archived.Add(file.Session);
        }
        return (map, archived);
    }

    /// <summary>Each session file's session, account and archive flag. A file that can't be read this pass (the app is
    /// rewriting it) keeps what <paramref name="previous"/> had for it; a folder that can't be listed is skipped.</summary>
    internal static Dictionary<string, DesktopSession> ReadFiles(string root, Dictionary<string, DesktopSession> previous) {
        var files = new Dictionary<string, DesktopSession>(StringComparer.OrdinalIgnoreCase);
        try {
            if (!Directory.Exists(root)) return files;
            foreach (var account in Directory.EnumerateDirectories(root)) {
                var accountId = Path.GetFileName(account);
                if (!Guid.TryParse(accountId, out _)) continue;
                try {
                    foreach (var file in Directory.EnumerateFiles(account, "local_*.json", SearchOption.AllDirectories)) {
                        try {
                            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
                            var rootElement = json.RootElement;
                            if (rootElement.ValueKind == JsonValueKind.Object && rootElement.TryGetProperty("cliSessionId", out var id) &&
                                id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } session)
                                files[file] = new(session, accountId, rootElement.TryGetProperty("isArchived", out var flag) && flag.ValueKind == JsonValueKind.True,
                                    LocalId(rootElement, "sessionId") ?? Path.GetFileNameWithoutExtension(file),
                                    rootElement.TryGetProperty("spawnedFrom", out var spawned) ? LocalId(spawned, "sessionId") : null,
                                    spawned.ValueKind == JsonValueKind.Object && spawned.TryGetProperty("sideSession", out var side) && side.ValueKind == JsonValueKind.True);
                        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException) {
                            // InvalidOperationException: a string the parser accepted but can't decode (a lone surrogate).
                            if (previous.TryGetValue(file, out var known)) files[file] = known;
                        }
                    }
                } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                    // An account folder that vanished or can't be listed keeps what was known about its files.
                    foreach (var pair in previous.Where(p => p.Key.StartsWith(account + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                        files[pair.Key] = pair.Value;
                }
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return previous;
        }
        return files;
    }

    private static string? LocalId(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 and <= 128 } id && id.StartsWith("local_", StringComparison.Ordinal) ? id : null;

    /// <summary>The source root ids of the CLI's default profile (~/.claude), whose sessions belong to the account in
    /// ~/.claude.json, computed the way root discovery names them, so a folder created later matches too. None when
    /// CLAUDE_CONFIG_DIR points the CLI elsewhere.</summary>
    internal static HashSet<string> CliProfileRoots() {
        if (Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 }) return [];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(home, ".claude", "projects"), Path.Combine(home, ".config", "claude", "projects") }
            .Select(path => IntelligenceX.Telemetry.Usage.SourceRootRecord.CreateStableId("claude",
                IntelligenceX.Telemetry.Usage.UsageSourceKind.LocalLogs, IntelligenceX.Telemetry.Usage.UsageTelemetryIdentity.NormalizePath(path)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The account the CLI is signed in to, from its settings' account id alone.</summary>
    internal static string? CliAccount(string settings) {
        try {
            if (!File.Exists(settings)) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(settings));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("oauthAccount", out var account) && account.ValueKind == JsonValueKind.Object
                && account.TryGetProperty("accountUuid", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out _)
                ? id.GetString() : null;
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException) {
            return null;
        }
    }
}
