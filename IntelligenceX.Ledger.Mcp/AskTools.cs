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
        "Source project/branch/commit default to this session's Git checkout. Reuse the same id when retrying after an error.")]
    public Task<CoordinationWork> Ask(string task, string? machine = null, string? provider = null, string? kind = null,
        string? capability = null, string? id = null, string? project = null, string? branch = null, string? commit = null,
        CancellationToken token = default) {
        var (returnProvider, session) = CurrentSession();
        var source = GitSource();
        return Command(client.AskAsync(new CoordinationAsk(id ?? "ask-" + Guid.NewGuid().ToString("N"), task,
            project ?? source.Project ?? throw new McpException("{\"outcome\":\"rejected\",\"error\":\"project-required\"}"),
            branch ?? source.Branch ?? "main", commit ?? source.Commit ?? throw new McpException("{\"outcome\":\"rejected\",\"error\":\"commit-required\"}"),
            returnProvider, session, machine, provider, kind, capability), token));
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
    internal static (string Provider, string SessionId) CurrentSession(Func<string, string?>? environment = null) {
        environment ??= Environment.GetEnvironmentVariable;
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

    private static string? Git(params string[] arguments) {
        try {
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(5000) && process.ExitCode == 0 && output.Length > 0 ? output : null;
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private static async Task<T> Command<T>(Task<T> command) {
        try { return await command.ConfigureAwait(false); }
        catch (CoordinationCommandRejectedException error) {
            throw new McpException(JsonSerializer.Serialize(new { outcome = "rejected", error = error.ErrorCode }));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or ArgumentException or HttpRequestException or TaskCanceledException) {
            throw new McpException("{\"outcome\":\"uncertain\"}");
        }
    }
}
