using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Runs helper turns with Claude Code in print mode. One helper is one Claude session: the first turn creates it
/// under a known id and later turns (an answer, a resume) continue it, so the conversation and its context carry over.
/// A turn is one process; its stream-json transcript gives the final answer, the model and the tokens.</summary>
internal sealed class ClaudeRunner {
    /// <summary>The CLI's permission modes (Claude Code 2.1).</summary>
    internal static readonly string[] PermissionModes = ["acceptEdits", "auto", "bypassPermissions", "default", "manual", "dontAsk", "plan"];

    /// <summary>Error of a resumed turn whose session doesn't exist, so the caller starts it with the task instead.</summary>
    internal const string NoSession = "claude-session-missing";

    private readonly string _executable;
    private readonly string _permissionMode;

    public ClaudeRunner(string executable, string permissionMode) {
        if (!PermissionModes.Contains(permissionMode, StringComparer.Ordinal))
            throw new InvalidDataException("ClaudeHelperPermissionMode '" + permissionMode + "' isn't one of " + string.Join(", ", PermissionModes) + ".");
        (_executable, _permissionMode) = (executable, permissionMode);
    }

    /// <summary>Starts or continues <paramref name="sessionId"/> in <paramref name="workspace"/> with one prompt and waits
    /// for the answer, up to <paramref name="limit"/>. <paramref name="launched"/> gets the process id and start time as soon
    /// as it runs; <paramref name="accepted"/> runs once Claude has opened the session with the prompt (its init line), which
    /// a resume of a missing session never reaches. The process never outlives this call: any way out stops it and waits.</summary>
    public async Task<TurnOutcome> RunTurnAsync(string sessionId, bool resume, string workspace, string prompt, JsonObject schema,
        string? model, TimeSpan limit, Func<int, DateTimeOffset, Task> launched, Func<Task> accepted, CancellationToken token,
        string? effort = null) {
        var start = new ProcessStartInfo(_executable) {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workspace
        };
        foreach (var argument in Arguments(sessionId, resume, schema, model, _permissionMode, effort)) start.ArgumentList.Add(argument);
        var turnId = "claude-" + sessionId;
        Process process;
        try { process = Process.Start(start) ?? throw new InvalidOperationException("claude did not start."); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) {
            return new(turnId, "rejected", null, TurnTokens.None, model ?? "claude", error.Message);
        }
        using var running = process;
        try {
            using var end = token.Register(() => AgentService.Stop(process));
            await launched(process.Id, process.StartTime.ToUniversalTime()).ConfigureAwait(false);
            await process.StandardInput.WriteAsync(prompt.AsMemory(), token).ConfigureAwait(false);
            process.StandardInput.Close();
            var errors = process.StandardError.ReadToEndAsync(token);
            var opened = false;
            // Only the last assistant message (for the model name) and the result line are needed; a long turn isn't kept whole.
            string? assistant = null, result = null;
            string[] Kept() => new[] { assistant, result }.OfType<string>().ToArray();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(limit);
            try {
                while (await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { } line) {
                    // Each line is activity: the limit counts quiet time, so a long, busy turn isn't cut off.
                    deadline.CancelAfter(limit);
                    if (!opened && line.Contains("\"type\":\"system\"", StringComparison.Ordinal) && line.Contains("\"subtype\":\"init\"", StringComparison.Ordinal)) {
                        opened = true;
                        await accepted().ConfigureAwait(false);
                    }
                    if (line.Contains("\"type\":\"result\"", StringComparison.Ordinal)) result = line;
                    else if (line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)) assistant = line;
                }
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                return Parse(Kept(), turnId, model) with {
                    Status = "timed-out", Error = "The turn showed no activity for " + limit.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " minutes."
                };
            }
            var outcome = Parse(Kept(), turnId, model);
            if (outcome.Status == "failed") {
                // The process has exited; its error output is read to the end, bounded in case a child still holds the pipe.
                var stderr = "";
                try { stderr = (await errors.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false)).Trim(); }
                catch (TimeoutException) { }
                if (resume && !opened && (stderr + " " + outcome.FinalText).Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
                    return outcome with { Status = "rejected", Error = NoSession };
                if (outcome.Error is null)
                    outcome = outcome with { Error = stderr.Length == 0 ? "claude exited with code " + process.ExitCode : stderr.Length > 300 ? stderr[..300] : stderr };
            }
            return outcome;
        } finally {
            StopAndWait(process);
        }
    }

    /// <summary>Stops the process if it is still running and waits briefly for it to exit.</summary>
    private static void StopAndWait(Process process) {
        try {
            if (process.HasExited) return;
            AgentService.Stop(process);
            process.WaitForExit(10_000);
        } catch (InvalidOperationException) { }
    }

    /// <summary>Stops a helper's Claude process left by an earlier run of this service, identified by its id and start
    /// time so a reused id isn't touched. True when no such process is running any more.</summary>
    internal static bool StopLeftover(int processId, DateTimeOffset startedUtc) {
        try {
            using var process = Process.GetProcessById(processId);
            if (Math.Abs((process.StartTime.ToUniversalTime() - startedUtc.UtcDateTime).TotalSeconds) > 2) return true;
            AgentService.Stop(process);
            return process.WaitForExit(10_000);
        } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; }
    }

    internal static IEnumerable<string> Arguments(string sessionId, bool resume, JsonObject schema, string? model, string permissionMode,
        string? effort = null) {
        yield return "-p";
        yield return resume ? "--resume" : "--session-id";
        yield return sessionId;
        yield return "--output-format";
        yield return "stream-json";
        yield return "--verbose";
        yield return "--json-schema";
        yield return schema.ToJsonString();
        yield return "--permission-mode";
        yield return permissionMode;
        if (model is not null) { yield return "--model"; yield return model; }
        if (effort is not null) { yield return "--effort"; yield return effort; }
    }

    /// <summary>The turn's outcome from its transcript: the result line's structured answer (or text) and its usage. Only
    /// an HTTP 429 from the API counts as a usage limit; other errors are failures the helper resumes or reports.</summary>
    internal static TurnOutcome Parse(IEnumerable<string> lines, string turnId, string? requestedModel) {
        var model = requestedModel ?? "claude";
        string? final = null, error = null;
        var status = "failed";
        var tokens = TurnTokens.None;
        foreach (var line in lines) {
            JsonElement root;
            try { root = JsonDocument.Parse(line).RootElement.Clone(); } catch (JsonException) { continue; }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)) continue;
            if (type.GetString() == "assistant" && root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("model", out var used) && used.GetString() is { Length: > 0 } name) model = name;
            if (type.GetString() != "result") continue;
            if (root.TryGetProperty("usage", out var usage)) {
                long Long(string field) => usage.TryGetProperty(field, out var n) && n.TryGetInt64(out var v) ? v : 0;
                var input = Long("input_tokens") + Long("cache_creation_input_tokens");
                tokens = new(input, Long("cache_read_input_tokens"), Long("output_tokens"),
                    input + Long("cache_read_input_tokens") + Long("output_tokens"));
            }
            final = root.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object
                ? structured.GetRawText() : root.TryGetProperty("result", out var text) ? text.GetString() : null;
            var failed = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;
            status = failed ? "failed" : "completed";
            if (!failed) continue;
            // The status may be absent or null; only a number is read.
            var code = root.TryGetProperty("api_error_status", out var statusCode) && statusCode.ValueKind == JsonValueKind.Number &&
                statusCode.TryGetInt32(out var value) ? value : 0;
            error = code == 429 ? CodexRunner.UsageLimitPrefix + "Claude usage limit"
                : code is 401 or 403 ? "Claude signed-out" : final ?? "Claude reported an error";
        }
        return new(turnId, status, final, tokens, model, error);
    }
}
