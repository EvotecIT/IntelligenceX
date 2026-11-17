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
    private string? _cliAccount;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    internal static string DefaultRoot() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude-code-sessions")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");

    /// <summary>The account id for a Claude Code session id, or null when neither the desktop app nor the CLI says.</summary>
    public string? For(string sessionId) {
        if (_clock.GetUtcNow() - _readAt >= RereadAfter) Reread();
        return _accounts.TryGetValue(sessionId, out var account) ? account : _cliAccount;
    }

    /// <summary>Whether the desktop app archived the session; null when the app doesn't know it.</summary>
    public bool? Archived(string sessionId) {
        if (_clock.GetUtcNow() - _readAt >= RereadAfter) Reread();
        return _accounts.ContainsKey(sessionId) ? _archived.Contains(sessionId) : null;
    }

    private void Reread() {
        _readAt = _clock.GetUtcNow();
        (_accounts, _archived) = ReadWithArchive(_root);
        _cliAccount = CliAccount(_cli);
    }

    /// <summary>
    /// Session id to account from <c>claude-code-sessions/&lt;account&gt;/&lt;organization&gt;/local_*.json</c>, and the
    /// sessions the app has archived.
    /// </summary>
    internal static (Dictionary<string, string> Accounts, HashSet<string> Archived) ReadWithArchive(string root) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return (map, archived);
        foreach (var account in Directory.EnumerateDirectories(root)) {
            var accountId = Path.GetFileName(account);
            if (!Guid.TryParse(accountId, out _)) continue;
            foreach (var file in Directory.EnumerateFiles(account, "local_*.json", SearchOption.AllDirectories)) {
                try {
                    using var json = JsonDocument.Parse(File.ReadAllBytes(file));
                    if (json.RootElement.TryGetProperty("cliSessionId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } session) {
                        map[session] = accountId;
                        if (json.RootElement.TryGetProperty("isArchived", out var flag) && flag.ValueKind == JsonValueKind.True) archived.Add(session);
                    }
                } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
                    // A file the app is rewriting is read again on the next pass.
                }
            }
        }
        return (map, archived);
    }

    /// <summary>The account the CLI is signed in to, from its settings' account id alone.</summary>
    internal static string? CliAccount(string settings) {
        try {
            if (!File.Exists(settings)) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(settings));
            return json.RootElement.TryGetProperty("oauthAccount", out var account) && account.ValueKind == JsonValueKind.Object
                && account.TryGetProperty("accountUuid", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out _)
                ? id.GetString() : null;
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
            return null;
        }
    }
}
