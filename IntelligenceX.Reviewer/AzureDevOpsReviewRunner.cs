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

        var (limited, budgetNote) = LimitFiles(filtered, settings.MaxFiles, settings.MaxPatchChars);
        if (!settings.ReviewBudgetSummary) {
            budgetNote = string.Empty;
        }

        // Phase 2: include local git diffs where available to enable inline comments + better prompts.
        // In Azure Pipelines, sources are typically checked out already; we use the PR commit ids from the REST API.
        var withPatches = await TryAttachGitPatchesAsync(limited, pr.TargetCommit, pr.SourceCommit, settings.MaxPatchChars,
            cancellationToken).ConfigureAwait(false);

        var repoFullName = $"{pr.Project}/{pr.RepositoryName}";
        var context = new PullRequestContext(repoFullName, pr.Project, pr.RepositoryName,
            pr.PullRequestId, pr.Title, pr.Description, pr.IsDraft, pr.SourceCommit, pr.TargetCommit, Array.Empty<string>(),
            repoFullName, false, null);
        var diffNote = BuildDiffNote(iterationIds);
        var inlineSupported = true;
        var prompt = PromptBuilder.Build(context, withPatches, settings, diffNote, null, inlineSupported, null);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var runner = new ReviewRunner(settings);
        var reviewBody = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        var reviewFailed = ReviewDiagnostics.IsFailureBody(reviewBody);
        var inlineAllowed = inlineSupported && !reviewFailed && settings.MaxInlineComments > 0;
        var inlineComments = Array.Empty<InlineReviewComment>();
        var summaryBody = reviewBody;
        if (inlineAllowed) {
            var inlineResult = ReviewInlineParser.Extract(reviewBody, settings.MaxInlineComments);
            inlineComments = inlineResult.Comments as InlineReviewComment[] ?? inlineResult.Comments.ToArray();
            if (inlineResult.HadInlineSection && !string.IsNullOrWhiteSpace(inlineResult.Body)) {
                summaryBody = inlineResult.Body;
            }
        }

        if (inlineAllowed && inlineComments.Length > 0) {
            await TryPostInlineThreadsAsync(client, project, repositoryId, pr.PullRequestId, withPatches, settings,
                inlineComments, cancellationToken).ConfigureAwait(false);
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

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) LimitFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        if (maxFiles <= 0 || files.Count <= maxFiles) {
            return (files, ReviewerApp.BuildBudgetNote(files.Count, files.Count, 0, maxPatchChars));
        }
        var limited = files.Take(maxFiles).ToList();
        var note = ReviewerApp.BuildBudgetNote(files.Count, limited.Count, 0, maxPatchChars);
        return (limited, note);
    }

    private static async Task<IReadOnlyList<PullRequestFile>> TryAttachGitPatchesAsync(IReadOnlyList<PullRequestFile> files,
        string? baseSha, string? headSha, int maxPatchChars, CancellationToken cancellationToken) {
        if (files.Count == 0) {
            return files;
        }
        if (string.IsNullOrWhiteSpace(baseSha) || string.IsNullOrWhiteSpace(headSha)) {
            return files;
        }

        var list = new List<PullRequestFile>(files.Count);
        var truncated = 0;
        foreach (var file in files) {
            var patch = await TryGetGitPatchAsync(baseSha!, headSha!, file.Filename, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(patch) && maxPatchChars > 0 && patch.Length > maxPatchChars) {
                patch = patch.Substring(0, maxPatchChars) + "\n... (truncated) ...\n";
                truncated++;
            }
            list.Add(new PullRequestFile(file.Filename, file.Status, patch));
        }
        if (truncated > 0 && maxPatchChars > 0) {
            Console.WriteLine($"Note: trimmed {truncated} patch(es) to {maxPatchChars} chars for Azure DevOps review prompt.");
        }
        return list;
    }

    private static async Task<string?> TryGetGitPatchAsync(string baseSha, string headSha, string path, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        // Use local repo checkout to avoid REST API diff limitations for ADO.
        // We intentionally scope to a single file to keep memory bounded.
        var psi = new ProcessStartInfo {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("diff");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add(baseSha);
        psi.ArgumentList.Add(headSha);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(path);

        try {
            using var proc = Process.Start(psi);
            if (proc is null) {
                return null;
            }
            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (proc.ExitCode != 0) {
                if (!string.IsNullOrWhiteSpace(stderr)) {
                    Console.Error.WriteLine($"git diff failed for {path}: {stderr.Trim()}");
                }
                return null;
            }
            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.TrimEnd();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Console.Error.WriteLine($"git diff failed for {path}: {ex.Message}");
            return null;
        }
    }

    private static async Task TryPostInlineThreadsAsync(AzureDevOpsClient client, string project, string repositoryId,
        int pullRequestId, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        IReadOnlyList<InlineReviewComment> inlineComments, CancellationToken cancellationToken) {
        var lineMap = BuildInlineLineMap(files);
        if (lineMap.Count == 0) {
            Console.WriteLine("No diff hunks available for Azure DevOps inline comments; skipping inline thread posting.");
            return;
        }

        var posted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inline in inlineComments) {
            if (posted >= settings.MaxInlineComments) {
                break;
            }

            var normalizedPath = NormalizePath(inline.Path);
            var line = inline.Line;
            if (string.IsNullOrWhiteSpace(normalizedPath) || line <= 0) {
                continue;
            }
            if (!lineMap.TryGetValue(normalizedPath, out var allowedLines) || !allowedLines.Contains(line)) {
                continue;
            }
            var key = $"{normalizedPath}:{line}";
            if (!seen.Add(key)) {
                continue;
            }
            var body = inline.Body?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }

            var content = $"{ReviewFormatter.InlineMarker}\n{body}";
            try {
                await client.CreatePullRequestInlineThreadAsync(project, repositoryId, pullRequestId, normalizedPath, line,
                    content, cancellationToken).ConfigureAwait(false);
                posted++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Azure DevOps inline thread failed for {normalizedPath}:{line} - {ex.Message}");
            }
        }
        if (posted > 0) {
            Console.WriteLine($"Posted Azure DevOps inline threads: {posted}");
        }
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
        var lines = patch.Replace("\r", "").Split('\n');
        var newLine = 0;
        foreach (var line in lines) {
            var match = PatchHunkHeader.Match(line);
            if (match.Success) {
                _ = int.TryParse(match.Groups[2].Value, out newLine);
                continue;
            }
            if (newLine <= 0) {
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                allowed.Add(newLine);
                newLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                newLine++;
            }
        }
        return allowed;
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
