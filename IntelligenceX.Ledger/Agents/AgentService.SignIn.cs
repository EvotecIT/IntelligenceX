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
    /// <summary>Consecutive signed-out status checks per provider; one alone is not enough to ask for sign-in.</summary>
    private readonly Dictionary<string, int> _signedOutChecks = new(StringComparer.Ordinal);
    private const int SignedOutConfirmations = 2;

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

    /// <summary>Checks sign-in for Claude and Codex every five minutes, or every minute while one needs it or a
    /// signed-out result waits to be confirmed.</summary>
    private async Task CheckSignInAsync(CancellationToken stop) {
        bool waiting;
        lock (_signInGate) waiting = _signIn.Values.Any(state => !state.Ready) || _signedOutChecks.Values.Any(count => count > 0);
        if (DateTimeOffset.UtcNow - _signInCheckedAt < TimeSpan.FromMinutes(waiting ? 1 : 5)) return;
        _signInCheckedAt = DateTimeOffset.UtcNow;
        if (await ClaudeStatusAsync(stop).ConfigureAwait(false) is { } claude) ObserveCheck("claude", claude);
        if (await CodexStatusAsync(stop).ConfigureAwait(false) is { } codex) ObserveCheck("codex", codex);
    }

    /// <summary>Applies a status check. A check that timed out or failed is "unknown" and never changes whether the
    /// provider is usable. A signed-out answer counts only when the next check agrees, so one slow or failed check on a
    /// busy machine neither publishes a sign-in item nor prompts; until then the state reads "unknown".</summary>
    internal void ObserveCheck(string provider, string result) {
        lock (_signInGate) {
            var signedOut = result is "signed-out" or "expired";
            var count = signedOut ? _signedOutChecks[provider] = _signedOutChecks.GetValueOrDefault(provider) + 1 : 0;
            if (!signedOut) _signedOutChecks.Remove(provider);
            if (signedOut && count < SignedOutConfirmations || result == "unknown") {
                // A provider already known to need sign-in keeps that state until a check shows otherwise.
                if (_signIn.GetValueOrDefault(provider) is { Ready: false }) return;
                result = "unknown";
            }
        }
        if (result == "signed-in") Observe(provider, true, "status");
        else if (result == "unknown") Observe(provider, true, "unknown");
        else Observe(provider, false, result);
    }

    /// <summary>Records a provider's state and publishes it when it changes; a newly required sign-in prompts the user.
    /// "unknown" keeps the provider usable and never prompts.</summary>
    private void Observe(string provider, bool ready, string reason) {
        ProviderSignIn? changed = null;
        lock (_signInGate) {
            var prior = _signIn.GetValueOrDefault(provider);
            // A status check can't see a usage limit, so it doesn't clear one; a successful send does.
            if (prior is { Ready: false, Reason: "usage-limit" } && ready && reason is "status" or "unknown") return;
            var shown = !ready ? reason : reason == "unknown" ? "unknown" : "signed-in";
            if (prior is null || prior.Ready != ready || prior.Reason != shown) {
                _signIn[provider] = changed = new(provider, ready, shown, DateTimeOffset.UtcNow);
                // Inside the lock, so the publisher sees states in the order they changed.
                Publish(changed);
            }
        }
        if (changed is null) return;
        Log(!ready ? "sign-in-required" : changed.Reason == "unknown" ? "sign-in-unknown" : "sign-in-ok", provider + " " + changed.Reason);
        if (!ready && OperatingSystem.IsMacOS()) Start("prompt:" + provider, () => PromptSignInAsync(provider, _life));
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
        lock (_signInGate) if (_signIn.GetValueOrDefault("claude") is { Ready: false } state) Publish(state);
    }

    /// <summary>`claude auth status --json`. Null when the Claude CLI isn't installed here.</summary>
    private async Task<string?> ClaudeStatusAsync(CancellationToken stop) {
        var run = await RunAsync(_settings.ClaudeExecutable, ["auth", "status", "--json"], stop).ConfigureAwait(false);
        return run.Missing ? null : ClaudeStatus(run.Code, run.Output, DateTimeOffset.UtcNow);
    }

    /// <summary>"signed-in", "signed-out", "expired", or "unknown" when the check timed out or its answer can't be read.</summary>
    internal static string ClaudeStatus(int? code, string output, DateTimeOffset now) {
        if (code is null) return "unknown";
        try {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("loggedIn", out var loggedIn) ||
                loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return code == 0 ? "signed-in" : "unknown";
            if (loggedIn.ValueKind == JsonValueKind.False) return "signed-out";
            if (root.TryGetProperty("loginExpiresAt", out var expires) && expires.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(expires.GetString(), out var at) && at <= now) return "expired";
            return "signed-in";
        } catch (JsonException) {
            return code == 0 ? "signed-in" : "unknown";
        }
    }

    /// <summary>`codex login status`. Null when Codex isn't installed here.</summary>
    private async Task<string?> CodexStatusAsync(CancellationToken stop) {
        var run = await RunAsync(CodexCli.Resolve(_settings.CodexExecutable), ["login", "status"], stop).ConfigureAwait(false);
        return run.Missing ? null : CodexStatus(run.Code);
    }

    /// <summary>Exit code 0 is signed in and any other finished exit signed out; a check that didn't finish is "unknown".</summary>
    internal static string CodexStatus(int? code) => code switch { null => "unknown", 0 => "signed-in", _ => "signed-out" };

    /// <summary>Runs a CLI for at most 30 seconds. Code is null when it didn't finish in time; Missing when it isn't installed.</summary>
    private static async Task<(int? Code, string Output, bool Missing)> RunAsync(string executable, string[] arguments, CancellationToken stop) {
        try {
            var start = new ProcessStartInfo(executable) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return (null, "", true);
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var end = deadline.Token.Register(() => Stop(process));
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            return (process.ExitCode, output, false);
        } catch (System.ComponentModel.Win32Exception) {
            return (null, "", true);
        } catch (OperationCanceledException) when (!stop.IsCancellationRequested) {
            return (null, "", false);
        }
    }

    /// <summary>On a Mac, asks the signed-in user to sign in: a notification, then a dialog whose "Sign in" button opens
    /// Terminal running the provider's login command. AppleScript notifications can't carry an action of their own, so
    /// the dialog is the one-tap route. It gives up after an hour and isn't shown again until the state changes.</summary>
    private async Task PromptSignInAsync(string provider, CancellationToken stop) {
        var name = provider == "claude" ? "Claude" : "Codex";
        var executable = provider == "claude" ? _settings.ClaudeExecutable : CodexCli.Resolve(_settings.CodexExecutable);
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

    /// <summary>Ends a child process that outlived its wait (or the service), with anything it started.</summary>
    internal static void Stop(Process process) {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
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
            using var end = hour.Token.Register(() => Stop(process));
            var output = await process.StandardOutput.ReadToEndAsync(hour.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(hour.Token).ConfigureAwait(false);
            return (process.ExitCode, output);
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException) {
            return (null, "");
        }
    }
}
