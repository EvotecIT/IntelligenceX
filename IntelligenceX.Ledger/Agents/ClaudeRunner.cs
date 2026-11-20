using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Runs helper turns with Claude Code in print mode. One helper is one Claude session: the first turn creates it
/// under a known id and later turns (an answer, a resume) continue it, so the conversation and its context carry over.
/// A turn is one process; its stream-json transcript gives the final answer, the model and the tokens.</summary>
internal sealed class ClaudeRunner(string executable, string permissionMode) {
    /// <summary>Starts or continues <paramref name="sessionId"/> in <paramref name="workspace"/> with one prompt and waits
    /// for the answer, up to <paramref name="limit"/>. <paramref name="launched"/> runs once the process has the prompt.</summary>
    public async Task<TurnOutcome> RunTurnAsync(string sessionId, bool resume, string workspace, string prompt, JsonObject schema,
        string? model, TimeSpan limit, Func<Task> launched, CancellationToken token) {
        var start = new ProcessStartInfo(executable) {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workspace
        };
        foreach (var argument in Arguments(sessionId, resume, schema, model, permissionMode)) start.ArgumentList.Add(argument);
        var turnId = "claude-" + sessionId;
        Process process;
        try { process = Process.Start(start) ?? throw new InvalidOperationException("claude did not start."); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) {
            return new(turnId, "rejected", null, TurnTokens.None, model ?? "claude", error.Message);
        }
        using var running = process;
        using var end = token.Register(() => AgentService.Stop(process));
        await process.StandardInput.WriteAsync(prompt.AsMemory(), token).ConfigureAwait(false);
        process.StandardInput.Close();
        await launched().ConfigureAwait(false);
        var errors = process.StandardError.ReadToEndAsync(token);
        // Only the last assistant message (for the model name) and the result line are needed; a long turn isn't kept whole.
        string? assistant = null, result = null;
        string[] Kept() => new[] { assistant, result }.OfType<string>().ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(limit);
        try {
            while (await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { } line) {
                if (line.Contains("\"type\":\"result\"", StringComparison.Ordinal)) result = line;
                else if (line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)) assistant = line;
            }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            AgentService.Stop(process);
            return Parse(Kept(), turnId, model) with {
                Status = "timed-out", Error = "The turn passed its " + limit.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-minute limit."
            };
        }
        var outcome = Parse(Kept(), turnId, model);
        if (outcome.Status == "failed" && outcome.Error is null) {
            var stderr = errors.IsCompletedSuccessfully ? errors.Result.Trim() : "";
            outcome = outcome with { Error = stderr.Length == 0 ? "claude exited with code " + process.ExitCode : stderr.Length > 300 ? stderr[..300] : stderr };
        }
        return outcome;
    }

    internal static IEnumerable<string> Arguments(string sessionId, bool resume, JsonObject schema, string? model, string permissionMode) {
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
    }

    /// <summary>The turn's outcome from its transcript: the result line's structured answer (or text), its usage and
    /// whether the CLI failed for sign-in or a usage limit.</summary>
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
            if (failed) error = ClaudeSender.Failure(root) is "usage-limit" ? CodexRunner.UsageLimitPrefix + " Claude usage limit"
                : ClaudeSender.Failure(root) is { } reason ? "Claude " + reason : final;
        }
        return new(turnId, status, final, tokens, model, error);
    }
}
