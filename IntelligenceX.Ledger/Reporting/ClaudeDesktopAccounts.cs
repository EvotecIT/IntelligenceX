using System.Text.Json;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Which Claude account a Claude Code session belongs to. The desktop app files each session it knows under the account
/// that ran it, so after switching accounts the old sessions stay readable here even though the app lists only the
/// signed-in account's. Read-only: only the session's id and folder name are used, never credentials. A session the
/// desktop app doesn't know (one started from the CLI) belongs to the account the CLI is signed in to. The app's archive
/// flag comes along, since nothing else records that a Claude session was archived.
/// </summary>
internal sealed class ClaudeDesktopAccounts(string? sessionsRoot = null, string? cliSettings = null, TimeProvider? clock = null) {
    internal static readonly TimeSpan RereadAfter = TimeSpan.FromMinutes(2);
    private readonly string _root = sessionsRoot ?? DefaultRoot();
    private readonly string _cli = cliSettings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Dictionary<string, string> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _archived = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (string Session, string Account, bool Archived)> _files = new(StringComparer.OrdinalIgnoreCase);
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

    private void Reread() {
        _readAt = _clock.GetUtcNow();
        _files = ReadFiles(_root, _files);
        _accounts = new(StringComparer.OrdinalIgnoreCase);
        _archived = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (session, account, archived) in _files.Values) {
            _accounts[session] = account;
            if (archived) _archived.Add(session);
        }
        _cliAccount = CliAccount(_cli);
    }

    /// <summary>
    /// Session id to account from <c>claude-code-sessions/&lt;account&gt;/&lt;organization&gt;/local_*.json</c>, and the
    /// sessions the app has archived.
    /// </summary>
    internal static (Dictionary<string, string> Accounts, HashSet<string> Archived) ReadWithArchive(string root) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (session, account, isArchived) in ReadFiles(root, []).Values) {
            map[session] = account;
            if (isArchived) archived.Add(session);
        }
        return (map, archived);
    }

    /// <summary>Each session file's session, account and archive flag. A file that can't be read this pass (the app is
    /// rewriting it) keeps what <paramref name="previous"/> had for it; a folder that can't be listed is skipped.</summary>
    internal static Dictionary<string, (string Session, string Account, bool Archived)> ReadFiles(string root,
        Dictionary<string, (string Session, string Account, bool Archived)> previous) {
        var files = new Dictionary<string, (string Session, string Account, bool Archived)>(StringComparer.OrdinalIgnoreCase);
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
                                files[file] = (session, accountId, rootElement.TryGetProperty("isArchived", out var flag) && flag.ValueKind == JsonValueKind.True);
                        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
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
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
            return null;
        }
    }
}
