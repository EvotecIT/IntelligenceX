namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    private static string ShortSha(string? sha) {
        if (string.IsNullOrWhiteSpace(sha)) {
            return "?";
        }
        return sha.Length > 7 ? sha.Substring(0, 7) : sha;
    }

    private static List<PullRequestReviewThread> SelectAssessmentCandidates(IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings) {
        var candidates = new List<PullRequestReviewThread>();
        foreach (var thread in threads) {
            if (thread.IsResolved) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }
            candidates.Add(thread);
            if (candidates.Count >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
        }
        return candidates;
    }

    private static string BuildThreadAssessmentPrompt(PullRequestContext context, IReadOnlyList<PullRequestReviewThread> threads,
        IReadOnlyList<PullRequestFile> files, ReviewSettings settings, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing existing PR review threads to decide if they should be resolved.");
        sb.AppendLine("Only mark a thread as resolved if the current diff clearly addresses the comment.");
        sb.AppendLine("If a human reply in the thread confirms the issue is fixed/expected/accepted, resolve it.");
        sb.AppendLine("If unclear or still valid, keep it open.");
        sb.AppendLine("If diff context shows `<file patch>`, it is the file-level diff because line-level context was unavailable.");
        sb.AppendLine();
        sb.AppendLine("Return JSON only in this format:");
        sb.AppendLine("{\"threads\":[{\"id\":\"...\",\"action\":\"resolve|keep|comment\",\"reason\":\"...\",\"evidence\":\"...\"}]}");
        sb.AppendLine("When resolving, evidence must quote a line from the diff context (e.g., \"42: fixed null check\").");
        sb.AppendLine();
        sb.AppendLine($"PR: {context.Owner}/{context.Repo} #{context.Number}");
        if (!string.IsNullOrWhiteSpace(context.Title)) {
            sb.AppendLine($"Title: {context.Title}");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"Diff range: {diffNote}");
        }
        sb.AppendLine();

        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, settings.MaxPatchChars);
        var index = 1;
        foreach (var thread in threads) {
            sb.AppendLine($"Thread {index}:");
            sb.AppendLine($"id: {thread.Id}");
            sb.AppendLine($"status: {(thread.IsOutdated ? "stale" : "active")}");
            var location = TryGetThreadLocation(thread, out var path, out var line)
                ? line > 0 ? $"{path}:{line}" : path
                : "<unknown>";
            sb.AppendLine($"location: {location}");
            sb.AppendLine("comments:");
            var commentCount = 0;
            foreach (var comment in thread.Comments) {
                if (commentCount >= settings.ReviewThreadsMaxComments) {
                    break;
                }
                var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!;
                var body = TrimComment(comment.Body, settings.MaxCommentChars);
                sb.AppendLine($"- {author}: {body}");
                commentCount++;
            }
            sb.AppendLine("diff context:");
            sb.AppendLine("```");
            sb.AppendLine(BuildThreadDiffContext(patchIndex, patchLookup, path, line, settings.MaxPatchChars));
            sb.AppendLine("```");
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    private static bool TryGetThreadLocation(PullRequestReviewThread thread, out string path, out int line) {
        path = string.Empty;
        line = 0;
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Path)) {
                continue;
            }
            path = comment.Path!;
            if (comment.Line.HasValue) {
                line = (int)comment.Line.Value;
            }
            return true;
        }
        return false;
    }

    private static string BuildThreadDiffContext(Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, string path, int lineNumber, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "<unavailable>";
        }
        if (lineNumber <= 0) {
            var normalizedPath = NormalizePath(path);
            return TryBuildFallbackPatch(patchLookup, normalizedPath, maxPatchChars);
        }
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !patchIndex.TryGetValue(normalized, out var lines) || lines.Count == 0) {
            return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
        }
        var context = 3;
        var lower = lineNumber - context;
        var upper = lineNumber + context;
        var snippet = lines.Where(l => l.LineNumber >= lower && l.LineNumber <= upper)
            .Select(l => $"{l.LineNumber}: {l.Text}")
            .ToList();
        if (snippet.Count > 0) {
            return string.Join("\n", snippet);
        }
        return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
    }

    private static Dictionary<string, string> BuildPatchLookup(IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalized = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalized)) {
                continue;
            }
            lookup[normalized] = TrimPatch(file.Patch!, maxPatchChars);
        }
        return lookup;
    }

    private static string TryBuildFallbackPatch(IReadOnlyDictionary<string, string> patchLookup, string normalizedPath,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(normalizedPath)) {
            return "<unavailable>";
        }
        if (!patchLookup.TryGetValue(normalizedPath, out var patch) || string.IsNullOrWhiteSpace(patch)) {
            return "<unavailable>";
        }
        var limited = TrimPatch(patch, maxPatchChars);
        return $"<file patch>\n{limited}";
    }

    private static string TrimPatch(string patch, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(patch)) {
            return string.Empty;
        }
        if (maxPatchChars <= 0 || patch.Length <= maxPatchChars) {
            return patch;
        }
        var newline = patch.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = patch.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var headerLines = new List<string>();
        var hunks = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                if (current is not null) {
                    hunks.Add(current);
                }
                current = new List<string> { line };
                continue;
            }
            if (current is null) {
                headerLines.Add(line);
            } else {
                current.Add(line);
            }
        }

        if (current is not null) {
            hunks.Add(current);
        }

        if (hunks.Count == 0) {
            return TrimHard(normalized, maxPatchChars, newline);
        }

        var header = string.Join(newline, headerLines);
        if (header.Length > maxPatchChars) {
            return TrimHard(header, maxPatchChars, newline);
        }

        var hunkTexts = hunks.Select(hunk => string.Join(newline, hunk)).ToList();
        if (hunkTexts.Count <= 1) {
            return AppendSequential(header, hunkTexts, maxPatchChars, newline);
        }

        var marker = "... (truncated) ...";
        var sb = new StringBuilder(maxPatchChars + 32);
        if (!string.IsNullOrEmpty(header)) {
            sb.Append(header);
        }

        if (!TryAppendSegment(sb, hunkTexts[0], maxPatchChars, newline)) {
            return TrimHard(header, maxPatchChars, newline);
        }

        if (hunkTexts.Count == 2) {
            if (TryAppendSegment(sb, hunkTexts[1], maxPatchChars, newline)) {
                return sb.ToString();
            }

            var fallback = new StringBuilder(maxPatchChars + 32);
            if (!string.IsNullOrEmpty(header)) {
                fallback.Append(header);
            }
            if (CanAppendTail(fallback, maxPatchChars, newline.Length, marker, hunkTexts[1], includeMarker: true)) {
                TryAppendSegment(fallback, marker, maxPatchChars, newline);
                TryAppendSegment(fallback, hunkTexts[1], maxPatchChars, newline);
                return fallback.ToString();
            }
            if (CanAppendTail(fallback, maxPatchChars, newline.Length, marker, hunkTexts[1], includeMarker: false)) {
                TryAppendSegment(fallback, hunkTexts[1], maxPatchChars, newline);
                return fallback.ToString();
            }

            TryAppendSegment(sb, marker, maxPatchChars, newline);
            return sb.ToString();
        }

        var lastHunk = hunkTexts[^1];
        var includedMiddle = 0;
        for (var i = 1; i < hunkTexts.Count - 1; i++) {
            var hunkText = hunkTexts[i];
            if (!CanAppendWithReserve(sb, hunkText, maxPatchChars, newline, marker, lastHunk)) {
                break;
            }
            if (!TryAppendSegment(sb, hunkText, maxPatchChars, newline)) {
                break;
            }
            includedMiddle++;
        }

        var needsTruncation = includedMiddle < hunkTexts.Count - 2;
        if (needsTruncation) {
            if (CanAppendTail(sb, maxPatchChars, newline.Length, marker, lastHunk, includeMarker: true)) {
                TryAppendSegment(sb, marker, maxPatchChars, newline);
                TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
            } else if (CanAppendTail(sb, maxPatchChars, newline.Length, marker, lastHunk, includeMarker: false)) {
                TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
            } else {
                TryAppendSegment(sb, marker, maxPatchChars, newline);
            }
        } else {
            TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
        }

        return sb.ToString();
    }

    private static string AppendSequential(string header, IReadOnlyList<string> hunks, int maxPatchChars, string newline) {
        var sb = new StringBuilder(maxPatchChars + 32);
        if (!string.IsNullOrEmpty(header)) {
            sb.Append(header);
        }

        foreach (var hunk in hunks) {
            if (!TryAppendSegment(sb, hunk, maxPatchChars, newline)) {
                TryAppendSegment(sb, "... (truncated) ...", maxPatchChars, newline);
                break;
            }
        }
        return sb.ToString();
    }

    private static bool CanAppendWithReserve(StringBuilder sb, string segment, int maxChars, string newline,
        string lastText, string marker, bool includeMarker) {
        if (string.IsNullOrEmpty(segment)) {
            return true;
        }
        var extra = segment.Length + (sb.Length > 0 ? newline.Length : 0);
        var reserve = 0;
        if (includeMarker) {
            reserve += marker.Length + newline.Length;
        }
        if (!string.IsNullOrEmpty(lastText)) {
            reserve += lastText.Length + newline.Length;
        }
        return sb.Length + extra + reserve <= maxChars;
    }

    private static bool CanAppendTail(StringBuilder sb, int maxChars, string newline,
        string lastText, string marker, bool includeMarker) {
        var currentLength = sb.Length;
        if (includeMarker) {
            var markerExtra = marker.Length + (currentLength > 0 ? newline.Length : 0);
            if (currentLength + markerExtra > maxChars) {
                return false;
            }
            currentLength += markerExtra;
        }
        if (string.IsNullOrEmpty(lastText)) {
            return true;
        }
        var lastExtra = lastText.Length + (currentLength > 0 ? newline.Length : 0);
        return currentLength + lastExtra <= maxChars;
    }

    private static bool TryAppendSegment(StringBuilder sb, string segment, int maxChars, string newline) {
        if (string.IsNullOrEmpty(segment)) {
            return true;
        }
        var extra = segment.Length + (sb.Length > 0 ? newline.Length : 0);
        if (sb.Length + extra > maxChars) {
            return false;
        }
        if (sb.Length > 0) {
            sb.Append(newline);
        }
        sb.Append(segment);
        return true;
    }

    private static bool CanAppendWithReserve(StringBuilder sb, string segment, int maxChars, string newline,
        string marker, string lastSegment) {
        var length = AppendLength(sb.Length, segment.Length, newline.Length);
        if (!string.IsNullOrEmpty(marker)) {
            length = AppendLength(length, marker.Length, newline.Length);
        }
        if (!string.IsNullOrEmpty(lastSegment)) {
            length = AppendLength(length, lastSegment.Length, newline.Length);
        }
        return length <= maxChars;
    }

    private static bool CanAppendTail(StringBuilder sb, int maxChars, int newlineLength, string marker,
        string lastSegment, bool includeMarker) {
        var length = sb.Length;
        if (includeMarker && !string.IsNullOrEmpty(marker)) {
            length = AppendLength(length, marker.Length, newlineLength);
        }
        if (!string.IsNullOrEmpty(lastSegment)) {
            length = AppendLength(length, lastSegment.Length, newlineLength);
        }
        return length <= maxChars;
    }

    private static int AppendLength(int currentLength, int segmentLength, int newlineLength) {
        return currentLength + segmentLength + (currentLength > 0 ? newlineLength : 0);
    }

    private static string TrimHard(string text, int maxChars, string newline) {
        if (text.Length <= maxChars) {
            return text;
        }
        var suffix = $"{newline}... (truncated)";
        if (suffix.Length >= maxChars) {
            return text.Substring(0, maxChars);
        }
        var take = Math.Max(0, maxChars - suffix.Length);
        return text.Substring(0, take) + suffix;
    }

    private static string BuildThreadAssessmentComment(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- intelligencex:thread-triage -->");
        sb.AppendLine("### IntelligenceX thread triage");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"_Diff range: {diffNote}_");
            sb.AppendLine();
        }
        if (resolved.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Resolved:");
            foreach (var item in resolved) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        if (kept.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Needs attention:");
            foreach (var item in kept) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    internal static string BuildFallbackTriageSummary(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept) {
        if (resolved.Count == 0 && kept.Count == 0) {
            return string.Empty;
        }
        if (resolved.Count > 0 && kept.Count == 0) {
            return $"Auto-resolve: resolved {resolved.Count} thread(s).";
        }
        if (resolved.Count == 0 && kept.Count > 0) {
            return $"Auto-resolve: kept {kept.Count} thread(s).";
        }
        return $"Auto-resolve: resolved {resolved.Count}, kept {kept.Count} thread(s).";
    }

    private static string BuildThreadAutoResolveSummaryComment(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- intelligencex:thread-autoresolve-summary -->");
        sb.AppendLine("### IntelligenceX auto-resolve summary");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"_Diff range: {diffNote}_");
            sb.AppendLine();
        }
        if (resolved.Count > 0) {
            sb.AppendLine("Resolved:");
            foreach (var item in resolved) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
            sb.AppendLine();
        }
        if (kept.Count > 0) {
            sb.AppendLine("Kept:");
            foreach (var item in kept) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task ReplyToKeptThreadsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> candidates, IReadOnlyDictionary<string, ThreadAssessment> assessments,
        string? headSha, string? diffNote, ReviewSettings settings, CancellationToken cancellationToken) {
        var replies = 0;
        foreach (var thread in candidates) {
            if (replies >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (!assessments.TryGetValue(thread.Id, out var assessment)) {
                continue;
            }
            if (assessment.Action == "resolve") {
                continue;
            }
            if (ThreadHasAutoReply(thread)) {
                continue;
            }
            var target = GetReplyTargetComment(thread);
            if (target?.DatabaseId is null) {
                continue;
            }
            var body = BuildThreadReply(assessment, headSha, diffNote);
            try {
                await github.CreatePullRequestReviewCommentReplyAsync(context.Owner, context.Repo, context.Number,
                        target.DatabaseId.Value, body, cancellationToken)
                    .ConfigureAwait(false);
                replies++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to reply to thread {thread.Id}: {ex.Message}");
            }
        }
    }

    private static bool ThreadHasAutoReply(PullRequestReviewThread thread) {
        foreach (var comment in thread.Comments) {
            if (!string.IsNullOrWhiteSpace(comment.Body) &&
                comment.Body.Contains(ThreadReplyMarker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static PullRequestReviewThreadComment? GetReplyTargetComment(PullRequestReviewThread thread) {
        if (thread.Comments.Count == 0) {
            return null;
        }
        var withDates = thread.Comments.Where(c => c.CreatedAt.HasValue).ToList();
        if (withDates.Count > 0) {
            return withDates.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
        }
        return thread.Comments[^1];
    }

    private static string BuildThreadReply(ThreadAssessment assessment, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine(ThreadReplyMarker);
        sb.AppendLine($"IntelligenceX triage: {assessment.Reason}");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine();
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine();
            sb.AppendLine($"_Diff range: {diffNote}_");
        }
        return sb.ToString().TrimEnd();
    }

    private readonly record struct ThreadTriageResult(string SummaryLine, string EmbeddedBlock, string FallbackSummary) {
        public static ThreadTriageResult Empty => new(string.Empty, string.Empty, string.Empty);
    }

    private static List<ThreadAssessment> ParseThreadAssessments(string output) {
        var result = new List<ThreadAssessment>();
        var obj = TryParseJsonObject(output);
        var array = obj?.GetArray("threads");
        if (array is null) {
            return result;
        }
        foreach (var item in array) {
            var thread = item?.AsObject();
            if (thread is null) {
                continue;
            }
            var id = thread.GetString("id");
            var actionRaw = thread.GetString("action");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actionRaw)) {
                continue;
            }
            var action = actionRaw.Trim().ToLowerInvariant();
            if (action is not "resolve" and not "keep" and not "comment") {
                action = "keep";
            }
            var reason = thread.GetString("reason") ?? string.Empty;
            var evidence = thread.GetString("evidence") ?? string.Empty;
            result.Add(new ThreadAssessment(id.Trim(), action, reason.Trim(), evidence.Trim()));
        }
        return result;
    }

    private static JsonObject? TryParseJsonObject(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }
        var trimmed = output.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) {
            return null;
        }
        var json = trimmed.Substring(start, end - start + 1);
        var value = JsonLite.Parse(json);
        return value?.AsObject();
    }

    internal sealed record ThreadAssessment(string Id, string Action, string Reason, string Evidence);

    private static bool HasValidResolveEvidence(string? evidence, PullRequestReviewThread thread,
        Dictionary<string, List<PatchLine>> patchIndex, IReadOnlyDictionary<string, string> patchLookup,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(evidence)) {
            return false;
        }
        var normalizedEvidence = NormalizeResolveEvidence(evidence);
        if (normalizedEvidence.Length == 0) {
            return false;
        }
        if (TryGetThreadLocation(thread, out var path, out var line)) {
            var context = BuildThreadDiffContext(patchIndex, patchLookup, path, line, maxPatchChars);
            if (!IsUnavailableDiffContext(context)) {
                return HasEvidenceInContext(context, normalizedEvidence);
            }
        }
        return HasEvidenceInAnyDiffContext(patchIndex, patchLookup, normalizedEvidence);
    }

    private static string NormalizeResolveEvidence(string evidence) {
        var normalized = evidence.Trim().Trim('"').Trim();
        normalized = normalized.Trim('`').Trim();
        if (normalized.Length >= 2 && normalized[0] == '\'' && normalized[^1] == '\'') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }
        return normalized;
    }

    private static bool IsUnavailableDiffContext(string? context) {
        return string.IsNullOrWhiteSpace(context) || context.Equals("<unavailable>", StringComparison.Ordinal);
    }

    private static bool HasEvidenceInAnyDiffContext(Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, string evidence) {
        foreach (var lines in patchIndex.Values) {
            if (lines.Count == 0) {
                continue;
            }
            var context = string.Join("\n", lines.Select(static line => $"{line.LineNumber}: {line.Text}"));
            if (HasEvidenceInContext(context, evidence)) {
                return true;
            }
        }
        foreach (var patch in patchLookup.Values) {
            if (string.IsNullOrWhiteSpace(patch)) {
                continue;
            }
            var context = $"<file patch>\n{patch}";
            if (HasEvidenceInContext(context, evidence)) {
                return true;
            }
        }
        return false;
    }

    private static bool HasEvidenceInContext(string context, string evidence) {
        if (context.Contains(evidence, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        var normalizedContext = NormalizeSnippetText(context);
        var normalizedEvidence = NormalizeSnippetText(evidence);
        if (string.IsNullOrWhiteSpace(normalizedContext) || string.IsNullOrWhiteSpace(normalizedEvidence)) {
            return false;
        }
        return normalizedContext.Contains(normalizedEvidence, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThreadAssessmentId(string? id) {
        return id?.Trim() ?? string.Empty;
    }

    private static async Task<int> TryResolveKeptBotThreadsAfterNoBlockersAsync(
        GitHubClient github,
        GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> candidates,
        List<ThreadAssessment> resolved,
        List<ThreadAssessment> kept,
        ReviewSettings settings,
        CancellationToken cancellationToken) {
        if (kept.Count == 0) {
            return 0;
        }

        var candidateById = new Dictionary<string, PullRequestReviewThread>(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in candidates) {
            var normalizedId = NormalizeThreadAssessmentId(thread.Id);
            if (normalizedId.Length == 0 || candidateById.ContainsKey(normalizedId)) {
                continue;
            }
            candidateById[normalizedId] = thread;
        }

        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in resolved) {
            var normalizedId = NormalizeThreadAssessmentId(item.Id);
            if (normalizedId.Length > 0) {
                resolvedIds.Add(normalizedId);
            }
        }

        var sweepResolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sweepResolved = 0;
        foreach (var item in kept) {
            var normalizedId = NormalizeThreadAssessmentId(item.Id);
            if (normalizedId.Length == 0 || resolvedIds.Contains(normalizedId) || sweepResolvedIds.Contains(normalizedId)) {
                continue;
            }
            if (!candidateById.TryGetValue(normalizedId, out var thread)) {
                continue;
            }
            if (thread.IsResolved || thread.TotalComments > thread.Comments.Count || !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (!result.Resolved) {
                Console.Error.WriteLine($"Failed no-blockers sweep resolve for review thread {thread.Id}: {result.Error ?? "unknown error"}");
                continue;
            }

            sweepResolved++;
            sweepResolvedIds.Add(normalizedId);
            resolved.Add(new ThreadAssessment(item.Id, "resolve", FormatNoBlockersSweepReason(item.Reason), item.Evidence));
        }

        if (sweepResolvedIds.Count > 0) {
            kept.RemoveAll(item => sweepResolvedIds.Contains(NormalizeThreadAssessmentId(item.Id)));
        }

        return sweepResolved;
    }

    private static string FormatNoBlockersSweepReason(string reason) {
        const string suffix = "(no-blockers sweep)";
        return string.IsNullOrWhiteSpace(reason) ? suffix : $"{reason} {suffix}";
    }

    private static async Task<(bool Resolved, string? Error)> TryResolveThreadAsync(GitHubClient github,
        GitHubClient? fallbackGithub, string threadId, CancellationToken cancellationToken) {
        try {
            await github.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            return (true, null);
        } catch (Exception ex) {
            var isForbidden = IsIntegrationForbidden(ex);
            if (fallbackGithub is not null && isForbidden) {
                try {
                    await fallbackGithub.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                    return (true, null);
                } catch (Exception fallbackEx) {
                    // Only log after the integration-forbidden path actually fails (avoid false alarms).
                    LogIntegrationForbiddenHint();
                    return (false, fallbackEx.Message);
                }
            }
            if (isForbidden) {
                LogIntegrationForbiddenHint();
            }
            return (false, ex.Message);
        }
    }

    private static bool IsIntegrationForbidden(Exception ex) {
        if (ex.Message.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("\"FORBIDDEN\"", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && IsIntegrationForbidden(ex.InnerException);
    }

    private static void LogIntegrationForbiddenHint() {
        if (Interlocked.Exchange(ref _integrationForbiddenHintLogged, 1) == 1) {
            return;
        }
        // This hint is emitted only from auto-resolve thread resolution flows.
        var lines = new[] {
            "Auto-resolve thread resolution: GitHub returned \"Resource not accessible by integration\".",
            "Hint: This usually means the GitHub App installation token cannot resolve review threads.",
            "Hint: Re-authorize or reinstall the GitHub App after permission changes.",
            "Hint: Confirm the app installation includes this repository.",
            "Hint: Ensure the app has Pull requests: Read & write (and Issues: write if needed).",
            "Hint: Verify INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY point to the intended app.",
            "Hint: To bypass the app token, remove INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY to use GITHUB_TOKEN.",
            "Hint: GITHUB_TOKEN is provided automatically in GitHub Actions; outside Actions set a PAT in GITHUB_TOKEN."
        };
        Console.Error.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static bool ThreadHasOnlyBotComments(PullRequestReviewThread thread, ReviewSettings settings) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Author)) {
                return false;
            }
            if (!IsAutoResolveBot(comment.Author, settings)) {
                return false;
            }
        }
        return true;
    }

}
