using System.Diagnostics;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Whether a provider's CLI on this machine can be used right now, and how its user signs in.</summary>
internal sealed record ProviderSignIn(string Provider, bool Ready, string Reason, DateTimeOffset SinceUtc);

internal sealed partial class AgentService {
    private const string SignInHold = "claude-sign-in-required";
    private const string UnreachableHold = "claude-session-unreachable";
    private readonly object _unreachableGate = new();
    private DateTimeOffset _probeAt = DateTimeOffset.MinValue;

    /// <summary>When SendMessage last couldn't reach a request's session and how many times it tried, kept on disk so a
    /// restart doesn't resend everything at once.</summary>
    internal sealed record UnreachableEntry(DateTimeOffset AtUtc, int Attempts);

    private string UnreachableFile => Path.Combine(_settings.StateDirectory, "unreachable.json");

    private Dictionary<string, UnreachableEntry> LoadUnreachable() {
        try {
            return File.Exists(UnreachableFile)
                ? JsonSerializer.Deserialize<Dictionary<string, UnreachableEntry>>(File.ReadAllText(UnreachableFile)) ?? []
                : [];
        } catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    private void MarkUnreachable(string requestId) {
        lock (_unreachableGate) {
            var entries = LoadUnreachable();
            entries[requestId] = new(DateTimeOffset.UtcNow, (entries.GetValueOrDefault(requestId)?.Attempts ?? 0) + 1);
            LocalFiles.WriteAtomic(UnreachableFile, JsonSerializer.Serialize(entries));
        }
    }

    /// <summary>A session SendMessage couldn't reach waits for its next turn; it is sent again at most the policy's number
    /// of retries, the policy's interval apart.</summary>
    private bool Unreachable(string requestId) {
        lock (_unreachableGate) {
            if (LoadUnreachable().GetValueOrDefault(requestId) is not { } entry) return false;
            return entry.Attempts > _policy.UnreachableRetries ||
                DateTimeOffset.UtcNow - entry.AtUtc < TimeSpan.FromMinutes(_policy.UnreachableRetryMinutes);
        }
    }

    /// <summary>Forgets requests whose deliveries are settled, so the file holds only what is still waiting.</summary>
    private void ForgetUnreachable(IEnumerable<string> waiting) {
        lock (_unreachableGate) {
            var entries = LoadUnreachable();
            var keep = waiting.ToHashSet(StringComparer.Ordinal);
            if (entries.Keys.All(keep.Contains)) return;
            LocalFiles.WriteAtomic(UnreachableFile, JsonSerializer.Serialize(entries.Where(pair => keep.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value)));
        }
    }
    private readonly Dictionary<string, ProviderSignIn> _signIn = new(StringComparer.Ordinal);
    private readonly object _signInGate = new();
    private DateTimeOffset _signInCheckedAt = DateTimeOffset.MinValue;
    private int _claudeHeld;

    /// <summary>The one-step sign-in for a provider, as its user would type it.</summary>
    internal static string SignInCommand(string provider) => provider == "claude" ? "claude auth login" : "codex login";

    private bool Ready(string provider) {
        lock (_signInGate) return !_signIn.TryGetValue(provider, out var state) || state.Ready;
    }

    /// <summary>A usage limit can only be seen to pass by trying, so while Claude is at its limit one held delivery is
    /// let through per the policy's probe interval.</summary>
    private bool ProbeUsageLimit() {
        lock (_signInGate) {
            if (!_signIn.TryGetValue("claude", out var state) || state.Ready || state.Reason != "usage-limit") return false;
            if (DateTimeOffset.UtcNow - _probeAt < TimeSpan.FromMinutes(_policy.UsageLimitProbeMinutes)) return false;
            _probeAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>Checks sign-in for Claude and Codex every five minutes, or every minute while one needs it.</summary>
    private async Task CheckSignInAsync(CancellationToken stop) {
        bool waiting;
        lock (_signInGate) waiting = _signIn.Values.Any(state => !state.Ready);
        if (DateTimeOffset.UtcNow - _signInCheckedAt < TimeSpan.FromMinutes(waiting ? 1 : 5)) return;
        _signInCheckedAt = DateTimeOffset.UtcNow;
        if (await ClaudeStatusAsync(stop).ConfigureAwait(false) is { } claude) Observe("claude", claude.Ready, claude.Reason);
        if (await CodexStatusAsync(stop).ConfigureAwait(false) is { } codex) Observe("codex", codex.Ready, codex.Reason);
    }

    /// <summary>Records a provider's state and publishes it when it changes; a newly required sign-in prompts the user.</summary>
    private void Observe(string provider, bool ready, string reason) {
        ProviderSignIn? changed = null;
        lock (_signInGate) {
            var prior = _signIn.GetValueOrDefault(provider);
            // A status check can't see a usage limit, so it doesn't clear one; a successful send does.
            if (prior is { Ready: false, Reason: "usage-limit" } && ready && reason == "status") return;
            if (prior is null || prior.Ready != ready || !ready && prior.Reason != reason) {
                _signIn[provider] = changed = new(provider, ready, ready ? "signed-in" : reason, DateTimeOffset.UtcNow);
            }
        }
        if (changed is null) return;
        Publish(changed);
        Log(ready ? "sign-in-ok" : "sign-in-required", provider + " " + changed.Reason);
        if (!ready && OperatingSystem.IsMacOS()) Start("prompt:" + provider, () => PromptSignInAsync(provider, CancellationToken.None));
    }

    private void Publish(ProviderSignIn state) {
        try {
            _publishSignIn(new SignInFact(state.Provider, !state.Ready, state.Reason, SignInCommand(state.Provider), state.SinceUtc,
                DateTimeOffset.UtcNow, state.Provider == "claude" ? Volatile.Read(ref _claudeHeld) : 0));
        } catch (Exception error) when (error is IOException or InvalidOperationException) {
            Log("sign-in-unpublished", state.Provider + " " + error.Message);
        }
    }

    /// <summary>Updates the count of Claude deliveries held for sign-in, republishing the state when it changes.</summary>
    private void CountHeld(int held) {
        if (Interlocked.Exchange(ref _claudeHeld, held) == held) return;
        ProviderSignIn? state;
        lock (_signInGate) state = _signIn.GetValueOrDefault("claude");
        if (state is { Ready: false }) Publish(state);
    }

    /// <summary>`claude auth status --json`. Null when the Claude CLI isn't installed here.</summary>
    private async Task<(bool Ready, string Reason)?> ClaudeStatusAsync(CancellationToken stop) {
        var (code, output) = await RunAsync(_settings.ClaudeExecutable, ["auth", "status", "--json"], stop).ConfigureAwait(false);
        if (code is null) return null;
        try {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (!root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind != JsonValueKind.True) return (false, "signed-out");
            if (root.TryGetProperty("loginExpiresAt", out var expires) && expires.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(expires.GetString(), out var at) && at <= DateTimeOffset.UtcNow) return (false, "expired");
            return (true, "status");
        } catch (JsonException) {
            return code == 0 ? (true, "status") : (false, "signed-out");
        }
    }

    /// <summary>`codex login status`: exit code 0 means signed in. Null when Codex isn't installed here.</summary>
    private async Task<(bool Ready, string Reason)?> CodexStatusAsync(CancellationToken stop) {
        var (code, _) = await RunAsync(_settings.CodexExecutable, ["login", "status"], stop).ConfigureAwait(false);
        return code is null ? null : code == 0 ? (true, "status") : (false, "signed-out");
    }

    private static async Task<(int? Code, string Output)> RunAsync(string executable, string[] arguments, CancellationToken stop) {
        try {
            var start = new ProcessStartInfo(executable) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return (null, "");
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            return (process.ExitCode, output);
        } catch (System.ComponentModel.Win32Exception) {
            return (null, "");
        } catch (OperationCanceledException) when (!stop.IsCancellationRequested) {
            return (1, "");
        }
    }

    /// <summary>On a Mac, asks the signed-in user to sign in: a notification, then a dialog whose "Sign in" button opens
    /// Terminal running the provider's login command. AppleScript notifications can't carry an action of their own, so
    /// the dialog is the one-tap route. It gives up after an hour and isn't shown again until the state changes.</summary>
    private async Task PromptSignInAsync(string provider, CancellationToken stop) {
        var name = provider == "claude" ? "Claude" : "Codex";
        var executable = provider == "claude" ? _settings.ClaudeExecutable : _settings.CodexExecutable;
        var script = Path.Combine(_settings.StateDirectory, "sign-in-" + provider + ".command");
        var login = provider == "claude" ? "auth login" : "login";
        LocalFiles.WriteAtomic(script, "#!/bin/zsh -l\n'" + executable.Replace("'", "'\\''") + "' " + login + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var message = name + " on " + _machineId + " needs sign-in before the agent service can use it.";
        await RunAsync("/usr/bin/osascript", ["-e", "display notification \"" + message + "\" with title \"IntelligenceX Ledger\""], stop).ConfigureAwait(false);
        var (code, output) = await RunDialogAsync(message, stop).ConfigureAwait(false);
        if (code == 0 && output.Contains("Sign in", StringComparison.Ordinal))
            await RunAsync("/usr/bin/open", ["-a", "Terminal", script], stop).ConfigureAwait(false);
    }

    private static async Task<(int? Code, string Output)> RunDialogAsync(string message, CancellationToken stop) {
        using var hour = CancellationTokenSource.CreateLinkedTokenSource(stop);
        hour.CancelAfter(TimeSpan.FromMinutes(61));
        var start = new ProcessStartInfo("/usr/bin/osascript") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("display dialog \"" + message + "\" buttons {\"Later\", \"Sign in\"} default button \"Sign in\" " +
            "with title \"IntelligenceX Ledger\" giving up after 3600");
        try {
            using var process = Process.Start(start);
            if (process is null) return (null, "");
            var output = await process.StandardOutput.ReadToEndAsync(hour.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(hour.Token).ConfigureAwait(false);
            return (process.ExitCode, output);
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException) {
            return (null, "");
        }
    }
}
