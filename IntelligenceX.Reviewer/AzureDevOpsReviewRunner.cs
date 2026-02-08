using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal static class AzureDevOpsReviewRunner {
    private static readonly Regex PatchHunkHeader =
        new Regex(@"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    public static async Task<int> RunAsync(ReviewSettings settings, CancellationToken cancellationToken) {
        var options = ResolveOptions(settings);
        if (!options.Success || options.BaseUri is null || options.Project is null || options.Token is null) {
            Console.Error.WriteLine(options.Error ?? "Azure DevOps configuration is incomplete.");
            return 1;
        }

        if (settings.TriageOnly) {
            Console.Error.WriteLine("Triage-only mode is not yet supported for Azure DevOps.");
            return 1;
        }
        if (settings.ProgressUpdates) {
            Console.WriteLine("Progress updates are not supported for Azure DevOps; skipping.");
        }

        var baseUri = options.BaseUri;
        var project = options.Project;
        var token = options.Token;

        using var client = new AzureDevOpsClient(baseUri, token, options.AuthScheme);
        var pr = await client.GetPullRequestAsync(project, options.PullRequestId, cancellationToken).ConfigureAwait(false);

        if (settings.SkipDraft && pr.IsDraft) {
            Console.WriteLine("Skipping draft pull request.");
            return 0;
        }

        var repositoryId = !string.IsNullOrWhiteSpace(settings.AzureRepository)
            ? settings.AzureRepository!
            : pr.RepositoryId;
        if (string.IsNullOrWhiteSpace(repositoryId)) {
            Console.Error.WriteLine("Azure DevOps repository id is missing. Set review.azureRepo or ensure the PR payload includes repository info.");
            return 1;
        }

        var iterationIds = await client.GetIterationIdsAsync(project, repositoryId, pr.PullRequestId, cancellationToken)
            .ConfigureAwait(false);
        var files = await client.GetPullRequestChangesAsync(project, repositoryId, pr.PullRequestId, cancellationToken)
            .ConfigureAwait(false);
        if (files.Count == 0) {
            Console.WriteLine("No files to review.");
            return 0;
        }

        if (ShouldSkipByPaths(files, settings.SkipPaths)) {
            Console.WriteLine("Skipping pull request due to path filter.");
            return 0;
        }

        var filtered = ReviewerApp.FilterFilesByPaths(files, settings.IncludePaths, settings.ExcludePaths,
            settings.SkipBinaryFiles, settings.SkipGeneratedFiles, settings.GeneratedFileGlobs);
        if (filtered.Count == 0) {
            Console.WriteLine("No files matched include/exclude filters.");
            return 0;
        }

        var totalFiles = filtered.Count;
        var limited = LimitFiles(filtered, settings.MaxFiles);

        // Phase 2: include local git diffs where available to enable inline comments + better prompts.
        // In Azure Pipelines, sources are typically checked out already; we use the PR commit ids from the REST API.
        var patchResult = await TryAttachGitPatchesAsync(limited, pr.TargetCommit, pr.SourceCommit, settings.MaxPatchChars,
            cancellationToken).ConfigureAwait(false);
        var withPatches = patchResult.PromptFiles;
        var budgetNote = settings.ReviewBudgetSummary
            ? ReviewerApp.BuildBudgetNote(totalFiles, limited.Count, patchResult.TruncatedPatches, settings.MaxPatchChars)
            : string.Empty;

        var repoFullName = $"{pr.Project}/{pr.RepositoryName}";
        var context = new PullRequestContext(repoFullName, pr.Project, pr.RepositoryName,
            pr.PullRequestId, pr.Title, pr.Description, pr.IsDraft, pr.SourceCommit, pr.TargetCommit, Array.Empty<string>(),
            repoFullName, false, null);
        var diffNote = BuildDiffNote(iterationIds);
        var inlineSupported = !string.Equals(settings.Mode, "summary", StringComparison.OrdinalIgnoreCase) &&
                              settings.MaxInlineComments > 0 &&
                              !string.IsNullOrWhiteSpace(pr.SourceCommit);
        var prompt = PromptBuilder.Build(context, withPatches, settings, diffNote, null, inlineSupported, null);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var runner = new ReviewRunner(settings);
        var reviewBody = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        var reviewFailed = ReviewDiagnostics.IsFailureBody(reviewBody);
        var inlineAllowed = inlineSupported && !reviewFailed;
        var inlineComments = Array.Empty<InlineReviewComment>();
        InlineSectionResult? inlineResult = null;
        if (inlineAllowed) {
            inlineResult = ReviewInlineParser.Extract(reviewBody, settings.MaxInlineComments);
            inlineComments = inlineResult.Comments as InlineReviewComment[] ?? inlineResult.Comments.ToArray();
        }

        InlinePostResult? inlinePost = null;
        if (inlineAllowed && inlineComments.Length > 0) {
            inlinePost = await TryPostInlineThreadsAsync(client, project, repositoryId, pr.PullRequestId,
                patchResult.InlineFiles, settings, inlineComments, cancellationToken).ConfigureAwait(false);
        }

        var summaryBody = reviewBody;
        if (inlineResult is not null &&
            inlineResult.HadInlineSection &&
            !string.IsNullOrWhiteSpace(inlineResult.Body) &&
            (inlinePost?.HandledInline ?? false)) {
            // Only strip the inline section once we've successfully posted (or observed existing) inline threads.
            summaryBody = inlineResult.Body;
        }

        var inlineSuppressed = inlineSupported && !inlineAllowed;
        var findingsBlock = settings.StructuredFindings ? ReviewFindingsBuilder.Build(inlineComments) : string.Empty;
        var commentBody = ReviewFormatter.BuildComment(context, summaryBody, settings, inlineSupported,
            inlineSuppressed, autoResolveNote: string.Empty, budgetNote, usageLine: string.Empty, findingsBlock);

        await client.CreatePullRequestThreadAsync(project, repositoryId, pr.PullRequestId, commentBody, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine("Posted Azure DevOps review comment.");
        return 0;
    }

    private static bool ShouldSkipByPaths(IReadOnlyList<PullRequestFile> files, IReadOnlyList<string> skipPaths) {
        if (files.Count == 0 || skipPaths.Count == 0) {
            return false;
        }
        foreach (var file in files) {
            var matches = skipPaths.Any(pattern => GlobMatcher.IsMatch(pattern, file.Filename));
            if (!matches) {
                return false;
            }
        }
        return true;
    }

    private static IReadOnlyList<PullRequestFile> LimitFiles(IReadOnlyList<PullRequestFile> files, int maxFiles) {
        if (maxFiles <= 0 || files.Count <= maxFiles) {
            return files;
        }
        return files.Take(maxFiles).ToList();
    }

    private const string PatchTruncationMessage = "\n... (truncated) ...\n";

    private sealed record PatchAttachResult(IReadOnlyList<PullRequestFile> PromptFiles, IReadOnlyList<PullRequestFile> InlineFiles,
        int TruncatedPatches);

    private static async Task<PatchAttachResult> TryAttachGitPatchesAsync(IReadOnlyList<PullRequestFile> files,
        string? baseSha, string? headSha, int maxPatchChars, CancellationToken cancellationToken) {
        if (files.Count == 0) {
            return new PatchAttachResult(files, files, 0);
        }
        if (string.IsNullOrWhiteSpace(baseSha) || string.IsNullOrWhiteSpace(headSha)) {
            return new PatchAttachResult(files, files, 0);
        }

        var promptFiles = new List<PullRequestFile>(files.Count);
        var inlineFiles = new List<PullRequestFile>(files.Count);
        var truncated = 0;
        foreach (var file in files) {
            var patch = await TryGetGitPatchAsync(baseSha!, headSha!, file.Filename, cancellationToken).ConfigureAwait(false);
            inlineFiles.Add(new PullRequestFile(file.Filename, file.Status, patch));

            var promptPatch = patch;
            if (!string.IsNullOrWhiteSpace(promptPatch) && maxPatchChars > 0 && promptPatch.Length > maxPatchChars) {
                promptPatch = promptPatch.Substring(0, maxPatchChars) + PatchTruncationMessage;
                truncated++;
            }
            promptFiles.Add(new PullRequestFile(file.Filename, file.Status, promptPatch));
        }
        if (truncated > 0 && maxPatchChars > 0) {
            Console.WriteLine($"Note: trimmed {truncated} patch(es) to {maxPatchChars} chars for Azure DevOps review prompt.");
        }
        return new PatchAttachResult(promptFiles, inlineFiles, truncated);
    }

    private static async Task<string?> TryGetGitPatchAsync(string baseSha, string headSha, string path, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        // Use local repo checkout to avoid REST API diff limitations for ADO.
        // We intentionally scope to a single file to keep memory bounded.
        try {
            // Prefer merge-base diff to align with PR semantics. Fall back to a direct base/head diff if needed.
            var mergeBaseRange = $"{baseSha}...{headSha}";
            var stdout = await TryRunGitDiffAsync(mergeBaseRange, path, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stdout)) {
                return stdout.TrimEnd();
            }

            var directRange = $"{baseSha}..{headSha}";
            stdout = await TryRunGitDiffAsync(directRange, path, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.TrimEnd();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            Console.Error.WriteLine($"git diff failed for {path}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> TryRunGitDiffAsync(string range, string path, CancellationToken cancellationToken) {
        var psi = new ProcessStartInfo {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("diff");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(range);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(path);

        using var proc = Process.Start(psi);
        if (proc is null) {
            return null;
        }

        // Read both streams concurrently to avoid deadlocks with redirected output buffers.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask, proc.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (proc.ExitCode != 0) {
            if (!string.IsNullOrWhiteSpace(stderr)) {
                Console.Error.WriteLine($"git diff failed for {path}: {stderr.Trim()}");
            }
            return null;
        }
        return string.IsNullOrWhiteSpace(stdout) ? null : stdout;
    }

    private sealed record InlinePostResult(int Posted, int AlreadyPresent) {
        public bool HandledInline => Posted > 0 || AlreadyPresent > 0;
    }

    private static async Task<InlinePostResult> TryPostInlineThreadsAsync(AzureDevOpsClient client, string project, string repositoryId,
        int pullRequestId, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        IReadOnlyList<InlineReviewComment> inlineComments, CancellationToken cancellationToken) {
        var lineMap = BuildInlineLineMap(files);
        if (lineMap.Count == 0) {
            Console.WriteLine("No diff hunks available for Azure DevOps inline comments; skipping inline thread posting.");
            return new InlinePostResult(0, 0);
        }

        var patchIndex = BuildInlinePatchIndex(files);
        var posted = 0;
        var alreadyPresent = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = await ListExistingInlineThreadsAsync(client, project, repositoryId, pullRequestId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var inline in inlineComments) {
            if (posted >= settings.MaxInlineComments) {
                break;
            }

            var normalizedPath = NormalizePath(inline.Path);
            var line = inline.Line;
            if ((string.IsNullOrWhiteSpace(normalizedPath) || line <= 0) &&
                !string.IsNullOrWhiteSpace(inline.Snippet) &&
                TryResolveSnippet(inline.Snippet!, patchIndex, normalizedPath, out var resolvedPath, out var resolvedLine)) {
                normalizedPath = resolvedPath;
                line = resolvedLine;
            }
            if (string.IsNullOrWhiteSpace(normalizedPath) || line <= 0) {
                continue;
            }
            if (!lineMap.TryGetValue(normalizedPath, out var allowedLines) || !allowedLines.Contains(line)) {
                continue;
            }
            var key = $"{normalizedPath}:{line}";
            if (existing.Contains(key)) {
                alreadyPresent++;
                continue;
            }
            if (!seen.Add(key)) {
                continue;
            }
            var body = inline.Body?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }

            var content = $"{ReviewFormatter.InlineMarker}\nFile: {normalizedPath}\nLine: {line}\n\n{body}";
            try {
                await client.CreatePullRequestInlineThreadAsync(project, repositoryId, pullRequestId, normalizedPath, line,
                    content, cancellationToken).ConfigureAwait(false);
                posted++;
            } catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                Console.Error.WriteLine($"Azure DevOps inline thread failed for {normalizedPath}:{line} - {ex.Message}");
            }
        }
        if (posted > 0) {
            Console.WriteLine($"Posted Azure DevOps inline threads: {posted}");
        }
        return new InlinePostResult(posted, alreadyPresent);
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

    private static HashSet<int> ParsePatchLines(string patch) {
        var allowed = new HashSet<int>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = PatchHunkHeader.Match(line);
                if (match.Success) {
                    _ = int.TryParse(match.Groups[1].Value, out oldLine);
                    _ = int.TryParse(match.Groups[2].Value, out newLine);
                    oldLine = Math.Max(0, oldLine - 1);
                    newLine = Math.Max(0, newLine - 1);
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

    private static List<PatchLine> ParsePatchContent(string patch) {
        var results = new List<PatchLine>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = PatchHunkHeader.Match(line);
                if (match.Success) {
                    _ = int.TryParse(match.Groups[1].Value, out oldLine);
                    _ = int.TryParse(match.Groups[2].Value, out newLine);
                    oldLine = Math.Max(0, oldLine - 1);
                    newLine = Math.Max(0, newLine - 1);
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

    private static async Task<HashSet<string>> ListExistingInlineThreadsAsync(AzureDevOpsClient client, string project,
        string repositoryId, int pullRequestId, CancellationToken cancellationToken) {
        try {
            var threads = await client.ListPullRequestThreadsAsync(project, repositoryId, pullRequestId, cancellationToken)
                .ConfigureAwait(false);
            if (threads.Count == 0) {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var thread in threads) {
                if (thread.Line is null || thread.Line.Value <= 0) {
                    continue;
                }
                var path = NormalizePath(thread.FilePath);
                if (string.IsNullOrWhiteSpace(path)) {
                    continue;
                }
                var hasInlineMarker = thread.Comments.Any(comment =>
                    comment.Contains(ReviewFormatter.InlineMarker, StringComparison.Ordinal));
                if (!hasInlineMarker) {
                    continue;
                }
                keys.Add($"{path}:{thread.Line.Value}");
            }
            return keys;
        } catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            Console.Error.WriteLine($"Azure DevOps: failed to list existing threads for inline dedupe: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string NormalizePath(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        return value.Replace('\\', '/').TrimStart('/');
    }

    internal static string BuildDiffNote(IReadOnlyList<int> iterationIds) {
        if (iterationIds.Count == 0) {
            return "pull request changes";
        }
        var minIterationId = iterationIds.Min();
        var maxIterationId = iterationIds.Max();
        return iterationIds.Count == 1
            ? $"iteration {maxIterationId}"
            : $"iterations {minIterationId}-{maxIterationId} (count {iterationIds.Count})";
    }

    private static AzureDevOpsOptions ResolveOptions(ReviewSettings settings) {
        var baseUrl = settings.AzureBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            baseUrl = Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI");
        }
        if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(settings.AzureOrganization)) {
            baseUrl = $"https://dev.azure.com/{settings.AzureOrganization}";
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) {
            return AzureDevOpsOptions.Fail("Azure DevOps base URL is missing or invalid. Set review.azureBaseUrl or SYSTEM_COLLECTIONURI.");
        }

        var project = settings.AzureProject ?? Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT");
        if (string.IsNullOrWhiteSpace(project)) {
            return AzureDevOpsOptions.Fail("Azure DevOps project is missing. Set review.azureProject or SYSTEM_TEAMPROJECT.");
        }

        var pullRequestId = ResolvePullRequestId();
        if (!pullRequestId.HasValue) {
            return AzureDevOpsOptions.Fail("Azure DevOps pull request id is missing. Set SYSTEM_PULLREQUEST_PULLREQUESTID.");
        }

        var tokenEnv = settings.AzureTokenEnv;
        if (string.IsNullOrWhiteSpace(tokenEnv)) {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN"))) {
                tokenEnv = "SYSTEM_ACCESSTOKEN";
            } else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_DEVOPS_TOKEN"))) {
                tokenEnv = "AZURE_DEVOPS_TOKEN";
            }
        }
        if (string.IsNullOrWhiteSpace(tokenEnv)) {
            return AzureDevOpsOptions.Fail("Azure DevOps token env is missing. Set review.azureTokenEnv or SYSTEM_ACCESSTOKEN.");
        }
        var token = Environment.GetEnvironmentVariable(tokenEnv);
        if (string.IsNullOrWhiteSpace(token)) {
            return AzureDevOpsOptions.Fail($"Azure DevOps token is empty. Environment variable '{tokenEnv}' is not set.");
        }

        var scheme = ResolveAuthScheme(settings, tokenEnv, token);
        SecretsAudit.Record($"Azure DevOps token from {tokenEnv} ({scheme.ToString().ToLowerInvariant()})");
        return AzureDevOpsOptions.Ok(baseUri, project, pullRequestId.Value, token, scheme);
    }

    private static AzureDevOpsAuthScheme ResolveAuthScheme(ReviewSettings settings, string tokenEnv, string token) {
        if (settings.AzureAuthSchemeSpecified) {
            return settings.AzureAuthScheme;
        }
        if (string.Equals(tokenEnv, "SYSTEM_ACCESSTOKEN", StringComparison.OrdinalIgnoreCase)) {
            return AzureDevOpsAuthScheme.Bearer;
        }
        return LooksLikeJwt(token) ? AzureDevOpsAuthScheme.Bearer : AzureDevOpsAuthScheme.Basic;
    }

    private static bool LooksLikeJwt(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return false;
        }
        // Heuristic: most JWTs have at least two dots; users can override via azureAuthScheme.
        var dotCount = token.Count(ch => ch == '.');
        return dotCount >= 2;
    }

    private static int? ResolvePullRequestId() {
        var raw = Environment.GetEnvironmentVariable("SYSTEM_PULLREQUEST_PULLREQUESTID")
                  ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_PR_ID")
                  ?? Environment.GetEnvironmentVariable("PULL_REQUEST_ID");
        if (int.TryParse(raw, out var parsed) && parsed > 0) {
            return parsed;
        }
        return null;
    }

    private sealed record AzureDevOpsOptions(bool Success, Uri? BaseUri, string? Project, int PullRequestId,
        string? Token, AzureDevOpsAuthScheme AuthScheme, string? Error) {
        public static AzureDevOpsOptions Ok(Uri baseUri, string project, int pullRequestId, string token,
            AzureDevOpsAuthScheme authScheme)
            => new(true, baseUri, project, pullRequestId, token, authScheme, null);

        public static AzureDevOpsOptions Fail(string error)
            => new(false, null, null, 0, null, AzureDevOpsAuthScheme.Bearer, error);
    }
}
