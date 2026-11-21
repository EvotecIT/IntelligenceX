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
    [Description("Get work done on another machine, or by another agent on this one: Codex or Claude on a named machine (or any machine " +
        "with the capability) does the task. Use this rather than SSH or opening a chat there; no connection between the machines is needed. " +
        "One call creates the goal and the request. " +
        "The answer, any question from the helper and any service notice arrive in this session as a new message; do not poll. " +
        "machine: win-pc, mac, mac-studio (policy machines). provider: codex or claude; omit to use policy routing for kind. " +
        "project: the repository the work is in, by name or owner/name; omit it for this session's Git checkout. For another repository, " +
        "branch defaults to its default branch and commit to its latest (pass commit to pin it; a review needs one). " +
        "Rejections name the field and carry a hint. Reuse the same id when retrying after an error. " +
        "sessionId is this session's own id, where the answer is delivered; pass it when the tool reports session-unknown. " +
        "Codex: the CODEX_THREAD_ID environment variable of your shell commands. Claude Code: CLAUDE_CODE_SESSION_ID, and also " +
        "sessionAddress: this session's own name and ref exactly as ListAgents shows it (\"This session is <name> [<ref>]\"). " +
        "continues: the id of an earlier Codex ask of yours; the helper carries on from that conversation (a forked copy) on the same machine. " +
        "The Ledger policy picks a model and effort for each kind of work and provider (for example Sonnet for reviews, Opus for " +
        "implementation and design reviews); model and effort (low, medium, high, xhigh, max; ultra is Codex only) override that " +
        "choice for this ask, for Codex or Claude, such as model \"opus\" for a risky review. kind: engine (default), review, visual, quick. " +
        "output: \"review\" for a merge-gate review of the branch at the commit, returned as JSON with pass, verdict, failedChecks, findings and validation. " +
        "output: \"visual-review\" asks Claude for a design verdict (approve, changes or blocked, with findings) on the images you attach and, " +
        "where the task says how to reach it, the running product; name a machine that renders the target natively. " +
        "images: up to 8 local PNG, JPEG, WebP or GIF files (screenshots, mock-ups), uploaded first; the helper gets them as files. " +
        "A result that carries images names them as ledger-image:<id>; ask_images fetches them. " +
        "continues with a Claude ask (or a visual review) gives the new helper the earlier result and its images, for the next review round. " +
        "pullRequest (\"owner/name#123\" or its URL) with a visual review: the verdict, findings and images are also posted there as a comment.")]
    public async Task<CoordinationWork> Ask(string task, string? machine = null, string? provider = null, string? kind = null,
        string? capability = null, string? id = null, string? project = null, string? branch = null, string? commit = null,
        string? sessionId = null, string? sessionProvider = null, string? sessionAddress = null, string? continues = null,
        string? model = null, string? effort = null, string? output = null, CancellationToken token = default,
        [Description("Optional short title visible in the shared Ledger work view.")] string? displayTitle = null,
        [Description("Local image files to send with the task.")] string[]? images = null,
        [Description("Pull request where a visual review posts its verdict and images.")] string? pullRequest = null) {
        var (returnProvider, session) = CurrentSession(sessionId, sessionProvider);
        var target = Source(project, branch, commit, output, GitSource);
        // An uncertain outcome names the id, so a retry with it returns the ask the service may already have taken.
        var askId = id ?? "ask-" + Guid.NewGuid().ToString("N");
        var uploaded = images is { Length: > 0 } ? await UploadAsync(images, token).ConfigureAwait(false) : null;
        return await Command(client.AskAsync(new CoordinationAsk(askId, task, target.Project, target.Branch, target.Commit,
            returnProvider, session, machine, provider, kind, capability, sessionAddress, continues, model, effort, output) {
            DisplayTitle = displayTitle, Images = uploaded, PullRequest = pullRequest
        }, token), askId).ConfigureAwait(false);
    }

    [McpServerTool(Name = "ask_images", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Download the images a result of your ask refers to (ledger-image:<id>) and return their local paths, to open or " +
        "view them. directory defaults to a ledger-images folder in the machine's task scratch (EVOTEC_SCRATCH_ROOT) or temp folder.")]
    public async Task<string[]> AskImages(string requestId, string? directory = null, CancellationToken token = default) {
        var result = await client.ResultContentAsync(requestId, token).ConfigureAwait(false);
        var ids = CoordinationImage.References(result.Content);
        if (ids.Length == 0) return [];
        var folder = directory ?? Path.Combine(Environment.GetEnvironmentVariable("EVOTEC_SCRATCH_ROOT") is { Length: > 0 } scratch
            ? scratch : Path.GetTempPath(), "ledger-images", requestId);
        var paths = new List<string>();
        foreach (var (image, index) in ids.Select((image, index) => (image, index)))
            paths.Add(await client.DownloadImageAsync(image, folder, token, (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "-")
                .ConfigureAwait(false));
        return paths.ToArray();
    }

    /// <summary>Uploads the asker's image files and returns their ids. A missing, oversized or non-image file is rejected
    /// before anything is asked, naming the file.</summary>
    private async Task<string[]> UploadAsync(string[] files, CancellationToken token) {
        static McpException Rejected(string error, string hint) => new(JsonSerializer.Serialize(new { outcome = "rejected", error, hint }));
        if (files.Length > CoordinationImage.MaximumPerMessage)
            throw Rejected("too-many-images", "Send at most " + CoordinationImage.MaximumPerMessage + " images with one ask.");
        var ids = new List<string>();
        foreach (var file in files) {
            var info = new FileInfo(file);
            if (!info.Exists) throw Rejected("image-not-found", file);
            if (info.Length is 0 or > CoordinationImage.MaximumBytes) throw Rejected("image-too-large", file + " is empty or larger than 8 MB.");
            var bytes = await File.ReadAllBytesAsync(info.FullName, token).ConfigureAwait(false);
            if (CoordinationImage.Sniff(bytes) is null) throw Rejected("image-type-unsupported", file + " is not a PNG, JPEG, WebP or GIF image.");
            var image = await client.UploadImageAsync(bytes, token).ConfigureAwait(false);
            if (!ids.Contains(image.Id)) ids.Add(image.Id);
        }
        return ids.ToArray();
    }

    /// <summary>Commit recorded when work in another repository isn't pinned: the helper uses the latest of the branch.</summary>
    internal const string Unpinned = "0000000000000000000000000000000000000000";

    /// <summary>Branch recorded when work in another repository names none: the helper uses its default branch.</summary>
    internal const string DefaultBranch = "HEAD";

    /// <summary>Where the work is. Without a project it's this session's checkout. A project names a repository by name,
    /// <c>owner/name</c> or URL; when that is another repository, this checkout's branch and commit don't apply, so the
    /// work runs on the given branch and commit or, unpinned, on the latest of its default branch. A review needs a commit.</summary>
    internal static (string Project, string Branch, string Commit) Source(string? project, string? branch, string? commit,
        string? output, Func<(string? Project, string? Branch, string? Commit)> checkout) {
        static McpException Rejected(string error, string hint) =>
            new(JsonSerializer.Serialize(new { outcome = "rejected", error, hint }));
        var named = project is null ? null : RepositoryName(project);
        if (project is not null && named is null)
            throw Rejected("invalid-project", "Use the repository name, such as CanopyX, or owner/name.");
        var here = checkout();
        if (named is null || string.Equals(named, here.Project, StringComparison.OrdinalIgnoreCase)) {
            // The checkout's own spelling, since the service matches project names exactly.
            var current = here.Project ?? named ?? throw Rejected("project-required",
                "This session isn't in a Git checkout with an origin remote. Pass project (and commit for a review).");
            return (current, branch ?? here.Branch ?? "main", commit ?? here.Commit ?? throw Rejected("commit-required",
                "This checkout has no commit. Pass commit."));
        }
        if (commit is null && output == "review")
            throw Rejected("commit-required", "A review of another repository needs the exact commit to review.");
        return (named, branch ?? DefaultBranch, commit ?? Unpinned);
    }

    /// <summary>The repository name from <c>name</c>, <c>owner/name</c>, an HTTPS or SSH URL, with any <c>.git</c> removed.</summary>
    internal static string? RepositoryName(string value) {
        var name = value.Trim().TrimEnd('/');
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.Split('/', ':').Last();
        return DeclarationValidation.Identifier(name) ? name : null;
    }

    [McpServerTool(Name = "wake", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Wake this session once, then end your turn; do not poll or watch. ci: \"owner/repo#123\" wakes it when the checks on that " +
        "pull request's current head finish (passed or failed) or the pull request closes; afterMinutes wakes it after a delay. Give exactly one. " +
        "prompt is what you want to be told when it fires, such as the next step; the wake arrives as a new message with the reason first. " +
        "A newer ci wake for the same session and pull request replaces a waiting one. A ci wake that waits past the policy's expiry " +
        "(72 hours by default) arrives saying it expired. Reuse the same id when retrying after an error. sessionId and sessionAddress as for ask.")]
    public Task<CoordinationWake> Wake(string prompt, string? ci = null, double? afterMinutes = null, string? id = null,
        string? sessionId = null, string? sessionProvider = null, string? sessionAddress = null, CancellationToken token = default) {
        var (provider, session) = CurrentSession(sessionId, sessionProvider);
        string? repo = null;
        int? pr = null;
        if (ci is not null) {
            var hash = ci.LastIndexOf('#');
            if (hash <= 0 || !int.TryParse(ci[(hash + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
                throw new McpException("{\"outcome\":\"rejected\",\"error\":\"invalid-wake\"}");
            (repo, pr) = (ci[..hash], number);
        }
        var wakeId = id ?? "wake-" + Guid.NewGuid().ToString("N");
        return Command(client.WakeAsync(new CoordinationWakeRequest(wakeId, prompt, provider, session, sessionAddress, repo, pr, afterMinutes), token),
            wakeId, "wake");
    }

    [McpServerTool(Name = "wake_cancel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cancel one of your wakes that has not been delivered yet.")]
    public Task<CoordinationWake> WakeCancel(string id, CancellationToken token = default) => Command(client.CancelWakeAsync(id, token));

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
        var project = remote is null ? null : RepositoryName(remote);
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

    /// <summary>What to change for the rejections a caller can fix itself.</summary>
    private static readonly Dictionary<string, string> Hints = new(StringComparer.Ordinal) {
        ["invalid-checkpoint-project"] = "project is a repository name such as CanopyX.",
        ["invalid-checkpoint-branch"] = "branch is a branch name of at most 200 characters.",
        ["invalid-checkpoint-commit"] = "commit is the full 40- or 64-character SHA, not a short one.",
        ["project-not-authorized"] = "This machine's sessions may not ask about that project. It has to be added to the Ledger's coordination configuration.",
        ["ask-machine-unknown"] = "machine is one of the machines in the Ledger policy (the policy tool lists them).",
        ["ask-provider-unavailable"] = "No machine with that capability may start that provider. Omit provider or pick another machine."
    };

    private static async Task<T> Command<T>(Task<T> command, string? retryId = null, string tool = "ask") {
        try { return await command.ConfigureAwait(false); }
        catch (CoordinationCommandRejectedException error) {
            throw new McpException(Hints.TryGetValue(error.ErrorCode, out var hint)
                ? JsonSerializer.Serialize(new { outcome = "rejected", error = error.ErrorCode, hint })
                : JsonSerializer.Serialize(new { outcome = "rejected", error = error.ErrorCode }));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or ArgumentException or HttpRequestException or TaskCanceledException) {
            throw new McpException(retryId is null ? "{\"outcome\":\"uncertain\"}"
                : JsonSerializer.Serialize(new { outcome = "uncertain", id = retryId, retry = "Call " + tool + " again with this id." }));
        }
    }
}
