using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class ProjectSyncRunner {
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
        double? IssueReviewActionConfidence = null
    );

    private sealed class Options {
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

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        if (!TryResolveProjectTarget(options, out var owner, out var projectNumber, out var resolveError)) {
            Console.Error.WriteLine(resolveError);
            return 1;
        }

        if (!File.Exists(options.TriagePath)) {
            Console.Error.WriteLine($"Triage index not found: {options.TriagePath}");
            return 1;
        }

        List<ProjectSyncEntry> entries;
        try {
            entries = LoadEntries(options.TriagePath, options.VisionPath, options.IssueReviewPath, options.MaxItems);
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
                fields = await EnsureFieldsAsync(client, owner, projectNumber).ConfigureAwait(false);
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
                updatedFieldValues += await ApplyUpdatesAsync(client, project.Id, item.Id, fields, entry).ConfigureAwait(false);
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

    private static Options ParseOptions(string[] args) {
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
        Console.WriteLine("  --link-comment-min-confidence <0-1>  Min confidence for suggestion comments (default: 0.55)");
        Console.WriteLine("  --link-comment-max-issues <n>  Max related issues to include per PR comment (1-10, default: 3)");
        Console.WriteLine("  --dry-run                Compute sync plan without writing project changes");
        Console.WriteLine();
        Console.WriteLine("Required token scopes for sync: `read:project` and `project`.");
    }

    private static bool TryResolveProjectTarget(Options options, out string owner, out int projectNumber, out string error) {
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

        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(options.ConfigPath));
            var root = doc.RootElement;
            if (string.IsNullOrWhiteSpace(owner)) {
                owner = ReadString(root, "owner");
            }
            if (projectNumber <= 0 &&
                TryGetProperty(root, "project", out var projectObj) &&
                projectObj.ValueKind == JsonValueKind.Object) {
                projectNumber = ReadInt(projectObj, "number");
            }
        } catch (Exception ex) {
            error = $"Failed to parse project config at {options.ConfigPath}: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
            error = "Unable to resolve owner/project from arguments or config.";
            return false;
        }
        return true;
    }

    private static List<ProjectSyncEntry> LoadEntries(string triagePath, string visionPath, string issueReviewPath, int maxItems) {
        using var triageDoc = JsonDocument.Parse(File.ReadAllText(triagePath));
        JsonDocument? visionDoc = null;
        JsonDocument? issueReviewDoc = null;
        if (File.Exists(visionPath)) {
            visionDoc = JsonDocument.Parse(File.ReadAllText(visionPath));
        }
        if (File.Exists(issueReviewPath)) {
            issueReviewDoc = JsonDocument.Parse(File.ReadAllText(issueReviewPath));
        }

        var entries = BuildEntriesFromDocuments(
            triageDoc.RootElement,
            visionDoc?.RootElement,
            maxItems,
            issueReviewDoc?.RootElement);
        visionDoc?.Dispose();
        issueReviewDoc?.Dispose();
        return entries;
    }

    internal static List<ProjectSyncEntry> BuildEntriesFromDocuments(
        JsonElement triageRoot,
        JsonElement? visionRoot,
        int maxItems,
        JsonElement? issueReviewRoot = null) {
        var entriesByUrl = new Dictionary<string, ProjectSyncEntry>(StringComparer.OrdinalIgnoreCase);
        var idToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clusterToCanonicalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var decisionSignalsByUrl = new Dictionary<string, PullRequestDecisionSignals>(StringComparer.OrdinalIgnoreCase);
        var bestPullRequestUrls = ParseBestPullRequestUrls(triageRoot);

        if (TryGetProperty(triageRoot, "items", out var items) && items.ValueKind == JsonValueKind.Array) {
            foreach (var item in items.EnumerateArray()) {
                var id = ReadString(item, "id");
                var url = ReadString(item, "url");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(url)) {
                    idToUrl[id] = url;
                }
            }

            if (TryGetProperty(triageRoot, "duplicateClusters", out var clusters) && clusters.ValueKind == JsonValueKind.Array) {
                foreach (var cluster in clusters.EnumerateArray()) {
                    var clusterId = ReadString(cluster, "id");
                    var canonicalId = ReadString(cluster, "canonicalItemId");
                    if (!string.IsNullOrWhiteSpace(clusterId) && !string.IsNullOrWhiteSpace(canonicalId)) {
                        clusterToCanonicalId[clusterId] = canonicalId;
                    }
                }
            }

            foreach (var item in items.EnumerateArray()) {
                var url = ReadString(item, "url");
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }
                var number = ReadInt(item, "number");
                var kind = ReadString(item, "kind");
                if (string.IsNullOrWhiteSpace(kind)) {
                    kind = "pull_request";
                }
                var triageScore = ReadNullableDouble(item, "score");
                var duplicateClusterId = ReadNullableString(item, "duplicateClusterId");
                var category = ReadNullableString(item, "category");
                var categoryConfidence = ReadNullableDouble(item, "categoryConfidence");
                var tags = ReadStringArray(item, "tags");
                var tagConfidences = ReadStringDoubleMap(item, "tagConfidences");
                var signalQuality = NormalizeSignalQuality(ReadNullableString(item, "signalQuality"));
                var signalQualityScore = ReadNullableDouble(item, "signalQualityScore");
                var signalQualityReasons = ReadStringArray(item, "signalQualityReasons");
                var pullRequestSize = NormalizePullRequestSize(ReadNullableString(item, "prSizeBand"));
                var pullRequestChurnRisk = NormalizePullRequestChurnRisk(ReadNullableString(item, "prChurnRisk"));
                var pullRequestMergeReadiness = NormalizePullRequestMergeReadiness(ReadNullableString(item, "prMergeReadiness"));
                var pullRequestFreshness = NormalizePullRequestFreshness(ReadNullableString(item, "prFreshness"));
                var pullRequestCheckHealth = NormalizePullRequestCheckHealth(ReadNullableString(item, "prCheckHealth"));
                var pullRequestReviewLatency = NormalizePullRequestReviewLatency(ReadNullableString(item, "prReviewLatency"));
                var pullRequestMergeConflictRisk = NormalizePullRequestMergeConflictRisk(ReadNullableString(item, "prMergeConflictRisk"));
                var existingLabels = ReadStringArray(item, "labels");
                var matchedIssueUrl = ReadNullableString(item, "matchedIssueUrl");
                var matchedIssueConfidence = ReadNullableDouble(item, "matchedIssueConfidence");
                var matchedIssueReason = ReadNullableString(item, "matchedIssueReason");
                var relatedIssues = ParseRelatedIssueCandidates(item);
                if (string.IsNullOrWhiteSpace(matchedIssueUrl) && relatedIssues.Count > 0) {
                    matchedIssueUrl = relatedIssues[0].Url;
                    matchedIssueConfidence = relatedIssues[0].Confidence;
                    matchedIssueReason = relatedIssues[0].Reason;
                } else if (!string.IsNullOrWhiteSpace(matchedIssueUrl) && !matchedIssueConfidence.HasValue) {
                    var confidenceFromRelated = relatedIssues
                        .Where(candidate => candidate.Number > 0 &&
                                            !string.IsNullOrWhiteSpace(candidate.Url) &&
                                            candidate.Url.Equals(matchedIssueUrl, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(candidate => candidate.Confidence)
                        .FirstOrDefault();
                    if (confidenceFromRelated is not null) {
                        matchedIssueConfidence = confidenceFromRelated.Confidence;
                        if (string.IsNullOrWhiteSpace(matchedIssueReason)) {
                            matchedIssueReason = confidenceFromRelated.Reason;
                        }
                    }
                }
                var matchedPullRequestUrl = ReadNullableString(item, "matchedPullRequestUrl");
                var matchedPullRequestConfidence = ReadNullableDouble(item, "matchedPullRequestConfidence");
                var matchedPullRequestReason = ReadNullableString(item, "matchedPullRequestReason");
                var relatedPullRequests = ParseRelatedPullRequestCandidates(item);
                if (string.IsNullOrWhiteSpace(matchedPullRequestUrl) && relatedPullRequests.Count > 0) {
                    matchedPullRequestUrl = relatedPullRequests[0].Url;
                    matchedPullRequestConfidence = relatedPullRequests[0].Confidence;
                    matchedPullRequestReason = relatedPullRequests[0].Reason;
                } else if (!string.IsNullOrWhiteSpace(matchedPullRequestUrl) &&
                           (string.IsNullOrWhiteSpace(matchedPullRequestReason) || !matchedPullRequestConfidence.HasValue)) {
                    var candidate = relatedPullRequests
                        .Where(itemCandidate => itemCandidate.Number > 0 &&
                                                !string.IsNullOrWhiteSpace(itemCandidate.Url) &&
                                                itemCandidate.Url.Equals(matchedPullRequestUrl, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(itemCandidate => itemCandidate.Confidence)
                        .ThenBy(itemCandidate => itemCandidate.Number)
                        .FirstOrDefault();
                    if (candidate is not null) {
                        if (!matchedPullRequestConfidence.HasValue) {
                            matchedPullRequestConfidence = candidate.Confidence;
                        }
                        if (string.IsNullOrWhiteSpace(matchedPullRequestReason)) {
                            matchedPullRequestReason = candidate.Reason;
                        }
                    }
                }
                var decisionSignals = ParsePullRequestSignals(item);
                if (decisionSignals is not null) {
                    decisionSignalsByUrl[url] = decisionSignals;
                }

                string? canonicalUrl = null;
                if (!string.IsNullOrWhiteSpace(duplicateClusterId) &&
                    clusterToCanonicalId.TryGetValue(duplicateClusterId, out var canonicalId) &&
                    idToUrl.TryGetValue(canonicalId, out var canonicalFromId)) {
                    canonicalUrl = canonicalFromId;
                }

                entriesByUrl[url] = new ProjectSyncEntry(
                    Number: number,
                    Url: url,
                    Kind: kind,
                    TriageScore: triageScore,
                    DuplicateCluster: duplicateClusterId,
                    CanonicalItem: canonicalUrl,
                    Category: category,
                    Tags: tags,
                    MatchedIssueUrl: matchedIssueUrl,
                    MatchedIssueConfidence: matchedIssueConfidence,
                    MatchedIssueReason: matchedIssueReason,
                    VisionFit: null,
                    VisionConfidence: null,
                    RelatedIssues: relatedIssues,
                    SuggestedDecision: null,
                    MatchedPullRequestUrl: matchedPullRequestUrl,
                    MatchedPullRequestConfidence: matchedPullRequestConfidence,
                    MatchedPullRequestReason: matchedPullRequestReason,
                    RelatedPullRequests: relatedPullRequests,
                    ExistingLabels: existingLabels,
                    CategoryConfidence: categoryConfidence,
                    TagConfidences: tagConfidences,
                    SignalQuality: signalQuality,
                    SignalQualityScore: signalQualityScore,
                    SignalQualityReasons: signalQualityReasons,
                    PullRequestSize: pullRequestSize,
                    PullRequestChurnRisk: pullRequestChurnRisk,
                    PullRequestMergeReadiness: pullRequestMergeReadiness,
                    PullRequestFreshness: pullRequestFreshness,
                    PullRequestCheckHealth: pullRequestCheckHealth,
                    PullRequestReviewLatency: pullRequestReviewLatency,
                    PullRequestMergeConflictRisk: pullRequestMergeConflictRisk
                );
            }
        }

        if (visionRoot.HasValue &&
            TryGetProperty(visionRoot.Value, "assessments", out var assessments) &&
            assessments.ValueKind == JsonValueKind.Array) {
            foreach (var assessment in assessments.EnumerateArray()) {
                var url = ReadString(assessment, "url");
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }
                var classification = ReadString(assessment, "classification");
                var confidence = ReadNullableDouble(assessment, "confidence");
                var score = ReadNullableDouble(assessment, "score");

                if (entriesByUrl.TryGetValue(url, out var existing)) {
                    entriesByUrl[url] = existing with {
                        VisionFit = string.IsNullOrWhiteSpace(classification) ? existing.VisionFit : classification,
                        VisionConfidence = confidence ?? existing.VisionConfidence,
                        TriageScore = existing.TriageScore ?? score
                    };
                } else {
                    var (kind, number) = ParseKindAndNumberFromUrl(url);
                    entriesByUrl[url] = new ProjectSyncEntry(
                        Number: number,
                        Url: url,
                        Kind: kind,
                        TriageScore: score,
                        DuplicateCluster: null,
                        CanonicalItem: null,
                        Category: null,
                        Tags: Array.Empty<string>(),
                        MatchedIssueUrl: null,
                        MatchedIssueConfidence: null,
                        VisionFit: classification,
                        VisionConfidence: confidence,
                        RelatedIssues: Array.Empty<RelatedIssueCandidate>(),
                        SuggestedDecision: null,
                        RelatedPullRequests: Array.Empty<RelatedPullRequestCandidate>(),
                        ExistingLabels: Array.Empty<string>(),
                        SignalQualityReasons: Array.Empty<string>()
                    );
                }
            }
        }

        if (issueReviewRoot.HasValue) {
            MergeIssueReviewAssessments(entriesByUrl, issueReviewRoot.Value);
        }

        foreach (var pair in entriesByUrl.ToList()) {
            decisionSignalsByUrl.TryGetValue(pair.Key, out var decisionSignals);
            var suggestion = SuggestMaintainerDecision(
                pair.Value,
                bestPullRequestUrls.Contains(pair.Key),
                decisionSignals);
            if (!string.IsNullOrWhiteSpace(suggestion)) {
                entriesByUrl[pair.Key] = pair.Value with { SuggestedDecision = suggestion };
            }
        }

        var issueMatchByUrl = BuildIssueToPullRequestMatchByUrl(entriesByUrl.Values);
        var issueMatchByNumber = BuildIssueToPullRequestMatchByNumber(entriesByUrl.Values);
        foreach (var pair in entriesByUrl.ToList()) {
            if (!pair.Value.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (issueMatchByUrl.TryGetValue(pair.Key, out var byUrlMatch) ||
                (pair.Value.Number > 0 && issueMatchByNumber.TryGetValue(pair.Value.Number, out byUrlMatch))) {
                if (!ShouldReplaceIssuePullRequestMatch(
                        pair.Value.MatchedPullRequestUrl,
                        pair.Value.MatchedPullRequestConfidence,
                        byUrlMatch)) {
                    continue;
                }

                entriesByUrl[pair.Key] = pair.Value with {
                    MatchedPullRequestUrl = byUrlMatch.Url,
                    MatchedPullRequestConfidence = byUrlMatch.Confidence,
                    MatchedPullRequestReason = byUrlMatch.Reason
                };
            }
        }

        return entriesByUrl.Values
            .OrderBy(entry => VisionPriority(entry.VisionFit))
            .ThenByDescending(entry => entry.TriageScore ?? double.MinValue)
            .ThenBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxItems))
            .ToList();
    }

    private static int VisionPriority(string? visionFit) {
        return visionFit?.ToLowerInvariant() switch {
            "likely-out-of-scope" => 0,
            "needs-human-review" => 1,
            "aligned" => 2,
            _ => 3
        };
    }

    private static string? NormalizeSignalQuality(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => null
        };
    }

    private static string? NormalizePullRequestSize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "xsmall" => "xsmall",
            "small" => "small",
            "medium" => "medium",
            "large" => "large",
            "xlarge" => "xlarge",
            _ => null
        };
    }

    private static string? NormalizePullRequestChurnRisk(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? NormalizePullRequestMergeReadiness(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "ready" => "ready",
            "needs-review" => "needs-review",
            "blocked" => "blocked",
            _ => null
        };
    }

    private static string? NormalizePullRequestFreshness(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "fresh" => "fresh",
            "recent" => "recent",
            "aging" => "aging",
            "stale" => "stale",
            _ => null
        };
    }

    private static string? NormalizePullRequestCheckHealth(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "healthy" => "healthy",
            "pending" => "pending",
            "failing" => "failing",
            "unknown" => "unknown",
            _ => null
        };
    }

    private static string? NormalizePullRequestReviewLatency(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? NormalizePullRequestMergeConflictRisk(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? SuggestMaintainerDecision(
        ProjectSyncEntry entry,
        bool isBestPullRequest,
        PullRequestDecisionSignals? prSignals) {
        if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var visionFit = entry.VisionFit?.Trim().ToLowerInvariant();
        var visionConfidence = entry.VisionConfidence ?? 0;
        var triageScore = entry.TriageScore ?? 0;

        if (visionFit == "likely-out-of-scope" && visionConfidence >= RejectVisionConfidenceThreshold) {
            return "reject";
        }

        if (IsLowSignalQuality(entry)) {
            return "defer";
        }

        var blockedBySignals = prSignals is not null && IsBlockedByReviewOrChecks(prSignals);
        if (blockedBySignals && visionFit != "likely-out-of-scope") {
            return "defer";
        }

        if (prSignals is not null &&
            IsStronglyReadyForMerge(prSignals) &&
            visionFit != "likely-out-of-scope" &&
            (isBestPullRequest || triageScore >= MergeCandidateScoreThreshold)) {
            return "merge-candidate";
        }

        if (visionFit == "aligned" &&
            visionConfidence >= AcceptVisionConfidenceThreshold &&
            triageScore >= 60 &&
            !blockedBySignals) {
            return "accept";
        }

        return "defer";
    }

    private static bool IsLowSignalQuality(ProjectSyncEntry entry) {
        if (!string.IsNullOrWhiteSpace(entry.SignalQuality) &&
            entry.SignalQuality.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return entry.SignalQualityScore.HasValue &&
               entry.SignalQualityScore.Value < LowSignalQualityScoreThreshold;
    }

    private static bool IsStronglyReadyForMerge(PullRequestDecisionSignals signals) {
        return signals.IsDraft == false &&
               string.Equals(signals.Mergeable, "MERGEABLE", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(signals.ReviewDecision, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(signals.StatusCheckState, "SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedByReviewOrChecks(PullRequestDecisionSignals signals) {
        if (signals.IsDraft == true) {
            return true;
        }

        if (string.Equals(signals.Mergeable, "CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(signals.Mergeable, "UNKNOWN", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(signals.ReviewDecision, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return string.Equals(signals.StatusCheckState, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(signals.StatusCheckState, "ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(signals.StatusCheckState, "PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ParseBestPullRequestUrls(JsonElement triageRoot) {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(triageRoot, "bestPullRequests", out var best) || best.ValueKind != JsonValueKind.Array) {
            return urls;
        }

        foreach (var candidate in best.EnumerateArray()) {
            var url = ReadString(candidate, "url");
            if (!string.IsNullOrWhiteSpace(url)) {
                urls.Add(url);
            }
        }
        return urls;
    }

    private static IReadOnlyDictionary<string, RelatedPullRequestCandidate> BuildIssueToPullRequestMatchByUrl(
        IEnumerable<ProjectSyncEntry> entries) {
        var map = new Dictionary<string, RelatedPullRequestCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            foreach (var candidate in entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>()) {
                if (string.IsNullOrWhiteSpace(candidate.Url) || candidate.Number <= 0) {
                    continue;
                }

                if (map.TryGetValue(candidate.Url, out var existing) &&
                    ComparePullRequestMatch(existing, entry.Number, candidate.Confidence) >= 0) {
                    continue;
                }

                map[candidate.Url] = new RelatedPullRequestCandidate(
                    entry.Number,
                    entry.Url,
                    candidate.Confidence,
                    candidate.Reason
                );
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<int, RelatedPullRequestCandidate> BuildIssueToPullRequestMatchByNumber(
        IEnumerable<ProjectSyncEntry> entries) {
        var map = new Dictionary<int, RelatedPullRequestCandidate>();
        foreach (var entry in entries) {
            if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            foreach (var candidate in entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>()) {
                if (candidate.Number <= 0) {
                    continue;
                }

                if (map.TryGetValue(candidate.Number, out var existing) &&
                    ComparePullRequestMatch(existing, entry.Number, candidate.Confidence) >= 0) {
                    continue;
                }

                map[candidate.Number] = new RelatedPullRequestCandidate(
                    entry.Number,
                    entry.Url,
                    candidate.Confidence,
                    candidate.Reason
                );
            }
        }

        return map;
    }

    private static int ComparePullRequestMatch(RelatedPullRequestCandidate existing, int number, double confidence) {
        var confidenceCompare = existing.Confidence.CompareTo(confidence);
        if (confidenceCompare != 0) {
            return confidenceCompare;
        }
        return number.CompareTo(existing.Number);
    }

    private static bool ShouldReplaceIssuePullRequestMatch(
        string? existingPullRequestUrl,
        double? existingConfidence,
        RelatedPullRequestCandidate candidate) {
        if (string.IsNullOrWhiteSpace(existingPullRequestUrl) || !existingConfidence.HasValue) {
            return true;
        }

        if (candidate.Confidence > existingConfidence.Value) {
            return true;
        }

        if (candidate.Confidence < existingConfidence.Value) {
            return false;
        }

        var (existingKind, existingNumber) = ParseKindAndNumberFromUrl(existingPullRequestUrl);
        if (!existingKind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || existingNumber <= 0) {
            return true;
        }

        return candidate.Number > 0 && candidate.Number < existingNumber;
    }

    private static PullRequestDecisionSignals? ParsePullRequestSignals(JsonElement item) {
        var kind = ReadString(item, "kind");
        if (!kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (!TryGetPropertyCaseInsensitive(item, "signals", out var signalsObj) ||
            signalsObj.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyCaseInsensitive(signalsObj, "pullRequest", out var prSignalsObj) ||
            prSignalsObj.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var isDraft = ReadNullableBoolCaseInsensitive(prSignalsObj, "isDraft");
        var mergeable = ReadNullableStringCaseInsensitive(prSignalsObj, "mergeable");
        var reviewDecision = ReadNullableStringCaseInsensitive(prSignalsObj, "reviewDecision");
        var statusCheckState = ReadNullableStringCaseInsensitive(prSignalsObj, "statusCheckState");
        if (!isDraft.HasValue &&
            string.IsNullOrWhiteSpace(mergeable) &&
            string.IsNullOrWhiteSpace(reviewDecision) &&
            string.IsNullOrWhiteSpace(statusCheckState)) {
            return null;
        }

        return new PullRequestDecisionSignals(
            isDraft,
            mergeable,
            reviewDecision,
            statusCheckState
        );
    }

    private static void MergeIssueReviewAssessments(
        IDictionary<string, ProjectSyncEntry> entriesByUrl,
        JsonElement issueReviewRoot) {
        if (!TryGetProperty(issueReviewRoot, "items", out var items) || items.ValueKind != JsonValueKind.Array) {
            return;
        }

        var issueEntriesByNumber = entriesByUrl.Values
            .Where(entry => entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) && entry.Number > 0)
            .GroupBy(entry => entry.Number)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var item in items.EnumerateArray()) {
            var proposedAction = NormalizeIssueReviewAction(ReadNullableStringCaseInsensitive(item, "proposedAction"));
            var actionConfidenceRaw = ReadNullableDoubleCaseInsensitive(item, "actionConfidence");
            var actionConfidence = actionConfidenceRaw.HasValue
                ? Math.Round(Math.Clamp(actionConfidenceRaw.Value, 0.0, 100.0), 2, MidpointRounding.AwayFromZero)
                : (double?)null;
            if (string.IsNullOrWhiteSpace(proposedAction) && !actionConfidence.HasValue) {
                continue;
            }

            var url = ReadNullableStringCaseInsensitive(item, "url") ?? string.Empty;
            var number = ReadInt(item, "number");
            if (string.IsNullOrWhiteSpace(url) &&
                number > 0 &&
                issueEntriesByNumber.TryGetValue(number, out var byNumberMatch)) {
                url = byNumberMatch.Url;
            }

            ProjectSyncEntry existing;
            if (!string.IsNullOrWhiteSpace(url) && entriesByUrl.TryGetValue(url, out var byUrlExisting)) {
                existing = byUrlExisting;
            } else if (number > 0 && issueEntriesByNumber.TryGetValue(number, out var byNumberExisting)) {
                existing = byNumberExisting;
                url = existing.Url;
            } else {
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }

                var (_, parsedNumber) = ParseKindAndNumberFromUrl(url);
                existing = new ProjectSyncEntry(
                    Number: number > 0 ? number : parsedNumber,
                    Url: url,
                    Kind: "issue",
                    TriageScore: null,
                    DuplicateCluster: null,
                    CanonicalItem: null,
                    Category: null,
                    Tags: Array.Empty<string>(),
                    MatchedIssueUrl: null,
                    MatchedIssueConfidence: null,
                    VisionFit: null,
                    VisionConfidence: null,
                    RelatedIssues: Array.Empty<RelatedIssueCandidate>(),
                    SuggestedDecision: null,
                    RelatedPullRequests: Array.Empty<RelatedPullRequestCandidate>(),
                    ExistingLabels: Array.Empty<string>(),
                    SignalQualityReasons: Array.Empty<string>()
                );
            }

            if (!existing.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var updated = existing with {
                IssueReviewAction = proposedAction ?? existing.IssueReviewAction,
                IssueReviewActionConfidence = actionConfidence ?? existing.IssueReviewActionConfidence
            };
            entriesByUrl[updated.Url] = updated;
            if (!string.IsNullOrWhiteSpace(url) &&
                !updated.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) {
                entriesByUrl[url] = updated;
            }
            if (updated.Number > 0) {
                issueEntriesByNumber[updated.Number] = updated;
            }
        }
    }

    private static string? NormalizeIssueReviewAction(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "close" => "close",
            "keep-open" => "keep-open",
            "needs-human-review" => "needs-human-review",
            "ignore" => "ignore",
            _ => null
        };
    }

    private static async Task<IReadOnlyDictionary<string, ProjectV2Client.ProjectField>> EnsureFieldsAsync(
        ProjectV2Client client,
        string owner,
        int projectNumber) {
        var fields = await client.GetProjectFieldsByNameAsync(owner, projectNumber).ConfigureAwait(false);
        foreach (var field in ProjectFieldCatalog.DefaultFields) {
            if (fields.ContainsKey(field.Name)) {
                continue;
            }
            await CreateFieldAsync(owner, projectNumber, field).ConfigureAwait(false);
        }
        return await client.GetProjectFieldsByNameAsync(owner, projectNumber).ConfigureAwait(false);
    }

    private static async Task CreateFieldAsync(string owner, int projectNumber, ProjectFieldDefinition field) {
        var args = new List<string> {
            "project", "field-create",
            projectNumber.ToString(CultureInfo.InvariantCulture),
            "--owner", owner,
            "--name", field.Name,
            "--data-type", field.DataType
        };
        if (field.DataType.Equals("SINGLE_SELECT", StringComparison.OrdinalIgnoreCase) &&
            field.SingleSelectOptions.Count > 0) {
            args.Add("--single-select-options");
            args.Add(string.Join(",", field.SingleSelectOptions));
        }
        var (code, _, stderr) = await GhCli.RunAsync(args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(
                $"Failed to create field '{field.Name}' in project #{projectNumber}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
        }
    }

    private static async Task<int> ApplyUpdatesAsync(
        ProjectV2Client client,
        string projectId,
        string itemId,
        IReadOnlyDictionary<string, ProjectV2Client.ProjectField> fields,
        ProjectSyncEntry entry) {
        var updated = 0;
        var isPullRequest = entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase);
        var isIssue = entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase);

        if (fields.TryGetValue("Triage Score", out var triageScoreField) && entry.TriageScore.HasValue) {
            await client.SetNumberFieldAsync(projectId, itemId, triageScoreField.Id, entry.TriageScore.Value).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Category", out var categoryField) && !string.IsNullOrWhiteSpace(entry.Category)) {
            if (TryResolveOptionId(categoryField, entry.Category, out var categoryOptionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, categoryField.Id, categoryOptionId).ConfigureAwait(false);
                updated++;
            } else {
                Console.Error.WriteLine($"Warning: option '{entry.Category}' not found in field '{categoryField.Name}'.");
            }
        }

        if (fields.TryGetValue("Category Confidence", out var categoryConfidenceField)) {
            if (entry.CategoryConfidence.HasValue) {
                await client.SetNumberFieldAsync(projectId, itemId, categoryConfidenceField.Id, entry.CategoryConfidence.Value).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, categoryConfidenceField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Signal Quality", out var signalQualityField)) {
            if (!string.IsNullOrWhiteSpace(entry.SignalQuality) &&
                TryResolveOptionId(signalQualityField, entry.SignalQuality, out var signalQualityOptionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, signalQualityField.Id, signalQualityOptionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, signalQualityField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Signal Quality Score", out var signalQualityScoreField)) {
            if (entry.SignalQualityScore.HasValue) {
                await client.SetNumberFieldAsync(projectId, itemId, signalQualityScoreField.Id, entry.SignalQualityScore.Value).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, signalQualityScoreField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Signal Quality Notes", out var signalQualityNotesField)) {
            var notes = BuildSignalQualityNotesFieldValue(entry, maxReasons: 4);
            if (!string.IsNullOrWhiteSpace(notes)) {
                await client.SetTextFieldAsync(projectId, itemId, signalQualityNotesField.Id, notes).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, signalQualityNotesField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Size", out var pullRequestSizeField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestSize) &&
                TryResolveOptionId(pullRequestSizeField, entry.PullRequestSize, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestSizeField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestSizeField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Churn Risk", out var pullRequestChurnRiskField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestChurnRisk) &&
                TryResolveOptionId(pullRequestChurnRiskField, entry.PullRequestChurnRisk, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestChurnRiskField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestChurnRiskField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Merge Readiness", out var pullRequestMergeReadinessField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestMergeReadiness) &&
                TryResolveOptionId(pullRequestMergeReadinessField, entry.PullRequestMergeReadiness, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestMergeReadinessField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestMergeReadinessField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Freshness", out var pullRequestFreshnessField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestFreshness) &&
                TryResolveOptionId(pullRequestFreshnessField, entry.PullRequestFreshness, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestFreshnessField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestFreshnessField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Check Health", out var pullRequestCheckHealthField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestCheckHealth) &&
                TryResolveOptionId(pullRequestCheckHealthField, entry.PullRequestCheckHealth, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestCheckHealthField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestCheckHealthField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Review Latency", out var pullRequestReviewLatencyField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestReviewLatency) &&
                TryResolveOptionId(pullRequestReviewLatencyField, entry.PullRequestReviewLatency, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestReviewLatencyField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestReviewLatencyField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("PR Merge Conflict Risk", out var pullRequestMergeConflictRiskField)) {
            if (isPullRequest &&
                !string.IsNullOrWhiteSpace(entry.PullRequestMergeConflictRisk) &&
                TryResolveOptionId(pullRequestMergeConflictRiskField, entry.PullRequestMergeConflictRisk, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, pullRequestMergeConflictRiskField.Id, optionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, pullRequestMergeConflictRiskField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Tags", out var tagsField) && entry.Tags.Count > 0) {
            await client.SetTextFieldAsync(projectId, itemId, tagsField.Id, string.Join(", ", entry.Tags)).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Tag Confidence Summary", out var tagConfidenceSummaryField)) {
            var tagConfidenceSummary = BuildTagConfidenceSummaryFieldValue(entry, maxTags: 10);
            if (!string.IsNullOrWhiteSpace(tagConfidenceSummary)) {
                await client.SetTextFieldAsync(projectId, itemId, tagConfidenceSummaryField.Id, tagConfidenceSummary).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, tagConfidenceSummaryField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isPullRequest && fields.TryGetValue("Matched Issue", out var matchedIssueField)) {
            if (!string.IsNullOrWhiteSpace(entry.MatchedIssueUrl)) {
                await client.SetTextFieldAsync(projectId, itemId, matchedIssueField.Id, entry.MatchedIssueUrl).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedIssueField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isPullRequest && fields.TryGetValue("Matched Issue Confidence", out var matchedIssueConfidenceField)) {
            if (entry.MatchedIssueConfidence.HasValue) {
                await client.SetNumberFieldAsync(projectId, itemId, matchedIssueConfidenceField.Id, entry.MatchedIssueConfidence.Value).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedIssueConfidenceField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isPullRequest && fields.TryGetValue("Matched Issue Reason", out var matchedIssueReasonField)) {
            var matchReasonValue = BuildMatchReasonFieldValue(entry.MatchedIssueReason);
            if (!string.IsNullOrWhiteSpace(matchReasonValue)) {
                await client.SetTextFieldAsync(projectId, itemId, matchedIssueReasonField.Id, matchReasonValue).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedIssueReasonField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isPullRequest && fields.TryGetValue("Related Issues", out var relatedIssuesField)) {
            var relatedIssuesValue = BuildRelatedIssuesFieldValue(entry, maxIssues: 3);
            if (!string.IsNullOrWhiteSpace(relatedIssuesValue)) {
                await client.SetTextFieldAsync(projectId, itemId, relatedIssuesField.Id, relatedIssuesValue).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, relatedIssuesField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Issue Review Action", out var issueReviewActionField)) {
            if (isIssue &&
                !string.IsNullOrWhiteSpace(entry.IssueReviewAction) &&
                TryResolveOptionId(issueReviewActionField, entry.IssueReviewAction, out var issueReviewActionOptionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, issueReviewActionField.Id, issueReviewActionOptionId).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, issueReviewActionField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Issue Review Action Confidence", out var issueReviewActionConfidenceField)) {
            if (isIssue && entry.IssueReviewActionConfidence.HasValue) {
                await client.SetNumberFieldAsync(projectId, itemId, issueReviewActionConfidenceField.Id, entry.IssueReviewActionConfidence.Value).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, issueReviewActionConfidenceField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isIssue && fields.TryGetValue("Matched Pull Request", out var matchedPullRequestField)) {
            if (!string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl)) {
                await client.SetTextFieldAsync(projectId, itemId, matchedPullRequestField.Id, entry.MatchedPullRequestUrl).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedPullRequestField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isIssue &&
            fields.TryGetValue("Matched Pull Request Confidence", out var matchedPullRequestConfidenceField)) {
            if (entry.MatchedPullRequestConfidence.HasValue) {
                await client.SetNumberFieldAsync(projectId, itemId, matchedPullRequestConfidenceField.Id, entry.MatchedPullRequestConfidence.Value).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedPullRequestConfidenceField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isIssue && fields.TryGetValue("Matched Pull Request Reason", out var matchedPullRequestReasonField)) {
            var matchReasonValue = BuildMatchReasonFieldValue(entry.MatchedPullRequestReason);
            if (!string.IsNullOrWhiteSpace(matchReasonValue)) {
                await client.SetTextFieldAsync(projectId, itemId, matchedPullRequestReasonField.Id, matchReasonValue).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, matchedPullRequestReasonField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (isIssue && fields.TryGetValue("Related Pull Requests", out var relatedPullRequestsField)) {
            var relatedPullRequestsValue = BuildRelatedPullRequestsFieldValue(entry, maxPullRequests: 3);
            if (!string.IsNullOrWhiteSpace(relatedPullRequestsValue)) {
                await client.SetTextFieldAsync(projectId, itemId, relatedPullRequestsField.Id, relatedPullRequestsValue).ConfigureAwait(false);
            } else {
                await client.ClearFieldAsync(projectId, itemId, relatedPullRequestsField.Id).ConfigureAwait(false);
            }
            updated++;
        }

        if (fields.TryGetValue("Duplicate Cluster", out var duplicateField) && !string.IsNullOrWhiteSpace(entry.DuplicateCluster)) {
            await client.SetTextFieldAsync(projectId, itemId, duplicateField.Id, entry.DuplicateCluster).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Canonical Item", out var canonicalField) && !string.IsNullOrWhiteSpace(entry.CanonicalItem)) {
            await client.SetTextFieldAsync(projectId, itemId, canonicalField.Id, entry.CanonicalItem).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Vision Fit", out var visionField) && !string.IsNullOrWhiteSpace(entry.VisionFit)) {
            if (TryResolveOptionId(visionField, entry.VisionFit, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, visionField.Id, optionId).ConfigureAwait(false);
                updated++;
            } else {
                Console.Error.WriteLine($"Warning: option '{entry.VisionFit}' not found in field '{visionField.Name}'.");
            }
        }

        if (fields.TryGetValue("Vision Confidence", out var confidenceField) && entry.VisionConfidence.HasValue) {
            await client.SetNumberFieldAsync(projectId, itemId, confidenceField.Id, entry.VisionConfidence.Value).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("IX Suggested Decision", out var suggestedDecisionField) &&
            !string.IsNullOrWhiteSpace(entry.SuggestedDecision)) {
            if (TryResolveOptionId(suggestedDecisionField, entry.SuggestedDecision, out var optionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, suggestedDecisionField.Id, optionId).ConfigureAwait(false);
                updated++;
            } else {
                Console.Error.WriteLine($"Warning: option '{entry.SuggestedDecision}' not found in field '{suggestedDecisionField.Name}'.");
            }
        }

        if (fields.TryGetValue("Triage Kind", out var kindField) && !string.IsNullOrWhiteSpace(entry.Kind)) {
            if (TryResolveOptionId(kindField, entry.Kind, out var kindOptionId)) {
                await client.SetSingleSelectFieldAsync(projectId, itemId, kindField.Id, kindOptionId).ConfigureAwait(false);
                updated++;
            } else {
                Console.Error.WriteLine($"Warning: option '{entry.Kind}' not found in field '{kindField.Name}'.");
            }
        }

        return updated;
    }

    internal static IReadOnlyList<string> BuildLabelsForEntry(ProjectSyncEntry entry) {
        var labels = new List<string>();
        var isPullRequest = entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase);
        var isIssue = entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase);

        if (ShouldApplyCategoryLabel(entry) &&
            ProjectLabelCatalog.TryMapCategoryLabel(entry.Category ?? string.Empty, out var categoryLabel)) {
            labels.Add(categoryLabel);
        }

        foreach (var tag in entry.Tags) {
            if (ShouldApplyTagLabel(entry, tag) &&
                ProjectLabelCatalog.TryMapTagLabel(tag, out var tagLabel)) {
                labels.Add(tagLabel);
            }
        }

        var visionLabel = MapVisionLabel(entry.VisionFit);
        if (!string.IsNullOrWhiteSpace(visionLabel) && isPullRequest) {
            labels.Add(visionLabel);
        }

        var relatedTopCandidate = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .FirstOrDefault();

        var effectiveMatchedIssueUrl = !string.IsNullOrWhiteSpace(entry.MatchedIssueUrl)
            ? entry.MatchedIssueUrl
            : relatedTopCandidate?.Url;
        var effectiveMatchedIssueConfidence = entry.MatchedIssueConfidence ??
                                              relatedTopCandidate?.Confidence;

        if (!string.IsNullOrWhiteSpace(effectiveMatchedIssueUrl) && isPullRequest) {
            if (effectiveMatchedIssueConfidence.HasValue &&
                effectiveMatchedIssueConfidence.Value >= HighConfidenceIssueMatchLabelThreshold) {
                labels.Add("ix/match:linked-issue");
            } else {
                labels.Add("ix/match:needs-review");
            }
        }

        var relatedPullRequestTopCandidate = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .FirstOrDefault();

        var effectiveMatchedPullRequestUrl = !string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl)
            ? entry.MatchedPullRequestUrl
            : relatedPullRequestTopCandidate?.Url;
        var effectiveMatchedPullRequestConfidence = entry.MatchedPullRequestConfidence ??
                                                    relatedPullRequestTopCandidate?.Confidence;

        if (!string.IsNullOrWhiteSpace(effectiveMatchedPullRequestUrl) && isIssue) {
            if (effectiveMatchedPullRequestConfidence.HasValue &&
                effectiveMatchedPullRequestConfidence.Value >= HighConfidencePullRequestMatchLabelThreshold) {
                labels.Add("ix/match:linked-pr");
            } else {
                labels.Add("ix/match:needs-review-pr");
            }
        }

        var decisionLabel = MapSuggestedDecisionLabel(entry.SuggestedDecision);
        if (!string.IsNullOrWhiteSpace(decisionLabel) && isPullRequest) {
            labels.Add(decisionLabel);
        }

        if (IsLowSignalQuality(entry)) {
            labels.Add("ix/signal:low");
        }

        if (!string.IsNullOrWhiteSpace(entry.DuplicateCluster)) {
            labels.Add("ix/duplicate:clustered");
        }

        return labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldApplyCategoryLabel(ProjectSyncEntry entry) {
        if (!entry.CategoryConfidence.HasValue) {
            // Backward-compatible default for legacy triage artifacts without confidence fields.
            return true;
        }

        return entry.CategoryConfidence.Value >= CategoryLabelConfidenceThreshold;
    }

    private static bool ShouldApplyTagLabel(ProjectSyncEntry entry, string tag) {
        if (string.IsNullOrWhiteSpace(tag)) {
            return false;
        }

        if (entry.TagConfidences is null ||
            !entry.TagConfidences.TryGetValue(tag, out var confidence)) {
            // Backward-compatible default for legacy triage artifacts without confidence fields.
            return true;
        }

        return confidence >= TagLabelConfidenceThreshold;
    }

    private static (IReadOnlyList<string> Categories, IReadOnlyList<string> Tags) BuildLabelTaxonomyForEntries(
        IReadOnlyList<ProjectSyncEntry> entries) {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            foreach (var label in BuildLabelsForEntry(entry)) {
                if (label.StartsWith("ix/category:", StringComparison.OrdinalIgnoreCase)) {
                    categories.Add(label["ix/category:".Length..]);
                    continue;
                }

                if (label.StartsWith("ix/tag:", StringComparison.OrdinalIgnoreCase)) {
                    tags.Add(label["ix/tag:".Length..]);
                }
            }
        }

        return (
            categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            tags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static string BuildRelatedIssuesFieldValue(ProjectSyncEntry entry, int maxIssues) {
        var limit = Math.Max(1, Math.Min(maxIssues, 10));
        var related = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (related.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, related.Select(candidate =>
            $"#{candidate.Number.ToString(CultureInfo.InvariantCulture)} | {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {candidate.Url}"));
    }

    internal static string BuildMatchReasonFieldValue(string? reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return string.Empty;
        }

        return NormalizeCommentReason(reason);
    }

    internal static string BuildTagConfidenceSummaryFieldValue(ProjectSyncEntry entry, int maxTags) {
        var limit = Math.Max(1, Math.Min(maxTags, 20));
        if (entry.TagConfidences is null || entry.TagConfidences.Count == 0) {
            return string.Empty;
        }

        var normalized = entry.TagConfidences
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, double>(pair.Key.Trim(), Math.Clamp(pair.Value, 0, 1)))
            .ToList();
        if (normalized.Count == 0) {
            return string.Empty;
        }

        var tagSet = entry.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = tagSet.Count > 0
            ? normalized.Where(pair => tagSet.Contains(pair.Key)).ToList()
            : normalized;
        if (selected.Count == 0) {
            selected = normalized;
        }

        var summaryLines = selected
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(pair => $"{pair.Key}: {pair.Value.ToString("0.00", CultureInfo.InvariantCulture)}")
            .ToList();
        if (summaryLines.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, summaryLines);
    }

    internal static string BuildSignalQualityNotesFieldValue(ProjectSyncEntry entry, int maxReasons) {
        var limit = Math.Max(1, Math.Min(maxReasons, 10));
        var reasons = (entry.SignalQualityReasons ?? Array.Empty<string>())
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
        if (reasons.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, reasons);
    }

    internal static string BuildRelatedPullRequestsFieldValue(ProjectSyncEntry entry, int maxPullRequests) {
        var limit = Math.Max(1, Math.Min(maxPullRequests, 10));
        var related = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();

        if (related.Count == 0 &&
            !string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
            entry.MatchedPullRequestConfidence.HasValue) {
            var (_, matchedNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
            if (matchedNumber > 0) {
                related.Add(new RelatedPullRequestCandidate(
                    Number: matchedNumber,
                    Url: entry.MatchedPullRequestUrl,
                    Confidence: entry.MatchedPullRequestConfidence.Value,
                    Reason: "issue-side matched pull request"
                ));
            }
        }

        if (related.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, related.Select(candidate =>
            $"PR #{candidate.Number.ToString(CultureInfo.InvariantCulture)} | {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {candidate.Url}"));
    }

    internal static IReadOnlyDictionary<int, string> BuildPullRequestIssueSuggestionComments(
        IReadOnlyList<ProjectSyncEntry> entries,
        double minConfidence,
        int maxIssuesPerPullRequest) {
        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var pullRequestToCandidates = new Dictionary<int, Dictionary<int, RelatedIssueCandidate>>();

        static void AddPullRequestCandidate(
            IDictionary<int, Dictionary<int, RelatedIssueCandidate>> pullRequestMap,
            int pullRequestNumber,
            RelatedIssueCandidate candidate) {
            if (pullRequestNumber <= 0 || candidate.Number <= 0 || string.IsNullOrWhiteSpace(candidate.Url)) {
                return;
            }

            if (!pullRequestMap.TryGetValue(pullRequestNumber, out var issueMap)) {
                issueMap = new Dictionary<int, RelatedIssueCandidate>();
                pullRequestMap[pullRequestNumber] = issueMap;
            }

            if (issueMap.TryGetValue(candidate.Number, out var existing) &&
                existing.Confidence >= candidate.Confidence) {
                return;
            }

            issueMap[candidate.Number] = candidate;
        }

        foreach (var entry in entries) {
            if (entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && entry.Number > 0) {
                var relatedIssues = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
                    .Where(candidate => candidate.Number > 0 &&
                                        candidate.Confidence >= threshold &&
                                        !string.IsNullOrWhiteSpace(candidate.Url))
                    .ToList();

                foreach (var candidate in relatedIssues) {
                    AddPullRequestCandidate(pullRequestToCandidates, entry.Number, new RelatedIssueCandidate(
                        Number: candidate.Number,
                        Url: candidate.Url,
                        Confidence: candidate.Confidence,
                        Reason: candidate.Reason
                    ));
                }

                if (!string.IsNullOrWhiteSpace(entry.MatchedIssueUrl) &&
                    entry.MatchedIssueConfidence.HasValue &&
                    entry.MatchedIssueConfidence.Value >= threshold) {
                    var (kind, issueNumber) = ParseKindAndNumberFromUrl(entry.MatchedIssueUrl);
                    if (kind.Equals("issue", StringComparison.OrdinalIgnoreCase) && issueNumber > 0) {
                        AddPullRequestCandidate(pullRequestToCandidates, entry.Number, new RelatedIssueCandidate(
                            Number: issueNumber,
                            Url: entry.MatchedIssueUrl,
                            Confidence: entry.MatchedIssueConfidence.Value,
                            Reason: "matched issue"
                        ));
                    }
                }

                continue;
            }

            if (!entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            var relatedPullRequests = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
                .Where(candidate => candidate.Number > 0 &&
                                    candidate.Confidence >= threshold &&
                                    !string.IsNullOrWhiteSpace(candidate.Url))
                .ToList();

            foreach (var candidate in relatedPullRequests) {
                AddPullRequestCandidate(pullRequestToCandidates, candidate.Number, new RelatedIssueCandidate(
                    Number: entry.Number,
                    Url: entry.Url,
                    Confidence: candidate.Confidence,
                    Reason: candidate.Reason
                ));
            }

            if (!string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
                entry.MatchedPullRequestConfidence.HasValue &&
                entry.MatchedPullRequestConfidence.Value >= threshold) {
                var (kind, pullRequestNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
                if (kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && pullRequestNumber > 0) {
                    AddPullRequestCandidate(pullRequestToCandidates, pullRequestNumber, new RelatedIssueCandidate(
                        Number: entry.Number,
                        Url: entry.Url,
                        Confidence: entry.MatchedPullRequestConfidence.Value,
                        Reason: "issue-side matched pull request"
                    ));
                }
            }
        }

        var comments = new Dictionary<int, string>();
        foreach (var pullRequestCandidates in pullRequestToCandidates) {
            var comment = BuildIssueMatchSuggestionComment(
                pullRequestCandidates.Key,
                pullRequestCandidates.Value.Values.ToList(),
                threshold,
                maxIssuesPerPullRequest);
            if (!string.IsNullOrWhiteSpace(comment)) {
                comments[pullRequestCandidates.Key] = comment;
            }
        }

        return comments;
    }

    internal static IReadOnlyList<int> BuildStaleSuggestionCommentTargets(
        IReadOnlyList<ProjectSyncEntry> entries,
        string kind,
        IReadOnlyDictionary<int, string> activeComments) {
        if (string.IsNullOrWhiteSpace(kind) || entries.Count == 0) {
            return Array.Empty<int>();
        }

        var activeNumbers = new HashSet<int>(activeComments.Keys);
        return entries
            .Where(entry => entry.Number > 0 &&
                            entry.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                            !activeNumbers.Contains(entry.Number))
            .Select(entry => entry.Number)
            .Distinct()
            .OrderBy(number => number)
            .ToList();
    }

    internal static string? BuildIssueMatchSuggestionComment(ProjectSyncEntry entry, double minConfidence, int maxIssues) {
        if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
            return null;
        }

        return BuildIssueMatchSuggestionComment(
            entry.Number,
            entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>(),
            minConfidence,
            maxIssues);
    }

    internal static string? BuildIssueMatchSuggestionComment(
        int pullRequestNumber,
        IReadOnlyList<RelatedIssueCandidate> candidates,
        double minConfidence,
        int maxIssues) {
        if (pullRequestNumber <= 0 || candidates.Count == 0) {
            return null;
        }

        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var limit = Math.Max(1, Math.Min(maxIssues, 10));
        var related = candidates
            .Where(candidate => candidate.Number > 0 &&
                                candidate.Confidence >= threshold &&
                                !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (related.Count == 0) {
            return null;
        }

        var lines = new List<string> {
            IssueSuggestionCommentManager.CommentMarker,
            "### IntelligenceX Related Issue Suggestions",
            string.Empty,
            $"Automated match candidates (confidence >= {threshold.ToString("0.00", CultureInfo.InvariantCulture)}). Please verify before linking/closing.",
            string.Empty
        };

        foreach (var candidate in related) {
            var reason = NormalizeCommentReason(candidate.Reason);
            lines.Add($"- #{candidate.Number} ({candidate.Url}) - confidence {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} - {reason}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    internal static IReadOnlyDictionary<int, string> BuildIssueBacklinkSuggestionComments(
        IReadOnlyList<ProjectSyncEntry> entries,
        double minConfidence,
        int maxPullRequestsPerIssue) {
        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var issueToCandidates = new Dictionary<int, Dictionary<int, RelatedPullRequestCandidate>>();

        static void AddIssueCandidate(
            IDictionary<int, Dictionary<int, RelatedPullRequestCandidate>> issueMap,
            int issueNumber,
            RelatedPullRequestCandidate candidate) {
            if (issueNumber <= 0 || candidate.Number <= 0 || string.IsNullOrWhiteSpace(candidate.Url)) {
                return;
            }

            if (!issueMap.TryGetValue(issueNumber, out var pullRequestMap)) {
                pullRequestMap = new Dictionary<int, RelatedPullRequestCandidate>();
                issueMap[issueNumber] = pullRequestMap;
            }

            if (pullRequestMap.TryGetValue(candidate.Number, out var existing) &&
                existing.Confidence >= candidate.Confidence) {
                return;
            }

            pullRequestMap[candidate.Number] = candidate;
        }

        foreach (var entry in entries) {
            if (entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && entry.Number > 0) {
                var related = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
                    .Where(candidate => candidate.Number > 0 &&
                                        candidate.Confidence >= threshold &&
                                        !string.IsNullOrWhiteSpace(candidate.Url))
                    .ToList();
                foreach (var candidate in related) {
                    AddIssueCandidate(issueToCandidates, candidate.Number, new RelatedPullRequestCandidate(
                        Number: entry.Number,
                        Url: entry.Url,
                        Confidence: candidate.Confidence,
                        Reason: candidate.Reason
                    ));
                }
                continue;
            }

            if (!entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            var relatedPullRequests = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
                .Where(candidate => candidate.Number > 0 &&
                                    candidate.Confidence >= threshold &&
                                    !string.IsNullOrWhiteSpace(candidate.Url))
                .ToList();

            foreach (var candidate in relatedPullRequests) {
                AddIssueCandidate(issueToCandidates, entry.Number, new RelatedPullRequestCandidate(
                    Number: candidate.Number,
                    Url: candidate.Url,
                    Confidence: candidate.Confidence,
                    Reason: candidate.Reason
                ));
            }

            if (!string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
                entry.MatchedPullRequestConfidence.HasValue &&
                entry.MatchedPullRequestConfidence.Value >= threshold) {
                var (kind, pullRequestNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
                if (kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && pullRequestNumber > 0) {
                    AddIssueCandidate(issueToCandidates, entry.Number, new RelatedPullRequestCandidate(
                        Number: pullRequestNumber,
                        Url: entry.MatchedPullRequestUrl,
                        Confidence: entry.MatchedPullRequestConfidence.Value,
                        Reason: "issue-side matched pull request"
                    ));
                }
            }
        }

        var comments = new Dictionary<int, string>();
        foreach (var issueCandidates in issueToCandidates) {
            var candidates = issueCandidates.Value.Values.ToList();
            var comment = BuildIssueBacklinkSuggestionComment(
                issueCandidates.Key,
                candidates,
                threshold,
                maxPullRequestsPerIssue);
            if (!string.IsNullOrWhiteSpace(comment)) {
                comments[issueCandidates.Key] = comment;
            }
        }

        return comments;
    }

    internal static string? BuildIssueBacklinkSuggestionComment(
        int issueNumber,
        IReadOnlyList<RelatedPullRequestCandidate> candidates,
        double minConfidence,
        int maxPullRequests) {
        if (issueNumber <= 0 || candidates.Count == 0) {
            return null;
        }

        var limit = Math.Max(1, Math.Min(maxPullRequests, 10));
        var filtered = candidates
            .Where(candidate => candidate.Number > 0 &&
                                candidate.Confidence >= minConfidence &&
                                !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (filtered.Count == 0) {
            return null;
        }

        var lines = new List<string> {
            IssueSuggestionCommentManager.IssueBacklinkCommentMarker,
            $"### IntelligenceX Related Pull Request Suggestions for #{issueNumber.ToString(CultureInfo.InvariantCulture)}",
            string.Empty,
            $"Automated PR candidates (confidence >= {minConfidence.ToString("0.00", CultureInfo.InvariantCulture)}). Please verify before linking/closing.",
            string.Empty
        };

        foreach (var candidate in filtered) {
            var reason = NormalizeCommentReason(candidate.Reason);
            lines.Add($"- PR #{candidate.Number} ({candidate.Url}) - confidence {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} - {reason}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static string? MapVisionLabel(string? visionFit) {
        return visionFit?.ToLowerInvariant() switch {
            "aligned" => "ix/vision:aligned",
            "needs-human-review" => "ix/vision:needs-review",
            "likely-out-of-scope" => "ix/vision:out-of-scope",
            _ => null
        };
    }

    private static string? MapSuggestedDecisionLabel(string? suggestedDecision) {
        return suggestedDecision?.ToLowerInvariant() switch {
            "accept" => "ix/decision:accept",
            "defer" => "ix/decision:defer",
            "reject" => "ix/decision:reject",
            "merge-candidate" => "ix/decision:merge-candidate",
            _ => null
        };
    }

    private static bool TryResolveOptionId(ProjectV2Client.ProjectField field, string optionName, out string optionId) {
        optionId = string.Empty;
        foreach (var option in field.OptionsByName) {
            if (option.Key.Equals(optionName, StringComparison.OrdinalIgnoreCase)) {
                optionId = option.Value;
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<RelatedIssueCandidate> ParseRelatedIssueCandidates(JsonElement item) {
        if (!TryGetProperty(item, "relatedIssues", out var relatedProp) || relatedProp.ValueKind != JsonValueKind.Array) {
            return Array.Empty<RelatedIssueCandidate>();
        }

        var results = new List<RelatedIssueCandidate>();
        foreach (var related in relatedProp.EnumerateArray()) {
            var number = ReadInt(related, "number");
            var url = ReadString(related, "url");
            var confidence = ReadNullableDouble(related, "confidence");
            var reason = ReadNullableString(related, "reason") ?? string.Empty;
            if (number <= 0 || string.IsNullOrWhiteSpace(url) || !confidence.HasValue) {
                continue;
            }

            results.Add(new RelatedIssueCandidate(
                Number: number,
                Url: url,
                Confidence: Math.Round(Math.Clamp(confidence.Value, 0.0, 1.0), 4, MidpointRounding.AwayFromZero),
                Reason: reason
            ));
        }

        return results
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<RelatedPullRequestCandidate> ParseRelatedPullRequestCandidates(JsonElement item) {
        if (!TryGetProperty(item, "relatedPullRequests", out var relatedProp) || relatedProp.ValueKind != JsonValueKind.Array) {
            return Array.Empty<RelatedPullRequestCandidate>();
        }

        var results = new List<RelatedPullRequestCandidate>();
        foreach (var related in relatedProp.EnumerateArray()) {
            var number = ReadInt(related, "number");
            var url = ReadString(related, "url");
            var confidence = ReadNullableDouble(related, "confidence");
            var reason = ReadNullableString(related, "reason") ?? string.Empty;
            if (number <= 0 || string.IsNullOrWhiteSpace(url) || !confidence.HasValue) {
                continue;
            }

            results.Add(new RelatedPullRequestCandidate(
                Number: number,
                Url: url,
                Confidence: Math.Round(Math.Clamp(confidence.Value, 0.0, 1.0), 4, MidpointRounding.AwayFromZero),
                Reason: reason
            ));
        }

        return results
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(10)
            .ToList();
    }

    private static string NormalizeCommentReason(string reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return "token similarity";
        }

        var compact = string.Join(" ", reason
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (compact.Length <= 140) {
            return compact;
        }
        return compact[..137] + "...";
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (obj.TryGetProperty(name, out value)) {
            return true;
        }

        foreach (var property in obj.EnumerateObject()) {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static string? ReadNullableStringCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.String) {
            return null;
        }
        return prop.GetString();
    }

    private static bool? ReadNullableBoolCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False) {
            return null;
        }
        return prop.GetBoolean();
    }

    private static double? ReadNullableDoubleCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetDouble(out var value)) {
            return null;
        }
        return value;
    }

    private static string? ReadNullableString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.String) {
            return null;
        }
        return prop.GetString();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var element in prop.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.String) {
                continue;
            }

            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value)) {
                values.Add(value);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, double> ReadStringDoubleMap(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Object) {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in prop.EnumerateObject()) {
            var key = property.Name?.Trim();
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetDouble(out var confidence)) {
                continue;
            }

            values[key] = Math.Clamp(confidence, 0, 1);
        }

        return values;
    }

    private static int ReadInt(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static double? ReadNullableDouble(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetDouble(out var value)) {
            return null;
        }
        return value;
    }

    private static (string Kind, int Number) ParseKindAndNumberFromUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return ("pull_request", 0);
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 4 &&
                int.TryParse(segments[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                number > 0) {
                var kindSegment = segments[^2];
                if (kindSegment.Equals("issues", StringComparison.OrdinalIgnoreCase)) {
                    return ("issue", number);
                }
                if (kindSegment.Equals("pull", StringComparison.OrdinalIgnoreCase) ||
                    kindSegment.Equals("pulls", StringComparison.OrdinalIgnoreCase)) {
                    return ("pull_request", number);
                }
            }
        }

        return ("pull_request", 0);
    }
}
