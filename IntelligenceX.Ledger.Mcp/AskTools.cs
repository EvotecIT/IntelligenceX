using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Asks another agent for work in one call and answers its questions. Waiting happens in code: the result,
/// any question and any notice arrive in this session as a new message, so there is nothing to poll.</summary>
[McpServerToolType]
public sealed class AskTools(LedgerReadClient client) {
    [McpServerTool(Name = "ask", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ask Codex or Claude on a named machine (or any machine with the capability) to do a task. One call creates the goal and the request. " +
        "The answer, any question from the helper and any service notice arrive in this session as a new message; do not poll. " +
        "machine: win-pc, mac, mac-studio (policy machines). provider: codex or claude; omit to use policy routing for kind. " +
        "Source project/branch/commit default to this session's Git checkout. Reuse the same id when retrying after an error. " +
        "sessionId is this session's own id, where the answer is delivered; pass it when the tool reports session-unknown. " +
        "Codex: the CODEX_THREAD_ID environment variable of your shell commands. Claude Code: CLAUDE_CODE_SESSION_ID, and also " +
        "sessionAddress: this session's own name and ref exactly as ListAgents shows it (\"This session is <name> [<ref>]\"). " +
        "continues: the id of an earlier Codex ask of yours; the helper carries on from that conversation (a forked copy) on the same machine. " +
        "model and effort (low, medium, high, xhigh) override the Codex defaults for this ask. " +
        "output: \"review\" for a merge-gate review of the branch at the commit, returned as JSON with pass, verdict, failedChecks, findings and validation.")]
    public Task<CoordinationWork> Ask(string task, string? machine = null, string? provider = null, string? kind = null,
        string? capability = null, string? id = null, string? project = null, string? branch = null, string? commit = null,
        string? sessionId = null, string? sessionProvider = null, string? sessionAddress = null, string? continues = null,
        string? model = null, string? effort = null, string? output = null, CancellationToken token = default) {
        var (returnProvider, session) = CurrentSession(sessionId, sessionProvider);
        var source = project is null || branch is null || commit is null ? GitSource() : default;
        // An uncertain outcome names the id, so a retry with it returns the ask the service may already have taken.
        var askId = id ?? "ask-" + Guid.NewGuid().ToString("N");
        return Command(client.AskAsync(new CoordinationAsk(askId, task,
            project ?? source.Project ?? throw new McpException("{\"outcome\":\"rejected\",\"error\":\"project-required\"}"),
            branch ?? source.Branch ?? "main", commit ?? source.Commit ?? throw new McpException("{\"outcome\":\"rejected\",\"error\":\"commit-required\"}"),
            returnProvider, session, machine, provider, kind, capability, sessionAddress, continues, model, effort, output), token), askId);
    }

    [McpServerTool(Name = "ask_answer", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Answer question number N of an ask you made. The answer goes back into the same helper session, which then continues.")]
    public Task<CoordinationQuestion> Answer(string requestId, int number, string answer, CancellationToken token = default) =>
        Command(client.AnswerAsync(requestId, number, new(answer), token));

    [McpServerTool(Name = "ask_usage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tokens recorded for one ask (helper turns, wakes and deliveries), or totals for a UTC day (yyyy-MM-dd) when requestId is omitted.")]
    public async Task<object> Usage(string? requestId = null, string? date = null, CancellationToken token = default) =>
        requestId is not null ? await client.UsageAsync(requestId, token).ConfigureAwait(false)
            : await client.UsageReportAsync(date is null ? DateOnly.FromDateTime(DateTime.UtcNow)
                : DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);

    /// <summary>The session that launched this server: Codex sets CODEX_THREAD_ID and Claude Code CLAUDE_CODE_SESSION_ID.</summary>
    internal static (string Provider, string SessionId) CurrentSession(string? sessionId = null, string? sessionProvider = null,
        Func<string, string?>? environment = null) {
        environment ??= Environment.GetEnvironmentVariable;
        if (sessionId is { Length: > 0 }) {
            var provider = sessionProvider ?? (environment("CLAUDECODE") is { Length: > 0 } ? "claude" : "codex");
            if (provider is not ("codex" or "claude")) throw new McpException("{\"outcome\":\"rejected\",\"error\":\"session-provider-unknown\"}");
            return (provider, provider == "codex" ? sessionId.ToLowerInvariant() : sessionId);
        }
        if (environment("CODEX_THREAD_ID") is { Length: > 0 } codex) return ("codex", codex.ToLowerInvariant());
        if (environment("CLAUDE_CODE_SESSION_ID") is { Length: > 0 } claude) return ("claude", claude);
        throw new McpException("{\"outcome\":\"rejected\",\"error\":\"session-unknown\"}");
    }

    private static (string? Project, string? Branch, string? Commit) GitSource() {
        var remote = Git("remote", "get-url", "origin");
        var project = remote is null ? null : Path.GetFileNameWithoutExtension(remote.TrimEnd('/').Split('/', ':').Last());
        var branch = Git("rev-parse", "--abbrev-ref", "HEAD");
        return (project, branch is null or "HEAD" ? null : branch, Git("rev-parse", "HEAD"));
    }

    /// <summary>Runs git with its own stdin and both outputs drained, so it can neither read the MCP channel nor block on
    /// a full pipe; it is stopped after five seconds.</summary>
    private static string? Git(params string[] arguments) {
        try {
            var start = new ProcessStartInfo("git") {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            var text = output.Wait(1000) ? output.Result.Trim() : "";
            return process.ExitCode == 0 && text.Length > 0 ? text : null;
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private static async Task<T> Command<T>(Task<T> command, string? retryId = null) {
        try { return await command.ConfigureAwait(false); }
        catch (CoordinationCommandRejectedException error) {
            throw new McpException(JsonSerializer.Serialize(new { outcome = "rejected", error = error.ErrorCode }));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or ArgumentException or HttpRequestException or TaskCanceledException) {
            throw new McpException(retryId is null ? "{\"outcome\":\"uncertain\"}"
                : JsonSerializer.Serialize(new { outcome = "uncertain", id = retryId, retry = "Call ask again with this id." }));
        }
    }
}
