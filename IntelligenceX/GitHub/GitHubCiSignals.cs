using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.GitHub;

internal sealed class GitHubCheckRunInfo {
    public GitHubCheckRunInfo(string name, string status, string? conclusion, string? detailsUrl) {
        Name = name;
        Status = status;
        Conclusion = conclusion;
        DetailsUrl = detailsUrl;
    }

    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
    public string? DetailsUrl { get; }
}

internal sealed class GitHubCheckSnapshot {
    public GitHubCheckSnapshot(int passedCount, int failedCount, int pendingCount, IReadOnlyList<GitHubCheckRunInfo> failedChecks) {
        PassedCount = passedCount;
        FailedCount = failedCount;
        PendingCount = pendingCount;
        FailedChecks = failedChecks;
    }

    public int PassedCount { get; }
    public int FailedCount { get; }
    public int PendingCount { get; }
    public IReadOnlyList<GitHubCheckRunInfo> FailedChecks { get; }
    public bool HasData => PassedCount > 0 || FailedCount > 0 || PendingCount > 0 || FailedChecks.Count > 0;
}

internal sealed class GitHubWorkflowRunInfo {
    public GitHubWorkflowRunInfo(string? runId, string name, string status, string? conclusion, string? url) {
        RunId = runId;
        Name = name;
        Status = status;
        Conclusion = conclusion;
        Url = url;
    }

    public string? RunId { get; }
    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
    public string? Url { get; }
}

internal sealed class GitHubWorkflowStepInfo {
    public GitHubWorkflowStepInfo(string name, string status, string? conclusion) {
        Name = name;
        Status = status;
        Conclusion = conclusion;
    }

    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
}

internal sealed class GitHubWorkflowJobInfo {
    public GitHubWorkflowJobInfo(string name, string status, string? conclusion, IReadOnlyList<GitHubWorkflowStepInfo> steps) {
        Name = name;
        Status = status;
        Conclusion = conclusion;
        Steps = steps;
    }

    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
    public IReadOnlyList<GitHubWorkflowStepInfo> Steps { get; }
}

internal enum GitHubWorkflowFailureKind {
    Unknown,
    Actionable,
    Operational,
    Mixed
}

internal sealed class GitHubWorkflowFailureEvidence {
    public GitHubWorkflowFailureEvidence(GitHubWorkflowFailureKind kind, string summary) {
        Kind = kind;
        Summary = summary;
    }

    public GitHubWorkflowFailureKind Kind { get; }
    public string Summary { get; }
    public bool HasData => !string.IsNullOrWhiteSpace(Summary);
}

internal static class GitHubCiSignals {
    private static readonly HashSet<string> FailedConclusions = new(StringComparer.OrdinalIgnoreCase) {
        "failure",
        "timed_out",
        "cancelled",
        "action_required",
        "startup_failure",
        "stale"
    };

    private static readonly HashSet<string> PassedConclusions = new(StringComparer.OrdinalIgnoreCase) {
        "success",
        "neutral",
        "skipped"
    };
    private static readonly string[] OperationalFailureMarkers = {
        "set up job",
        "setup job",
        "complete job",
        "post job cleanup",
        "initialize containers",
        "initialize job",
        "prepare workflow directory",
        "prepare all required actions",
        "request a runner",
        "set up runner",
        "checkout",
        "cache",
        "download artifact",
        "upload artifact",
        "download action repository",
        "download immutable action package",
        "post checkout",
        "post cache",
        "post run",
        "set up",
        "setup ",
        "install ",
        "login",
        "authenticate"
    };

    internal static IReadOnlyList<GitHubCheckRunInfo> ParseCheckRuns(JsonObject? root) {
        var array = root?.GetArray("check_runs");
        if (array is null || array.Count == 0) {
            return Array.Empty<GitHubCheckRunInfo>();
        }

        var items = new List<GitHubCheckRunInfo>(array.Count);
        foreach (var item in array) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            items.Add(new GitHubCheckRunInfo(
                obj.GetString("name") ?? "unnamed-check",
                obj.GetString("status") ?? string.Empty,
                obj.GetString("conclusion"),
                obj.GetString("details_url") ?? obj.GetString("html_url")));
        }

        return items;
    }

    internal static GitHubCheckSnapshot SummarizeCheckRuns(IEnumerable<GitHubCheckRunInfo>? checkRuns) {
        if (checkRuns is null) {
            return new GitHubCheckSnapshot(0, 0, 0, Array.Empty<GitHubCheckRunInfo>());
        }

        var passed = 0;
        var failed = 0;
        var pending = 0;
        var failedChecks = new List<GitHubCheckRunInfo>();

        foreach (var check in checkRuns) {
            if (!string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase)) {
                pending++;
                continue;
            }

            if (IsFailedConclusion(check.Conclusion)) {
                failed++;
                failedChecks.Add(check);
                continue;
            }

            if (IsPassedConclusion(check.Conclusion)) {
                passed++;
            }
        }

        failedChecks = failedChecks
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Conclusion ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GitHubCheckSnapshot(passed, failed, pending, failedChecks);
    }

    internal static IReadOnlyList<GitHubWorkflowRunInfo> ParseFailedWorkflowRuns(JsonObject? root, string? headSha, int maxResults) {
        if (string.IsNullOrWhiteSpace(headSha) || maxResults <= 0) {
            return Array.Empty<GitHubWorkflowRunInfo>();
        }

        var runs = root?.GetArray("workflow_runs");
        if (runs is null || runs.Count == 0) {
            return Array.Empty<GitHubWorkflowRunInfo>();
        }

        var failedRuns = new List<GitHubWorkflowRunInfo>();
        foreach (var run in runs) {
            if (failedRuns.Count >= maxResults) {
                break;
            }

            var obj = run.AsObject();
            if (obj is null) {
                continue;
            }

            var runHeadSha = obj.GetString("head_sha");
            if (!string.Equals(runHeadSha, headSha, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var conclusion = obj.GetString("conclusion");
            if (!IsFailedConclusion(conclusion)) {
                continue;
            }

            failedRuns.Add(new GitHubWorkflowRunInfo(
                obj.GetInt64("id")?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                obj.GetString("name") ?? "unnamed-workflow",
                obj.GetString("status") ?? string.Empty,
                conclusion,
                obj.GetString("html_url")));
        }

        return failedRuns
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RunId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<GitHubWorkflowJobInfo> ParseWorkflowJobs(JsonObject? root) {
        var runs = root?.GetArray("jobs") ?? root?.GetArray("workflow_jobs");
        if (runs is null || runs.Count == 0) {
            return Array.Empty<GitHubWorkflowJobInfo>();
        }

        var jobs = new List<GitHubWorkflowJobInfo>(runs.Count);
        foreach (var run in runs) {
            var obj = run.AsObject();
            if (obj is null) {
                continue;
            }

            var steps = new List<GitHubWorkflowStepInfo>();
            var stepArray = obj.GetArray("steps");
            if (stepArray is not null) {
                foreach (var step in stepArray) {
                    var stepObj = step.AsObject();
                    if (stepObj is null) {
                        continue;
                    }

                    steps.Add(new GitHubWorkflowStepInfo(
                        stepObj.GetString("name") ?? "unnamed-step",
                        stepObj.GetString("status") ?? string.Empty,
                        stepObj.GetString("conclusion")));
                }
            }

            jobs.Add(new GitHubWorkflowJobInfo(
                obj.GetString("name") ?? "unnamed-job",
                obj.GetString("status") ?? string.Empty,
                obj.GetString("conclusion"),
                steps));
        }

        return jobs
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static GitHubWorkflowFailureEvidence SummarizeWorkflowFailureEvidence(IEnumerable<GitHubWorkflowJobInfo>? jobs, int maxChars) {
        if (jobs is null || maxChars <= 0) {
            return new GitHubWorkflowFailureEvidence(GitHubWorkflowFailureKind.Unknown, string.Empty);
        }

        var failedJobs = jobs
            .Where(HasFailedWorkflowJob)
            .ToList();
        if (failedJobs.Count == 0) {
            return new GitHubWorkflowFailureEvidence(GitHubWorkflowFailureKind.Unknown, string.Empty);
        }

        var operational = 0;
        var actionable = 0;
        var summaries = new List<string>();
        foreach (var job in failedJobs) {
            var summary = BuildFailedJobSummary(job);
            if (string.IsNullOrWhiteSpace(summary)) {
                continue;
            }

            summaries.Add(summary);
            if (LooksOperationalFailure(job)) {
                operational++;
            } else {
                actionable++;
            }
        }

        if (summaries.Count == 0) {
            return new GitHubWorkflowFailureEvidence(GitHubWorkflowFailureKind.Unknown, string.Empty);
        }

        var kind =
            operational > 0 && actionable > 0 ? GitHubWorkflowFailureKind.Mixed :
            operational > 0 ? GitHubWorkflowFailureKind.Operational :
            actionable > 0 ? GitHubWorkflowFailureKind.Actionable :
            GitHubWorkflowFailureKind.Unknown;

        return new GitHubWorkflowFailureEvidence(kind, JoinWithBudget(summaries, maxChars));
    }

    internal static bool IsFailedConclusion(string? conclusion) {
        var normalized = conclusion?.Trim();
        if (normalized is null || normalized.Length == 0) {
            return false;
        }
        return FailedConclusions.Contains(normalized);
    }

    internal static bool IsPassedConclusion(string? conclusion) {
        var normalized = conclusion?.Trim();
        if (normalized is null || normalized.Length == 0) {
            return false;
        }
        return PassedConclusions.Contains(normalized);
    }

    internal static bool IsPotentiallyOperationalConclusion(string? conclusion) {
        var normalized = conclusion?.Trim();
        if (normalized is null || normalized.Length == 0) {
            return false;
        }

        return normalized.ToLowerInvariant() switch {
            "cancelled" or "timed_out" or "startup_failure" => true,
            _ => false
        };
    }

    private static bool HasFailedWorkflowJob(GitHubWorkflowJobInfo job) {
        if (IsFailedConclusion(job.Conclusion)) {
            return true;
        }

        return job.Steps.Any(step => IsFailedConclusion(step.Conclusion));
    }

    private static bool LooksOperationalFailure(GitHubWorkflowJobInfo job) {
        if (IsPotentiallyOperationalConclusion(job.Conclusion) || LooksOperationalName(job.Name)) {
            return true;
        }

        var failedSteps = job.Steps.Where(step => IsFailedConclusion(step.Conclusion)).ToList();
        if (failedSteps.Count == 0) {
            return false;
        }

        return failedSteps.All(step => IsPotentiallyOperationalConclusion(step.Conclusion) || LooksOperationalName(step.Name));
    }

    private static bool LooksOperationalName(string? value) {
        var normalized = value?.Trim();
        if (normalized is null || normalized.Length == 0) {
            return false;
        }

        foreach (var marker in OperationalFailureMarkers) {
            if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string BuildFailedJobSummary(GitHubWorkflowJobInfo job) {
        var failedSteps = job.Steps
            .Where(step => IsFailedConclusion(step.Conclusion))
            .Take(2)
            .Select(step => $"{step.Name} ({step.Conclusion ?? step.Status})")
            .ToList();
        if (failedSteps.Count > 0) {
            return $"job {job.Name}: failed step{(failedSteps.Count == 1 ? string.Empty : "s")} {string.Join("; ", failedSteps)}";
        }

        return $"job {job.Name} ({job.Conclusion ?? job.Status})";
    }

    private static string JoinWithBudget(IReadOnlyList<string> items, int maxChars) {
        if (items.Count == 0 || maxChars <= 0) {
            return string.Empty;
        }

        var normalized = items
            .Select(NormalizeSummaryText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        if (normalized.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in normalized) {
            var separator = sb.Length == 0 ? string.Empty : "; ";
            if (sb.Length + separator.Length + item.Length <= maxChars) {
                sb.Append(separator);
                sb.Append(item);
                continue;
            }

            var remaining = maxChars - sb.Length - separator.Length;
            if (remaining <= 0) {
                break;
            }

            sb.Append(separator);
            if (remaining <= 3) {
                sb.Append(new string('.', remaining));
            } else {
                sb.Append(item.Substring(0, remaining - 3));
                sb.Append("...");
            }
            break;
        }

        return sb.ToString();
    }

    private static string NormalizeSummaryText(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return string.Join(" ", value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
