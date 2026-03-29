using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class ProjectSyncRunner {
    private const double HighConfidenceIssueMatchLabelThreshold = 0.80;
    private const double HighConfidencePullRequestMatchLabelThreshold = 0.80;
    private const double CategoryLabelConfidenceThreshold = 0.62;
    private const double TagLabelConfidenceThreshold = 0.60;
    private const double DefaultIssueCommentMinConfidence = 0.55;
    private const double RejectVisionConfidenceThreshold = 0.70;
    private const double AcceptVisionConfidenceThreshold = 0.68;
    private const double MergeCandidateScoreThreshold = 82.0;
    private const double LowSignalQualityScoreThreshold = 50.0;

    internal sealed record RelatedIssueCandidate(
        int Number,
        string Url,
        double Confidence,
        string Reason
    );

    internal sealed record RelatedPullRequestCandidate(
        int Number,
        string Url,
        double Confidence,
        string Reason
    );

    internal sealed record PullRequestDecisionSignals(
        bool? IsDraft,
        string? Mergeable,
        string? ReviewDecision,
        string? StatusCheckState
    );

    internal sealed record ProjectSyncEntry(
        int Number,
        string Url,
        string Kind,
        double? TriageScore,
        string? DuplicateCluster,
        string? CanonicalItem,
        string? Category,
        IReadOnlyList<string> Tags,
        string? MatchedIssueUrl,
        double? MatchedIssueConfidence,
        string? VisionFit,
        double? VisionConfidence,
        string? MatchedIssueReason = null,
        IReadOnlyList<RelatedIssueCandidate>? RelatedIssues = null,
        string? SuggestedDecision = null,
        string? MatchedPullRequestUrl = null,
        double? MatchedPullRequestConfidence = null,
        string? MatchedPullRequestReason = null,
        IReadOnlyList<RelatedPullRequestCandidate>? RelatedPullRequests = null,
        IReadOnlyList<string>? ExistingLabels = null,
        double? CategoryConfidence = null,
        IReadOnlyDictionary<string, double>? TagConfidences = null,
        string? SignalQuality = null,
        double? SignalQualityScore = null,
        IReadOnlyList<string>? SignalQualityReasons = null,
        string? PullRequestSize = null,
        string? PullRequestChurnRisk = null,
        string? PullRequestMergeReadiness = null,
        string? PullRequestFreshness = null,
        string? PullRequestCheckHealth = null,
        string? PullRequestReviewLatency = null,
        string? PullRequestMergeConflictRisk = null,
        string? IssueReviewAction = null,
        double? IssueReviewActionConfidence = null,
        bool PrWatchGovernanceSuggested = false,
        string? PrWatchGovernanceSummary = null,
        string? PrWatchGovernanceSource = null
    );

    internal sealed record PrWatchGovernanceContext(
        string Source,
        bool RetryPolicyReviewSuggested,
        string SummaryLine,
        string TrackerIssueUrl
    );

    internal sealed class Options {
        public string? Owner { get; set; }
        public int? ProjectNumber { get; set; }
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public string ConfigPath { get; set; } = Path.Combine("artifacts", "triage", "ix-project-config.json");
        public string TriagePath { get; set; } = Path.Combine("artifacts", "triage", "ix-triage-index.json");
        public string VisionPath { get; set; } = Path.Combine("artifacts", "triage", "ix-vision-check.json");
        public string IssueReviewPath { get; set; } = Path.Combine("artifacts", "triage", "ix-issue-review.json");
        public int MaxItems { get; set; } = 500;
        public int ProjectItemScanLimit { get; set; } = 5000;
        public bool EnsureFields { get; set; } = true;
        public bool ApplyLabels { get; set; }
        public bool EnsureLabels { get; set; } = true;
        public bool ApplyLinkComments { get; set; }
        public bool ApplyPrWatchGovernanceLabels { get; set; }
        public bool ApplyPrWatchGovernanceLabelsSpecified { get; set; }
        public bool ApplyPrWatchGovernanceFields { get; set; }
        public bool ApplyPrWatchGovernanceFieldsSpecified { get; set; }
        public double LinkCommentMinConfidence { get; set; } = DefaultIssueCommentMinConfidence;
        public int LinkCommentMaxIssues { get; set; } = 3;
        public bool DryRun { get; set; }
        public bool ShowHelp { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        ProjectConfigDocument? projectConfig = null;
        string? projectConfigError = null;
        if (File.Exists(options.ConfigPath)) {
            if (ProjectConfigReader.TryReadFromFile(options.ConfigPath, out var loadedConfig, out var configError)) {
                projectConfig = loadedConfig;
                ApplyProjectConfigFeatureDefaults(options, loadedConfig.Features);
            } else {
                projectConfigError = configError;
                if (!HasExplicitProjectTarget(options)) {
                    Console.Error.WriteLine(projectConfigError);
                    return 1;
                }

                Console.Error.WriteLine($"Warning: {projectConfigError}");
            }
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        if (!TryResolveProjectTarget(options, projectConfig, projectConfigError, out var owner, out var projectNumber, out var resolveError)) {
            Console.Error.WriteLine(resolveError);
            return 1;
        }

        if (!File.Exists(options.TriagePath)) {
            Console.Error.WriteLine($"Triage index not found: {options.TriagePath}");
            return 1;
        }

        List<ProjectSyncEntry> entries;
        try {
            PrWatchGovernanceContext? prWatchGovernance = null;
            if (options.ApplyPrWatchGovernanceLabels || options.ApplyPrWatchGovernanceFields) {
                prWatchGovernance = await TryLoadPrWatchGovernanceContextAsync(options.Repo).ConfigureAwait(false);
            }

            entries = LoadEntries(
                options.TriagePath,
                options.VisionPath,
                options.IssueReviewPath,
                options.MaxItems,
                prWatchGovernance);

            if (options.ApplyPrWatchGovernanceLabels || options.ApplyPrWatchGovernanceFields) {
                var mode = options.ApplyPrWatchGovernanceLabels && options.ApplyPrWatchGovernanceFields
                    ? "labels+fields"
                    : options.ApplyPrWatchGovernanceLabels
                        ? "labels"
                        : "fields";
                if (prWatchGovernance is null) {
                    Console.WriteLine($"PR-watch governance {mode}: no tracker context found; governance sync will stay clear.");
                } else {
                    Console.WriteLine(
                        $"PR-watch governance {mode}: source={prWatchGovernance.Source}; " +
                        $"retryPolicyReviewSuggested={(prWatchGovernance.RetryPolicyReviewSuggested ? "true" : "false")}.");
                }
            }
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        if (entries.Count == 0) {
            Console.WriteLine("No triage/vision entries to sync.");
            return 0;
        }

        var client = new ProjectV2Client();
        ProjectV2Client.ProjectRef project;
        try {
            project = await client.TryGetProjectAsync(owner, projectNumber).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Project {projectNumber} was not found for owner '{owner}'.");
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        IReadOnlyDictionary<string, ProjectV2Client.ProjectField> fields;
        try {
            fields = await client.GetProjectFieldsByNameAsync(owner, projectNumber).ConfigureAwait(false);
            if (options.EnsureFields) {
                fields = await EnsureFieldsAsync(client, owner, projectNumber, options.ApplyPrWatchGovernanceFields).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        IReadOnlyDictionary<string, ProjectV2Client.ProjectItem> existingItems;
        try {
            existingItems = await client.GetProjectItemsByUrlAsync(owner, projectNumber, Math.Max(options.ProjectItemScanLimit, options.MaxItems))
                .ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load project items: {ex.Message}");
            return 1;
        }

        if (options.ApplyLabels && options.EnsureLabels && !options.DryRun) {
            try {
                var (categoryLabels, tagLabels) = BuildLabelTaxonomyForEntries(entries);
                var labelsToEnsure = ProjectLabelCatalog.BuildEnsureLabelCatalog(
                    categoryLabels,
                    tagLabels);
                await RepositoryLabelManager.EnsureLabelsAsync(options.Repo, labelsToEnsure).ConfigureAwait(false);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to ensure labels before apply-labels: {ex.Message}");
                return 1;
            }
        }

        var itemsByUrl = new Dictionary<string, ProjectV2Client.ProjectItem>(existingItems, StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var added = 0;
        var updatedFieldValues = 0;
        var skippedMissing = 0;
        var labeled = 0;
        var prCommentUpserts = 0;
        var issueCommentUpserts = 0;
        var prCommentDeletes = 0;
        var issueCommentDeletes = 0;

        foreach (var entry in entries) {
            processed++;
            if (!itemsByUrl.TryGetValue(entry.Url, out var item)) {
                if (options.DryRun) {
                    added++;
                    skippedMissing++;
                    continue;
                }

                var content = await client.ResolveContentByUrlAsync(entry.Url).ConfigureAwait(false);
                if (content is null) {
                    Console.Error.WriteLine($"Warning: unable to resolve content id for URL: {entry.Url}");
                    skippedMissing++;
                    continue;
                }

                try {
                    var itemId = await client.AddProjectItemByContentIdAsync(project.Id, content.Id).ConfigureAwait(false);
                    item = new ProjectV2Client.ProjectItem(itemId, content.Url, content.Id, content.ContentType);
                    itemsByUrl[entry.Url] = item;
                    itemsByUrl[content.Url] = item;
                    added++;
                } catch (Exception ex) {
                    Console.Error.WriteLine($"Warning: failed to add project item for URL '{entry.Url}': {ex.Message}");
                    skippedMissing++;
                    continue;
                }
            }

            if (!options.DryRun) {
                updatedFieldValues += await ApplyUpdatesAsync(
                    client,
                    project.Id,
                    item.Id,
                    fields,
                    entry,
                    options.ApplyPrWatchGovernanceFields).ConfigureAwait(false);
            }

            if (options.ApplyLabels) {
                var labels = BuildLabelsForEntry(entry);
                if (labels.Count > 0 && entry.Number > 0) {
                    if (options.DryRun) {
                        labeled++;
                    } else {
                        if (await RepositoryLabelManager.SyncManagedLabelsAsync(
                                options.Repo,
                                entry.Kind,
                                entry.Number,
                                labels,
                                entry.ExistingLabels).ConfigureAwait(false)) {
                            labeled++;
                        }
                    }
                }
            }

        }

        if (options.ApplyLinkComments) {
            var pullRequestSuggestionComments = BuildPullRequestIssueSuggestionComments(
                entries,
                options.LinkCommentMinConfidence,
                options.LinkCommentMaxIssues);
            var stalePullRequestCommentTargets = BuildStaleSuggestionCommentTargets(
                entries,
                "pull_request",
                pullRequestSuggestionComments);

            foreach (var suggestion in pullRequestSuggestionComments) {
                if (options.DryRun) {
                    prCommentUpserts++;
                    continue;
                }

                if (await IssueSuggestionCommentManager.UpsertAsync(
                        options.Repo,
                        suggestion.Key,
                        suggestion.Value).ConfigureAwait(false)) {
                    prCommentUpserts++;
                }
            }

            foreach (var pullRequestNumber in stalePullRequestCommentTargets) {
                if (options.DryRun) {
                    prCommentDeletes++;
                    continue;
                }

                if (await IssueSuggestionCommentManager.DeleteAsync(
                        options.Repo,
                        pullRequestNumber,
                        IssueSuggestionCommentManager.CommentMarker).ConfigureAwait(false)) {
                    prCommentDeletes++;
                }
            }

            var issueBacklinkComments = BuildIssueBacklinkSuggestionComments(
                entries,
                options.LinkCommentMinConfidence,
                options.LinkCommentMaxIssues);
            var staleIssueBacklinkCommentTargets = BuildStaleSuggestionCommentTargets(
                entries,
                "issue",
                issueBacklinkComments);

            foreach (var suggestion in issueBacklinkComments) {
                if (options.DryRun) {
                    issueCommentUpserts++;
                    continue;
                }

                if (await IssueSuggestionCommentManager.UpsertAsync(
                        options.Repo,
                        suggestion.Key,
                        suggestion.Value,
                        IssueSuggestionCommentManager.IssueBacklinkCommentMarker).ConfigureAwait(false)) {
                    issueCommentUpserts++;
                }
            }

            foreach (var issueNumber in staleIssueBacklinkCommentTargets) {
                if (options.DryRun) {
                    issueCommentDeletes++;
                    continue;
                }

                if (await IssueSuggestionCommentManager.DeleteAsync(
                        options.Repo,
                        issueNumber,
                        IssueSuggestionCommentManager.IssueBacklinkCommentMarker).ConfigureAwait(false)) {
                    issueCommentDeletes++;
                }
            }
        }

        Console.WriteLine($"Project sync target: {owner}#{projectNumber} ({project.Url})");
        Console.WriteLine($"Entries processed: {processed}");
        Console.WriteLine($"Items added: {added}");
        Console.WriteLine($"Field values updated: {(options.DryRun ? 0 : updatedFieldValues)}");
        if (options.ApplyLabels) {
            Console.WriteLine($"Items labeled: {labeled}");
        }
        if (options.ApplyLinkComments) {
            Console.WriteLine($"PR suggestion comments upserted: {prCommentUpserts}");
            Console.WriteLine($"PR suggestion comments deleted: {prCommentDeletes}");
            Console.WriteLine($"Issue backlink comments upserted: {issueCommentUpserts}");
            Console.WriteLine($"Issue backlink comments deleted: {issueCommentDeletes}");
        }
        Console.WriteLine($"Skipped unresolved items: {skippedMissing}");
        Console.WriteLine(options.DryRun ? "Dry run complete (no project updates were written)." : "Project sync complete.");
        return 0;
    }

    internal static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--owner":
                    if (i + 1 < args.Length) {
                        options.Owner = args[++i];
                    }
                    break;
                case "--project":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                        number > 0) {
                        options.ProjectNumber = number;
                    }
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--config":
                    if (i + 1 < args.Length) {
                        options.ConfigPath = args[++i];
                    }
                    break;
                case "--triage":
                    if (i + 1 < args.Length) {
                        options.TriagePath = args[++i];
                    }
                    break;
                case "--vision":
                    if (i + 1 < args.Length) {
                        options.VisionPath = args[++i];
                    }
                    break;
                case "--issue-review":
                    if (i + 1 < args.Length) {
                        options.IssueReviewPath = args[++i];
                    }
                    break;
                case "--max-items":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxItems)) {
                        options.MaxItems = Math.Max(1, Math.Min(maxItems, 5000));
                    }
                    break;
                case "--project-item-scan-limit":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scanLimit)) {
                        options.ProjectItemScanLimit = Math.Max(100, Math.Min(scanLimit, 10000));
                    }
                    break;
                case "--ensure-fields":
                    options.EnsureFields = true;
                    break;
                case "--no-ensure-fields":
                    options.EnsureFields = false;
                    break;
                case "--apply-labels":
                    options.ApplyLabels = true;
                    break;
                case "--ensure-labels":
                    options.EnsureLabels = true;
                    break;
                case "--no-ensure-labels":
                    options.EnsureLabels = false;
                    break;
                case "--apply-link-comments":
                    options.ApplyLinkComments = true;
                    break;
                case "--apply-pr-watch-governance-labels":
                    options.ApplyPrWatchGovernanceLabels = true;
                    options.ApplyPrWatchGovernanceLabelsSpecified = true;
                    break;
                case "--no-apply-pr-watch-governance-labels":
                    options.ApplyPrWatchGovernanceLabels = false;
                    options.ApplyPrWatchGovernanceLabelsSpecified = true;
                    break;
                case "--apply-pr-watch-governance-fields":
                    options.ApplyPrWatchGovernanceFields = true;
                    options.ApplyPrWatchGovernanceFieldsSpecified = true;
                    break;
                case "--no-apply-pr-watch-governance-fields":
                    options.ApplyPrWatchGovernanceFields = false;
                    options.ApplyPrWatchGovernanceFieldsSpecified = true;
                    break;
                case "--link-comment-min-confidence":
                    if (i + 1 < args.Length &&
                        double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var minConfidence)) {
                        options.LinkCommentMinConfidence = Math.Clamp(minConfidence, 0.0, 1.0);
                    }
                    break;
                case "--link-comment-max-issues":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxIssues)) {
                        options.LinkCommentMaxIssues = Math.Max(1, Math.Min(maxIssues, 10));
                    }
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ShowHelp = true;
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo project-sync [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --owner <login>          Project owner login (required unless --config resolves it)");
        Console.WriteLine("  --project <n>            Project number (required unless --config resolves it)");
        Console.WriteLine("  --repo <owner/name>      Repository context (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --config <path>          Project config JSON from project-init (default: artifacts/triage/ix-project-config.json)");
        Console.WriteLine("  --triage <path>          Triage index JSON (default: artifacts/triage/ix-triage-index.json)");
        Console.WriteLine("  --vision <path>          Vision check JSON (default: artifacts/triage/ix-vision-check.json)");
        Console.WriteLine("  --issue-review <path>    Issue review JSON (default: artifacts/triage/ix-issue-review.json)");
        Console.WriteLine("  --max-items <n>          Max entries to sync (1-5000, default: 500)");
        Console.WriteLine("  --project-item-scan-limit <n>  Existing project item scan limit (100-10000, default: 5000)");
        Console.WriteLine("  --ensure-fields          Ensure IX fields exist before sync (default)");
        Console.WriteLine("  --no-ensure-fields       Skip field creation");
        Console.WriteLine("  --apply-labels           Sync IX labels on PRs/issues (add missing + remove stale managed IX labels)");
        Console.WriteLine("  --ensure-labels          Ensure IX labels exist before applying labels (default)");
        Console.WriteLine("  --no-ensure-labels       Skip label ensure step");
        Console.WriteLine("  --apply-link-comments    Upsert marker comments on PRs/issues and delete stale managed suggestion comments");
        Console.WriteLine("  --apply-pr-watch-governance-labels  Add/remove the managed PR governance label from live pr-watch tracker state");
        Console.WriteLine("  --no-apply-pr-watch-governance-labels  Force governance label sync off even if config enables it");
        Console.WriteLine("  --apply-pr-watch-governance-fields  Sync optional PR governance fields from live pr-watch tracker state");
        Console.WriteLine("  --no-apply-pr-watch-governance-fields  Force governance field sync off even if config enables it");
        Console.WriteLine("  --link-comment-min-confidence <0-1>  Min confidence for suggestion comments (default: 0.55)");
        Console.WriteLine("  --link-comment-max-issues <n>  Max related issues to include per PR comment (1-10, default: 3)");
        Console.WriteLine("  --dry-run                Compute sync plan without writing project changes");
        Console.WriteLine();
        Console.WriteLine("Required token scopes for sync: `read:project` and `project`.");
    }

    internal static void ApplyProjectConfigFeatureDefaults(Options options, ProjectConfigFeatures features) {
        if (!options.ApplyPrWatchGovernanceLabelsSpecified) {
            options.ApplyPrWatchGovernanceLabels = features.PrWatchGovernanceLabels;
        }

        if (!options.ApplyPrWatchGovernanceFieldsSpecified) {
            options.ApplyPrWatchGovernanceFields = features.PrWatchGovernanceFields;
        }
    }

    private static bool HasExplicitProjectTarget(Options options) {
        return !string.IsNullOrWhiteSpace(options.Owner) && options.ProjectNumber.GetValueOrDefault() > 0;
    }

    private static bool TryResolveProjectTarget(
        Options options,
        ProjectConfigDocument? projectConfig,
        string? projectConfigError,
        out string owner,
        out int projectNumber,
        out string error) {
        owner = options.Owner?.Trim() ?? string.Empty;
        projectNumber = options.ProjectNumber ?? 0;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(owner) && projectNumber > 0) {
            return true;
        }

        if (!File.Exists(options.ConfigPath)) {
            error = "Owner/project not provided and project config file was not found. Use --owner/--project or run `todo project-init` first.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(projectConfigError)) {
            error = projectConfigError;
            return false;
        }

        if (projectConfig is not null) {
            if (string.IsNullOrWhiteSpace(owner)) {
                owner = projectConfig.Owner;
            }

            if (projectNumber <= 0) {
                projectNumber = projectConfig.ProjectNumber;
            }
        }

        if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
            error = "Unable to resolve owner/project from arguments or config.";
            return false;
        }
        return true;
    }

}
