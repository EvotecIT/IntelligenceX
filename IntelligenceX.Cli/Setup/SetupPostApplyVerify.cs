using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup;

internal static class SetupPostApplyVerifier {
    private const string WorkflowPath = ".github/workflows/review-intelligencex.yml";
    private const string ConfigPath = ".intelligencex/reviewer.json";
    private const string LegacyConfigPath = ".intelligencex/config.json";
    private const string ManagedWorkflowMarker = "INTELLIGENCEX:BEGIN";

    public static string? ExtractPullRequestUrl(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
            var marker = "PR created:";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                continue;
            }
            var url = line[(idx + marker.Length)..].Trim();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
        return null;
    }

    public static SetupPostApplyVerification EvaluateForTests(SetupPostApplyContext context, SetupPostApplyObservedState observed) {
        return Evaluate(context, observed);
    }

    public static SetupPostApplyVerification CreateFailedApplySkipped(SetupPostApplyContext context) {
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        return new SetupPostApplyVerification {
            Repo = context.Repo,
            Operation = DescribeOperation(context.Operation),
            PullRequestUrl = context.PullRequestUrl,
            Skipped = true,
            Passed = false,
            Note = "Setup command failed; verification was skipped."
        };
    }

    public static async Task<SetupPostApplyVerification> VerifyAsync(GitHubRepoClient? client, SetupPostApplyContext context) {
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        if (!context.ExitSuccess) {
            return CreateFailedApplySkipped(context);
        }

        if (context.DryRun) {
            return new SetupPostApplyVerification {
                Repo = context.Repo,
                Operation = DescribeOperation(context.Operation),
                PullRequestUrl = context.PullRequestUrl,
                Skipped = true,
                Passed = true,
                Note = "Dry run; no post-apply verification required."
            };
        }

        if (client is null) {
            var withoutClient = Evaluate(context, new SetupPostApplyObservedState());
            withoutClient.Skipped = false;
            withoutClient.Note = "Verification used command output only (no GitHub token available for repository checks).";
            return withoutClient;
        }

        if (!TryParseRepo(context.Repo, out var owner, out var repo)) {
            return new SetupPostApplyVerification {
                Repo = context.Repo,
                Operation = DescribeOperation(context.Operation),
                PullRequestUrl = context.PullRequestUrl,
                Skipped = false,
                Passed = false,
                Note = "Invalid repository name (expected owner/name)."
            };
        }

        var observed = await CaptureObservedStateAsync(client, context, owner, repo).ConfigureAwait(false);
        return Evaluate(context, observed);
    }

    private static async Task<SetupPostApplyObservedState> CaptureObservedStateAsync(
        GitHubRepoClient client,
        SetupPostApplyContext context,
        string owner,
        string repo) {
        var observed = new SetupPostApplyObservedState();

        var needsFileChecks = context.Operation == SetupApplyOperation.Setup || context.Operation == SetupApplyOperation.Cleanup;
        var needsRepoSecretCheck = ShouldCheckRepoSecret(context);
        var needsOrgSecretCheck = ShouldCheckOrgSecret(context);

        if (needsFileChecks) {
            string? defaultBranch = null;
            try {
                defaultBranch = await client.GetDefaultBranchAsync(owner, repo).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (HttpRequestException ex) {
                Trace.TraceWarning($"GitHub default branch lookup HTTP failure for {owner}/{repo}: {ex.Message}");
            } catch (JsonException ex) {
                Trace.TraceWarning($"GitHub default branch lookup JSON parse failure for {owner}/{repo}: {ex.Message}");
            } catch (InvalidOperationException ex) {
                Trace.TraceWarning($"GitHub default branch lookup failed for {owner}/{repo}: {ex.GetType().Name}: {ex.Message}");
            }

            observed.DefaultBranch = defaultBranch;
            if (TryExtractPullRequestNumber(context.PullRequestUrl, out var prNumber)) {
                var prInfo = await client.TryGetPullRequestAsync(owner, repo, prNumber).ConfigureAwait(false);
                if (prInfo is not null && !string.IsNullOrWhiteSpace(prInfo.HeadRef)) {
                    observed.CheckRef = prInfo.HeadRef;
                    observed.CheckRefSource = "pull-request";
                } else {
                    // Avoid false negatives on setup/cleanup PR flows when PR metadata can't be read.
                    observed.CheckRef = null;
                    observed.CheckRefSource = "pull-request";
                }
            } else {
                observed.CheckRef = defaultBranch;
                observed.CheckRefSource = "default-branch";
            }

            if (!string.IsNullOrWhiteSpace(observed.CheckRef)) {
                var workflow = await client.TryGetFileAsync(owner, repo, WorkflowPath, observed.CheckRef!).ConfigureAwait(false);
                observed.WorkflowExists = workflow is not null;
                observed.WorkflowManaged = workflow?.Content.Contains(ManagedWorkflowMarker, StringComparison.Ordinal) ?? false;

                var config = await client.TryGetFileAsync(owner, repo, ConfigPath, observed.CheckRef!).ConfigureAwait(false);
                if (config is not null) {
                    observed.ConfigExists = true;
                } else {
                    var legacyConfig = await client.TryGetFileAsync(owner, repo, LegacyConfigPath, observed.CheckRef!).ConfigureAwait(false);
                    observed.ConfigExists = legacyConfig is not null && LooksLikeReviewerConfig(legacyConfig.Content);
                }
            }
        }

        var secretName = SetupProviderCatalog.GetSecretName(context.Provider);
        if (needsRepoSecretCheck && !string.IsNullOrWhiteSpace(secretName)) {
            observed.RepoSecretLookup = await client.TryRepoSecretExistsAsync(owner, repo, secretName).ConfigureAwait(false);
        }
        if (needsOrgSecretCheck && !string.IsNullOrWhiteSpace(context.SecretOrg) && !string.IsNullOrWhiteSpace(secretName)) {
            observed.OrgSecretLookup = await client.TryOrgSecretExistsAsync(context.SecretOrg!, secretName).ConfigureAwait(false);
        }

        var runLookup = await client.ListWorkflowRunsAsync(owner, repo, WorkflowPath, maxCount: 1).ConfigureAwait(false);
        observed.WorkflowRunLookupStatus = runLookup.Status;
        observed.WorkflowRunLookupNote = runLookup.Note;
        if (runLookup.Runs.Count > 0) {
            observed.LatestWorkflowRun = runLookup.Runs[0];
        }

        return observed;
    }

    private static SetupPostApplyVerification Evaluate(SetupPostApplyContext context, SetupPostApplyObservedState observed) {
        var result = new SetupPostApplyVerification {
            Repo = context.Repo,
            Operation = DescribeOperation(context.Operation),
            PullRequestUrl = context.PullRequestUrl,
            CheckedRef = observed.CheckRef,
            CheckedRefSource = observed.CheckRefSource
        };

        AddPullRequestCheck(context, result);

        if (context.Operation == SetupApplyOperation.Setup) {
            AddSetupChecks(context, observed, result);
        } else if (context.Operation == SetupApplyOperation.Cleanup) {
            AddCleanupChecks(context, observed, result);
        } else if (context.Operation == SetupApplyOperation.UpdateSecret) {
            AddUpdateSecretChecks(context, observed, result);
        }
        AddLatestWorkflowRunCheck(observed, result);

        var hasDefinitiveChecks = false;
        var allDefinitivePassed = true;
        foreach (var check in result.Checks) {
            if (check.Skipped) {
                continue;
            }
            hasDefinitiveChecks = true;
            if (!check.Passed) {
                allDefinitivePassed = false;
            }
        }

        result.Passed = !hasDefinitiveChecks || allDefinitivePassed;
        result.Skipped = false;
        if (string.IsNullOrWhiteSpace(result.Note) && string.IsNullOrWhiteSpace(observed.CheckRef) &&
            (context.Operation == SetupApplyOperation.Setup || context.Operation == SetupApplyOperation.Cleanup)) {
            result.Note = "Repository branch state could not be resolved.";
        }
        return result;
    }

    private static void AddSetupChecks(SetupPostApplyContext context, SetupPostApplyObservedState observed,
        SetupPostApplyVerification result) {
        AddManagedWorkflowCheck(observed, expectedPresent: true, result);

        if (context.WithConfig) {
            AddPresenceCheck(
                result,
                SetupPostApplyCheckNames.ReviewerConfig,
                expected: "present",
                observedValue: observed.ConfigExists,
                whenTrue: "present",
                whenFalse: "missing",
                note: "Expected on setup branch.");
        } else {
            AddSkippedCheck(result, SetupPostApplyCheckNames.ReviewerConfig, "not requested", SetupPostApplyCheckValues.NotChecked,
                "with-config was disabled.");
        }

        AddSecretCheckForSetup(context, observed, result);
    }

    private static void AddCleanupChecks(SetupPostApplyContext context, SetupPostApplyObservedState observed,
        SetupPostApplyVerification result) {
        AddManagedWorkflowCheck(observed, expectedPresent: false, result);

        AddPresenceCheck(
            result,
            SetupPostApplyCheckNames.ReviewerConfig,
            expected: "missing",
            observedValue: observed.ConfigExists,
            whenTrue: "present",
            whenFalse: "missing",
            note: "Expected to be removed on cleanup branch.");

        if (!SetupProviderCatalog.RequiresManagedSecret(context.Provider)) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.RepoSecret, "not applicable", SetupPostApplyCheckValues.NotChecked,
                "Provider does not require a managed setup secret.");
            return;
        }

        AddSecretPresenceCheck(
            result,
            SetupPostApplyCheckNames.RepoSecret,
            expected: context.KeepSecret ? "present" : "missing",
            lookup: observed.RepoSecretLookup,
            note: context.KeepSecret ? "keep-secret enabled." : "cleanup deletes repo secret by default.");
    }

    private static void AddUpdateSecretChecks(SetupPostApplyContext context, SetupPostApplyObservedState observed,
        SetupPostApplyVerification result) {
        if (!SetupProviderCatalog.RequiresManagedSecret(context.Provider)) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.RepoSecret, "not applicable", SetupPostApplyCheckValues.NotChecked,
                "Provider does not require a managed setup secret.");
            return;
        }

        if (context.ExpectOrgSecret && !string.IsNullOrWhiteSpace(context.SecretOrg)) {
            AddSecretPresenceCheck(
                result,
                SetupPostApplyCheckNames.OrgSecret,
                expected: "present",
                lookup: observed.OrgSecretLookup,
                note: $"Organization: {context.SecretOrg}");
            return;
        }

        AddSecretPresenceCheck(
            result,
            SetupPostApplyCheckNames.RepoSecret,
            expected: "present",
            lookup: observed.RepoSecretLookup,
            note: "Update-secret should refresh this value.");
    }

    private static void AddSecretCheckForSetup(SetupPostApplyContext context, SetupPostApplyObservedState observed,
        SetupPostApplyVerification result) {
        if (!SetupProviderCatalog.RequiresManagedSecret(context.Provider)) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.RepoSecret, "not applicable", SetupPostApplyCheckValues.NotChecked,
                "Provider does not require a managed setup secret.");
            return;
        }

        if (context.ExpectOrgSecret && !string.IsNullOrWhiteSpace(context.SecretOrg)) {
            AddSecretPresenceCheck(
                result,
                SetupPostApplyCheckNames.OrgSecret,
                expected: "present",
                lookup: observed.OrgSecretLookup,
                note: $"Organization: {context.SecretOrg}");
            return;
        }

        if (context.ManualSecret) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.RepoSecret, "manual", SetupPostApplyCheckValues.NotChecked,
                "Manual secret mode requires user-managed secret upload.");
            return;
        }

        if (context.SkipSecret) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.RepoSecret, "skipped", SetupPostApplyCheckValues.NotChecked,
                "skip-secret enabled.");
            return;
        }

        AddSecretPresenceCheck(
            result,
            SetupPostApplyCheckNames.RepoSecret,
            expected: "present",
            lookup: observed.RepoSecretLookup,
            note: "Setup should create or update this secret.");
    }

    private static void AddLatestWorkflowRunCheck(SetupPostApplyObservedState observed, SetupPostApplyVerification result) {
        var lookupStatus = string.IsNullOrWhiteSpace(observed.WorkflowRunLookupStatus)
            ? SetupPostApplyCheckValues.Unknown
            : observed.WorkflowRunLookupStatus!;
        if (observed.LatestWorkflowRun is null) {
            if (!string.Equals(lookupStatus, SetupPostApplyCheckValues.Ok, StringComparison.OrdinalIgnoreCase)) {
                var lookupNote = string.IsNullOrWhiteSpace(observed.WorkflowRunLookupNote)
                    ? "Workflow run lookup failed."
                    : observed.WorkflowRunLookupNote!;
                AddSkippedCheck(result, SetupPostApplyCheckNames.LatestWorkflowRun, SetupPostApplyCheckValues.Observed, lookupStatus, lookupNote);
                return;
            }

            AddSkippedCheck(result, SetupPostApplyCheckNames.LatestWorkflowRun, SetupPostApplyCheckValues.Observed, SetupPostApplyCheckValues.None,
                "No workflow runs found for review-intelligencex.yml.");
            return;
        }

        var latestRun = observed.LatestWorkflowRun;
        var status = string.IsNullOrWhiteSpace(latestRun.Status) ? "unknown" : latestRun.Status!;
        var conclusion = string.IsNullOrWhiteSpace(latestRun.Conclusion) ? "n/a" : latestRun.Conclusion!;
        var actual = $"{status}/{conclusion}";
        var noteParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(latestRun.Url)) {
            noteParts.Add(latestRun.Url!);
        }
        if (latestRun.CreatedAt.HasValue) {
            noteParts.Add($"created {latestRun.CreatedAt.Value.UtcDateTime:u}");
        }
        if (!string.IsNullOrWhiteSpace(latestRun.HeadBranch)) {
            noteParts.Add($"branch {latestRun.HeadBranch}");
        }
        if (!string.IsNullOrWhiteSpace(latestRun.Event)) {
            noteParts.Add($"event {latestRun.Event}");
        }

        result.Checks.Add(new SetupPostApplyCheck {
            Name = SetupPostApplyCheckNames.LatestWorkflowRun,
            Expected = SetupPostApplyCheckValues.Observed,
            Actual = actual,
            Passed = true,
            Skipped = false,
            Note = noteParts.Count == 0 ? null : string.Join(" | ", noteParts)
        });
    }

    private static void AddPullRequestCheck(SetupPostApplyContext context, SetupPostApplyVerification result) {
        if (context.Operation == SetupApplyOperation.UpdateSecret) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.PullRequest, "not applicable", SetupPostApplyCheckValues.NotChecked,
                "update-secret does not create a pull request.");
            return;
        }

        var hasPr = !string.IsNullOrWhiteSpace(context.PullRequestUrl);
        if (hasPr) {
            result.Checks.Add(new SetupPostApplyCheck {
                Name = SetupPostApplyCheckNames.PullRequest,
                Expected = "created",
                Actual = "created",
                Passed = true,
                Skipped = false,
                Note = context.PullRequestUrl
            });
            return;
        }

        var output = context.Output ?? string.Empty;
        if (ContainsAny(output, "No files changed. Skipping PR creation.", "No files found to remove. Skipping PR creation.")) {
            result.Checks.Add(new SetupPostApplyCheck {
                Name = SetupPostApplyCheckNames.PullRequest,
                Expected = "not required",
                Actual = "not created (no file changes)",
                Passed = true,
                Skipped = false
            });
            return;
        }

        if (ContainsAny(output,
                "Files updated on branch, but PR was not created.",
                "Cleanup complete. Files removed on branch, but PR was not created.")) {
            result.Checks.Add(new SetupPostApplyCheck {
                Name = SetupPostApplyCheckNames.PullRequest,
                Expected = "created",
                Actual = "not created",
                Passed = false,
                Skipped = false,
                Note = "Branch was updated but pull request creation failed."
            });
            return;
        }

        AddSkippedCheck(result, SetupPostApplyCheckNames.PullRequest, SetupPostApplyCheckValues.Unknown, SetupPostApplyCheckValues.Unknown,
            "PR state could not be inferred from command output.");
    }

    private static void AddManagedWorkflowCheck(SetupPostApplyObservedState observed, bool expectedPresent,
        SetupPostApplyVerification result) {
        if (observed.WorkflowExists is null || observed.WorkflowManaged is null) {
            AddSkippedCheck(result, SetupPostApplyCheckNames.Workflow, expectedPresent ? "managed" : "missing", SetupPostApplyCheckValues.Unknown,
                "Repository branch state unavailable.");
            return;
        }

        var actual = observed.WorkflowExists.Value
            ? (observed.WorkflowManaged.Value ? "managed" : "present (unmanaged)")
            : "missing";

        var passed = expectedPresent
            ? observed.WorkflowExists.Value && observed.WorkflowManaged.Value
            : !observed.WorkflowExists.Value;

        result.Checks.Add(new SetupPostApplyCheck {
            Name = SetupPostApplyCheckNames.Workflow,
            Expected = expectedPresent ? "managed" : "missing",
            Actual = actual,
            Passed = passed,
            Skipped = false
        });
    }

    private static void AddSecretPresenceCheck(
        SetupPostApplyVerification result,
        string name,
        string expected,
        GitHubRepoClient.SecretLookupResult? lookup,
        string? note = null) {
        if (lookup is null) {
            AddSkippedCheck(result, name, expected, SetupPostApplyCheckValues.Unknown, "State unavailable from GitHub API.");
            return;
        }

        if (lookup.Exists.HasValue) {
            var actual = lookup.Exists.Value ? "present" : "missing";
            var passed = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
            result.Checks.Add(new SetupPostApplyCheck {
                Name = name,
                Expected = expected,
                Actual = actual,
                Passed = passed,
                Skipped = false,
                Note = CombineNotes(note, lookup.Note)
            });
            return;
        }

        if (string.Equals(lookup.Status, SetupPostApplyCheckValues.Unauthorized, StringComparison.Ordinal) ||
            string.Equals(lookup.Status, SetupPostApplyCheckValues.Forbidden, StringComparison.Ordinal) ||
            string.Equals(lookup.Status, SetupPostApplyCheckValues.RateLimited, StringComparison.Ordinal)) {
            result.Checks.Add(new SetupPostApplyCheck {
                Name = name,
                Expected = expected,
                Actual = lookup.Status,
                Passed = false,
                Skipped = false,
                Note = CombineNotes(note, lookup.Note)
            });
            return;
        }

        AddSkippedCheck(result, name, expected, lookup.Status, CombineNotes(note, lookup.Note));
    }

    private static void AddPresenceCheck(
        SetupPostApplyVerification result,
        string name,
        string expected,
        bool? observedValue,
        string whenTrue,
        string whenFalse,
        string? note = null) {
        if (!observedValue.HasValue) {
            AddSkippedCheck(result, name, expected, SetupPostApplyCheckValues.Unknown, "State unavailable from GitHub API.");
            return;
        }

        var actual = observedValue.Value ? whenTrue : whenFalse;
        var passed = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        result.Checks.Add(new SetupPostApplyCheck {
            Name = name,
            Expected = expected,
            Actual = actual,
            Passed = passed,
            Skipped = false,
            Note = note
        });
    }

    private static string CombineNotes(string? first, string? second) {
        if (string.IsNullOrWhiteSpace(first)) {
            return string.IsNullOrWhiteSpace(second) ? string.Empty : second!;
        }
        if (string.IsNullOrWhiteSpace(second)) {
            return first;
        }
        return $"{first} {second}";
    }

    private static void AddSkippedCheck(SetupPostApplyVerification result, string name, string expected, string actual, string note) {
        result.Checks.Add(new SetupPostApplyCheck {
            Name = name,
            Expected = expected,
            Actual = actual,
            Passed = true,
            Skipped = true,
            Note = note
        });
    }

    private static bool ShouldCheckRepoSecret(SetupPostApplyContext context) {
        if (!SetupProviderCatalog.RequiresManagedSecret(context.Provider)) {
            return false;
        }
        if (context.Operation == SetupApplyOperation.Setup) {
            if (context.ExpectOrgSecret) {
                return false;
            }
            if (context.SkipSecret || context.ManualSecret) {
                return false;
            }
            return true;
        }
        if (context.Operation == SetupApplyOperation.UpdateSecret) {
            return !context.ExpectOrgSecret;
        }
        if (context.Operation == SetupApplyOperation.Cleanup) {
            return true;
        }
        return false;
    }

    private static bool ShouldCheckOrgSecret(SetupPostApplyContext context) {
        if (!SetupProviderCatalog.SupportsOrgSecret(context.Provider)) {
            return false;
        }
        if (!context.ExpectOrgSecret) {
            return false;
        }
        if (string.IsNullOrWhiteSpace(context.SecretOrg)) {
            return false;
        }
        return context.Operation == SetupApplyOperation.Setup || context.Operation == SetupApplyOperation.UpdateSecret;
    }

    private static string DescribeOperation(SetupApplyOperation operation) {
        return operation switch {
            SetupApplyOperation.Cleanup => "cleanup",
            SetupApplyOperation.UpdateSecret => "update-secret",
            _ => "setup"
        };
    }

    private static bool ContainsAny(string text, params string[] phrases) {
        foreach (var phrase in phrases) {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool TryExtractPullRequestNumber(string? pullRequestUrl, out int number) {
        number = 0;
        if (string.IsNullOrWhiteSpace(pullRequestUrl)) {
            return false;
        }

        var marker = "/pull/";
        var index = pullRequestUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) {
            return false;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < pullRequestUrl.Length && char.IsDigit(pullRequestUrl[end])) {
            end++;
        }
        if (end == start) {
            return false;
        }

        return int.TryParse(pullRequestUrl[start..end], out number) && number > 0;
    }

    private static bool TryParseRepo(string repo, out string owner, out string name) {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo)) {
            return false;
        }
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        name = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }

    private static bool LooksLikeReviewerConfig(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }
            if (root.TryGetProperty("review", out var review) && review.ValueKind == JsonValueKind.Object) {
                return true;
            }
            if (root.TryGetProperty("provider", out _) ||
                root.TryGetProperty("model", out _) ||
                root.TryGetProperty("openaiModel", out _)) {
                return true;
            }
            return false;
        } catch {
            return false;
        }
    }
}
