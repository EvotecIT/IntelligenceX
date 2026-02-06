namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    internal static bool TryGetInlineThreadKey(PullRequestReviewThread thread, ReviewSettings settings, out string key) {
        key = string.Empty;
        if (thread.Comments.Count == 0) {
            return false;
        }
        if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
            return false;
        }

        var marker = thread.Comments.FirstOrDefault(comment =>
            comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase));
        if (marker is null || string.IsNullOrWhiteSpace(marker.Path) || !marker.Line.HasValue) {
            return false;
        }

        key = BuildInlineKey(marker.Path!, marker.Line.Value);
        return true;
    }

    private static string BuildRelatedPrsSection(PullRequestContext context, IReadOnlyList<RelatedPullRequest> related) {
        if (related.Count == 0) {
            return string.Empty;
        }
        var lines = new List<string>();
        foreach (var pr in related) {
            if (pr.RepoFullName.Equals(context.RepoFullName, StringComparison.OrdinalIgnoreCase) &&
                pr.Number == context.Number) {
                continue;
            }
            lines.Add($"- {pr.RepoFullName}#{pr.Number}: {pr.Title} ({pr.Url})");
        }
        if (lines.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Related pull requests (search results):");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string ResolveRelatedPrsQuery(PullRequestContext context, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.RelatedPrsQuery)) {
            return string.Empty;
        }
        return settings.RelatedPrsQuery!
            .Replace("{repo}", context.RepoFullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}", context.Owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", context.Repo, StringComparison.OrdinalIgnoreCase)
            .Replace("{number}", context.Number.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimComment(string value, int maxChars) {
        var text = value.Replace("\r", "").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return "<empty>";
        }
        if (text.Length <= maxChars) {
            return text;
        }
        return text.Substring(0, maxChars) + "...";
    }

    private static bool ShouldIncludeComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
        }
        if (!string.IsNullOrWhiteSpace(author)) {
            if (IsBotAuthor(author, settings)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool ShouldIncludeThreadComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
        }
        if (!settings.ReviewThreadsIncludeBots && !string.IsNullOrWhiteSpace(author)) {
            if (IsBotAuthor(author, settings)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool IsBotAuthor(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }
        var trimmedAuthor = author.Trim();
        var normalizedAuthor = NormalizeBotLogin(trimmedAuthor);
        if (settings.ReviewThreadsAutoResolveBotLogins.Count > 0) {
            foreach (var login in settings.ReviewThreadsAutoResolveBotLogins) {
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }
                var normalizedLogin = NormalizeBotLogin(login);
                if (string.IsNullOrWhiteSpace(normalizedLogin)) {
                    continue;
                }
                if (string.Equals(normalizedAuthor, normalizedLogin, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        if (trimmedAuthor.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.EndsWith("bot", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return trimmedAuthor.Equals("github-actions", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.Equals("intelligencex-review", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAutoResolveBot(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }
        var normalizedAuthor = NormalizeBotLogin(author.Trim());
        if (settings.ReviewThreadsAutoResolveBotLogins.Count > 0) {
            foreach (var login in settings.ReviewThreadsAutoResolveBotLogins) {
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }
                var normalizedLogin = NormalizeBotLogin(login);
                if (string.IsNullOrWhiteSpace(normalizedLogin)) {
                    continue;
                }
                if (string.Equals(normalizedAuthor, normalizedLogin, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }
        return IsBotAuthor(author, settings);
    }

    private static string NormalizeBotLogin(string login) {
        var trimmed = login.Trim();
        if (trimmed.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "[bot]".Length).TrimEnd();
        }
        return trimmed;
    }

    private static async Task<HashSet<string>?> PostInlineCommentsAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        IReadOnlyList<InlineReviewComment> inlineComments, CancellationToken cancellationToken) {
        var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inlineComments.Count == 0 || string.IsNullOrWhiteSpace(context.HeadSha)) {
            return expectedKeys;
        }

        var lineMap = BuildInlineLineMap(files);
        var patchIndex = BuildInlinePatchIndex(files);
        if (lineMap.Count == 0) {
            return null;
        }

        var limit = Math.Max(0, settings.CommentSearchLimit);
        var existing = await codeHostReader.ListPullRequestReviewCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in existing) {
            if (string.IsNullOrWhiteSpace(comment.Path) || !comment.Line.HasValue) {
                continue;
            }
            if (!comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            existingKeys.Add(BuildInlineKey(comment.Path!, comment.Line.Value));
        }

        var posted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inline in inlineComments) {
            var allowPost = posted < settings.MaxInlineComments;
            var normalizedPath = NormalizePath(inline.Path);
            var lineNumber = inline.Line;
            if ((string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) &&
                !string.IsNullOrWhiteSpace(inline.Snippet)) {
                if (!TryResolveSnippet(inline.Snippet!, patchIndex, normalizedPath, out normalizedPath, out lineNumber)) {
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) {
                continue;
            }
            if (!lineMap.TryGetValue(normalizedPath, out var allowedLines) ||
                !allowedLines.Contains(lineNumber)) {
                continue;
            }
            var body = inline.Body.Trim();
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }
            var key = BuildInlineKey(normalizedPath, lineNumber);
            expectedKeys.Add(key);
            if (!allowPost || existingKeys.Contains(key) || !seen.Add(key)) {
                continue;
            }
            body = $"{ReviewFormatter.InlineMarker}\n{body}";

            try {
                await github.CreatePullRequestReviewCommentAsync(context.Owner, context.Repo, context.Number, body,
                        context.HeadSha!, normalizedPath, lineNumber, cancellationToken)
                    .ConfigureAwait(false);
                posted++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Inline comment failed for {normalizedPath}:{lineNumber} - {ex.Message}");
            }
        }
        return expectedKeys;
    }

    private static Dictionary<string, HashSet<int>> BuildInlineLineMap(IReadOnlyList<PullRequestFile> files) {
        var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var allowed = ParsePatchLines(file.Patch!);
            if (allowed.Count > 0) {
                map[normalizedPath] = allowed;
            }
        }
        return map;
    }

    private sealed record PatchLine(int LineNumber, string Text, string NormalizedText);

    private static Dictionary<string, List<PatchLine>> BuildInlinePatchIndex(IReadOnlyList<PullRequestFile> files) {
        var index = new Dictionary<string, List<PatchLine>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var lines = ParsePatchContent(file.Patch!);
            if (lines.Count > 0) {
                index[normalizedPath] = lines;
            }
        }
        return index;
    }

    private static HashSet<int> ParsePatchLines(string patch) {
        var allowed = new HashSet<int>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                allowed.Add(newLine);
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                allowed.Add(newLine);
            }
        }
        return allowed;
    }

    private static List<PatchLine> ParsePatchContent(string patch) {
        var results = new List<PatchLine>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
            }
        }
        return results;
    }

    private static void AddPatchLine(List<PatchLine> results, int lineNumber, string text) {
        var normalized = NormalizeSnippetText(text);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return;
        }
        results.Add(new PatchLine(lineNumber, text, normalized));
    }

    private static bool TryResolveSnippet(string snippet, Dictionary<string, List<PatchLine>> patchIndex, string? preferredPath,
        out string path, out int lineNumber) {
        path = string.Empty;
        lineNumber = 0;
        var normalizedSnippet = NormalizeSnippetText(snippet);
        if (string.IsNullOrWhiteSpace(normalizedSnippet) || normalizedSnippet.Length < 3) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferredPath)) {
            var normalizedPreferred = NormalizePath(preferredPath);
            if (patchIndex.TryGetValue(normalizedPreferred, out var lines) &&
                TryResolveSnippetInLines(normalizedSnippet, normalizedPreferred, lines, out path, out lineNumber)) {
                return true;
            }
            return false;
        }

        var candidates = new List<(string path, int line, string normalized)>();
        foreach (var (filePath, lines) in patchIndex) {
            foreach (var line in lines) {
                if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                    candidates.Add((filePath, line.LineNumber, line.NormalizedText));
                }
            }
        }

        if (candidates.Count == 1) {
            path = candidates[0].path;
            lineNumber = candidates[0].line;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(candidate => candidate.normalized.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                path = exact[0].path;
                lineNumber = exact[0].line;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveSnippetInLines(string normalizedSnippet, string path,
        IReadOnlyList<PatchLine> lines, out string resolvedPath, out int resolvedLine) {
        resolvedPath = string.Empty;
        resolvedLine = 0;
        var candidates = new List<PatchLine>();
        foreach (var line in lines) {
            if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                candidates.Add(line);
            }
        }

        if (candidates.Count == 1) {
            resolvedPath = path;
            resolvedLine = candidates[0].LineNumber;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(line => line.NormalizedText.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                resolvedPath = path;
                resolvedLine = exact[0].LineNumber;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSnippetText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        var trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        var inWhitespace = false;
        foreach (var ch in trimmed) {
            if (char.IsWhiteSpace(ch)) {
                if (!inWhitespace) {
                    buffer.Append(' ');
                    inWhitespace = true;
                }
            } else {
                buffer.Append(ch);
                inWhitespace = false;
            }
        }
        return buffer.ToString();
    }

    private static string NormalizePath(string path) {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        return normalized;
    }

    private static string BuildInlineKey(string path, int line) {
        return $"{NormalizePath(path)}:{line}";
    }

    private static async Task<IssueComment?> FindExistingSummaryAsync(IReviewCodeHostReader codeHostReader, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await codeHostReader.ListIssueCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        foreach (var comment in comments) {
            if (comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                return comment;
            }
        }
        return null;
    }

    private static bool IsSummaryOutdated(IssueComment? summary, string? headSha) {
        if (summary is null || string.IsNullOrWhiteSpace(headSha)) {
            return false;
        }
        var reviewedCommit = ExtractReviewedCommit(summary.Body);
        if (string.IsNullOrWhiteSpace(reviewedCommit)) {
            return true;
        }
        return !headSha.StartsWith(reviewedCommit, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractReviewedCommit(string body) {
        return ReviewSummaryParser.TryGetReviewedCommit(body, out var commit) ? commit : null;
    }

    private static string? ExtractSummaryBody(string? body, int maxChars) {
        if (string.IsNullOrWhiteSpace(body)) {
            return null;
        }

        var lines = body.Replace("\r", "").Split('\n');
        var endIndex = lines.Length;
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].IndexOf("_Model:", StringComparison.OrdinalIgnoreCase) >= 0) {
                endIndex = i;
                break;
            }
        }

        var startIndex = 0;
        for (var i = 0; i < endIndex; i++) {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) {
                startIndex = i + 1;
                continue;
            }
            if (trimmed.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("## IntelligenceX Review", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Reviewing PR #", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(ReviewFormatter.ReviewedCommitMarker, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(">")) {
                startIndex = i + 1;
                continue;
            }
            break;
        }

        if (startIndex >= endIndex) {
            return null;
        }

        var block = string.Join("\n", lines, startIndex, endIndex - startIndex).Trim();
        if (string.IsNullOrWhiteSpace(block)) {
            return null;
        }
        if (maxChars > 0 && block.Length > maxChars) {
            return block.Substring(0, maxChars) + "...";
        }
        return block;
    }

    private static async Task<long?> CreateOrUpdateProgressCommentAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        PullRequestContext context, ReviewSettings settings, string body, CancellationToken cancellationToken) {
        IssueComment? existing = null;
        if (settings.CommentMode == ReviewCommentMode.Sticky) {
            existing = await FindExistingSummaryAsync(codeHostReader, context, settings, cancellationToken).ConfigureAwait(false);
        }

        if (existing is not null) {
            await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, body, cancellationToken)
                .ConfigureAwait(false);
            return existing.Id;
        }

        var created = await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
            .ConfigureAwait(false);
        return created.Id;
    }

    private static string? GetInput(string name) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{name.ToUpperInvariant()}");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? GetInputInt(string name) {
        var value = GetInput(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (int.TryParse(value, out var parsed) && parsed > 0) {
            return parsed;
        }
        return null;
    }

}
