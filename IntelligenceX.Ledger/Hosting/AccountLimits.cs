using System.Diagnostics;
using System.Text;
using System.Text.Json;

using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>
/// Reads the Ledger host's account limits through supported client interfaces only: the Codex app server's
/// <c>account/rateLimits/read</c>, and the last Claude Code status line input saved by the status line recorder. Never
/// reads credentials. Codex answers are cached for a minute so repeated questions don't start a server each time.
/// </summary>
public static class AccountLimits {
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateTimeOffset At, AccountLimit? Value)? _codex;
    internal static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan CodexTimeout = TimeSpan.FromSeconds(8);

    public static async Task<AccountLimit[]> ReadAsync(CancellationToken token) {
        var codex = await CodexAsync(token).ConfigureAwait(false);
        var claude = Claude(ClaudeSnapshotPath(), DateTimeOffset.UtcNow);
        var machine = Environment.GetEnvironmentVariable("EVOTEC_MACHINE") is { Length: > 0 } named ? named : Environment.MachineName.ToLowerInvariant();
        return new[] { codex, claude }.OfType<AccountLimit>().Select(limit => limit with { Machine = machine }).ToArray();
    }

    private static async Task<AccountLimit?> CodexAsync(CancellationToken token) {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (_codex is { } cached && DateTimeOffset.UtcNow - cached.At < CacheFor) return cached.Value;
            var value = CodexBinary() is { } binary ? await ReadCodexAsync(binary, token).ConfigureAwait(false)
                : new AccountLimit("codex", "codex-app-server", DateTimeOffset.UtcNow) { Note = "Codex isn't installed where the Ledger looks; set CODEX_BIN." };
            _codex = (DateTimeOffset.UtcNow, value);
            return value;
        } finally { Gate.Release(); }
    }

    /// <summary>CODEX_BIN, then the desktop app's, npm's and Homebrew's copies, then PATH.</summary>
    internal static string? CodexBinary() {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("CODEX_BIN") };
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows()) {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin", "codex.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex",
                "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"));
        } else {
            candidates.Add("/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex");
            candidates.Add("/opt/homebrew/bin/codex");
            candidates.Add("/usr/local/bin/codex");
            candidates.Add(Path.Combine(home, ".npm-global", "bin", "codex"));
        }
        var name = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(dir => Path.Combine(dir, name)));
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static async Task<AccountLimit?> ReadCodexAsync(string binary, CancellationToken token) {
        var observed = DateTimeOffset.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(CodexTimeout);
        using var process = new Process {
            StartInfo = new ProcessStartInfo(binary, "app-server") {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
            }
        };
        try {
            process.Start();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            async Task Send(string json) { await process.StandardInput.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false); await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false); }
            await Send("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"ix-ledger","version":"1"}}}""").ConfigureAwait(false);
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line) {
                using var message = TryParse(line);
                if (message is null || !message.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                if (id.GetInt32() == 1) {
                    await Send("""{"method":"initialized"}""").ConfigureAwait(false);
                    await Send("""{"id":2,"method":"account/rateLimits/read","params":{}}""").ConfigureAwait(false);
                } else if (id.GetInt32() == 2) {
                    return message.RootElement.TryGetProperty("result", out var result) ? Codex(result, observed)
                        : new AccountLimit("codex", "codex-app-server", observed) { Note = "Codex didn't report its limits (signed out or an API-key login)." };
                }
            }
            return new AccountLimit("codex", "codex-app-server", observed) { Note = "Codex closed before answering." };
        } catch (Exception error) when (error is OperationCanceledException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException) {
            if (token.IsCancellationRequested) throw;
            return new AccountLimit("codex", "codex-app-server", observed) { Note = "Codex didn't answer within " + CodexTimeout.TotalSeconds + " s." };
        } finally {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
    }

    private static JsonDocument? TryParse(string line) {
        try { return JsonDocument.Parse(line); } catch (JsonException) { return null; }
    }

    /// <summary>The Codex app server's rate limits: the main window, credits and the free full resets left.</summary>
    internal static AccountLimit Codex(JsonElement result, DateTimeOffset observed) {
        var limits = result.TryGetProperty("rateLimits", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        // The longer window is the main one (weekly), the shorter one the short window (five hours), as for Claude.
        var windows = new[] { Child(limits, "primary"), Child(limits, "secondary") }.Where(w => w.ValueKind == JsonValueKind.Object)
            .OrderByDescending(w => Number(w, "windowDurationMins") ?? 0).ToArray();
        var main = windows.FirstOrDefault();
        var short_ = windows.Skip(1).FirstOrDefault();
        var credits = Child(limits, "credits");
        var resets = Child(result, "rateLimitResetCredits");
        var reached = Text(limits, "rateLimitReachedType") is not null || Bool(limits, "spendControlReached") == true || Bool(result, "ordinaryUsageAllowed") == false;
        return new AccountLimit("codex", "codex-app-server", observed) {
            Plan = Text(limits, "planType"),
            UsedPercent = Number(main, "usedPercent"),
            WindowMinutes = (int?)Number(main, "windowDurationMins"),
            ResetsAtUtc = Number(main, "resetsAt") is { } at ? DateTimeOffset.FromUnixTimeSeconds((long)at) : null,
            ShortUsedPercent = Number(short_, "usedPercent"),
            ShortResetsAtUtc = Number(short_, "resetsAt") is { } shortAt ? DateTimeOffset.FromUnixTimeSeconds((long)shortAt) : null,
            HasCredits = Bool(credits, "hasCredits"),
            CreditBalance = Text(credits, "balance"),
            FreeResets = (int?)Number(resets, "availableCount"),
            // Not reached only when Codex says ordinary usage is allowed; otherwise unknown stays unknown.
            LimitReached = reached ? true : Bool(result, "ordinaryUsageAllowed") == true ? false : null,
        };
    }

    /// <summary>
    /// Where a Claude Code status line command saves the <c>rate_limits</c> of its last input, as
    /// <c>{"observedAtUtc": "...", "rate_limits": {...}}</c>. Claude Code passes those figures only to a session's status
    /// line, so the machine's status line has to write this file; without it, Claude limits are left out.
    /// </summary>
    internal static string ClaudeSnapshotPath() =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "usage-limits.json");

    /// <summary>
    /// Claude Code shares its 5-hour and weekly use only with a running session's status line, so this is the last
    /// one any session saw; older than a day it says so instead of passing for current.
    /// </summary>
    internal static AccountLimit? Claude(string path, DateTimeOffset now) {
        try {
            if (!File.Exists(path)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new AccountLimit("claude", "claude-status-line", now) { Note = "The saved status line snapshot couldn't be read." };
            var at = root.TryGetProperty("observedAtUtc", out var seen) && seen.ValueKind == JsonValueKind.String &&
                seen.TryGetDateTimeOffset(out var when) ? when : File.GetLastWriteTimeUtc(path);
            var limits = Child(root, "rate_limits");
            var week = Child(limits, "seven_day");
            var five = Child(limits, "five_hour");
            return new AccountLimit("claude", "claude-status-line", at) {
                UsedPercent = Number(week, "used_percentage"),
                WindowMinutes = week.ValueKind == JsonValueKind.Object ? 10080 : null,
                ResetsAtUtc = Number(week, "resets_at") is { } r ? DateTimeOffset.FromUnixTimeSeconds((long)r) : null,
                ShortUsedPercent = Number(five, "used_percentage"),
                ShortResetsAtUtc = Number(five, "resets_at") is { } s ? DateTimeOffset.FromUnixTimeSeconds((long)s) : null,
                Note = now - at > TimeSpan.FromDays(1) ? "Last seen by a status line " + (int)(now - at).TotalHours + " h ago." : null,
            };
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
            return new AccountLimit("claude", "claude-status-line", now) { Note = "The saved status line snapshot couldn't be read." };
        }
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static double? Number(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
