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
    private const double DefaultIssueCommentMinConfidence = 0.55;
    private const double RejectVisionConfidenceThreshold = 0.70;
    private const double AcceptVisionConfidenceThreshold = 0.68;
    private const double MergeCandidateScoreThreshold = 82.0;

    internal sealed record RelatedIssueCandidate(
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
        IReadOnlyList<RelatedIssueCandidate>? RelatedIssues = null,
        string? SuggestedDecision = null
    );

    private sealed class Options {
        public string? Owner { get; set; }
        public int? ProjectNumber { get; set; }
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public string ConfigPath { get; set; } = Path.Combine("artifacts", "triage", "ix-project-config.json");
        public string TriagePath { get; set; } = Path.Combine("artifacts", "triage", "ix-triage-index.json");
        public string VisionPath { get; set; } = Path.Combine("artifacts", "triage", "ix-vision-check.json");
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
            entries = LoadEntries(options.TriagePath, options.VisionPath, options.MaxItems);
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
                await RepositoryLabelManager.EnsureLabelsAsync(options.Repo, ProjectLabelCatalog.DefaultLabels).ConfigureAwait(false);
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
        var commentUpserts = 0;

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
                        if (await RepositoryLabelManager.AddLabelsAsync(options.Repo, entry.Kind, entry.Number, labels).ConfigureAwait(false)) {
                            labeled++;
                        }
                    }
                }
            }

            if (options.ApplyLinkComments && entry.Number > 0 &&
                entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
                var comment = BuildIssueMatchSuggestionComment(
                    entry,
                    options.LinkCommentMinConfidence,
                    options.LinkCommentMaxIssues);
                if (!string.IsNullOrWhiteSpace(comment)) {
                    if (options.DryRun) {
                        commentUpserts++;
                    } else if (await IssueSuggestionCommentManager.UpsertAsync(options.Repo, entry.Number, comment)
                                   .ConfigureAwait(false)) {
                        commentUpserts++;
                    }
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
            Console.WriteLine($"PR suggestion comments upserted: {commentUpserts}");
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
        Console.WriteLine("  --max-items <n>          Max entries to sync (1-5000, default: 500)");
        Console.WriteLine("  --project-item-scan-limit <n>  Existing project item scan limit (100-10000, default: 5000)");
        Console.WriteLine("  --ensure-fields          Ensure IX fields exist before sync (default)");
        Console.WriteLine("  --no-ensure-fields       Skip field creation");
        Console.WriteLine("  --apply-labels           Apply IX labels to PRs/issues from synced signals");
        Console.WriteLine("  --ensure-labels          Ensure IX labels exist before applying labels (default)");
        Console.WriteLine("  --no-ensure-labels       Skip label ensure step");
        Console.WriteLine("  --apply-link-comments    Upsert one marker comment on PRs with related issue suggestions");
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

    private static List<ProjectSyncEntry> LoadEntries(string triagePath, string visionPath, int maxItems) {
        using var triageDoc = JsonDocument.Parse(File.ReadAllText(triagePath));
        JsonDocument? visionDoc = null;
        if (File.Exists(visionPath)) {
            visionDoc = JsonDocument.Parse(File.ReadAllText(visionPath));
        }

        var entries = BuildEntriesFromDocuments(triageDoc.RootElement, visionDoc?.RootElement, maxItems);
        visionDoc?.Dispose();
        return entries;
    }

    internal static List<ProjectSyncEntry> BuildEntriesFromDocuments(JsonElement triageRoot, JsonElement? visionRoot, int maxItems) {
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
                var tags = ReadStringArray(item, "tags");
                var matchedIssueUrl = ReadNullableString(item, "matchedIssueUrl");
                var matchedIssueConfidence = ReadNullableDouble(item, "matchedIssueConfidence");
                var relatedIssues = ParseRelatedIssueCandidates(item);
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
                    VisionFit: null,
                    VisionConfidence: null,
                    RelatedIssues: relatedIssues,
                    SuggestedDecision: null
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
                        SuggestedDecision: null
                    );
                }
            }
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

        if (fields.TryGetValue("Tags", out var tagsField) && entry.Tags.Count > 0) {
            await client.SetTextFieldAsync(projectId, itemId, tagsField.Id, string.Join(", ", entry.Tags)).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Matched Issue", out var matchedIssueField) && !string.IsNullOrWhiteSpace(entry.MatchedIssueUrl)) {
            await client.SetTextFieldAsync(projectId, itemId, matchedIssueField.Id, entry.MatchedIssueUrl).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Matched Issue Confidence", out var matchedIssueConfidenceField) && entry.MatchedIssueConfidence.HasValue) {
            await client.SetNumberFieldAsync(projectId, itemId, matchedIssueConfidenceField.Id, entry.MatchedIssueConfidence.Value).ConfigureAwait(false);
            updated++;
        }

        if (fields.TryGetValue("Related Issues", out var relatedIssuesField)) {
            var relatedIssuesValue = BuildRelatedIssuesFieldValue(entry, maxIssues: 3);
            if (!string.IsNullOrWhiteSpace(relatedIssuesValue)) {
                await client.SetTextFieldAsync(projectId, itemId, relatedIssuesField.Id, relatedIssuesValue).ConfigureAwait(false);
                updated++;
            }
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

        if (!string.IsNullOrWhiteSpace(entry.Category)) {
            labels.Add($"ix/category:{entry.Category}");
        }

        foreach (var tag in entry.Tags) {
            if (ProjectLabelCatalog.TryMapTagLabel(tag, out var tagLabel)) {
                labels.Add(tagLabel);
            }
        }

        var visionLabel = MapVisionLabel(entry.VisionFit);
        if (!string.IsNullOrWhiteSpace(visionLabel) && entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            labels.Add(visionLabel);
        }

        if (!string.IsNullOrWhiteSpace(entry.MatchedIssueUrl) && entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            if (entry.MatchedIssueConfidence.HasValue &&
                entry.MatchedIssueConfidence.Value >= HighConfidenceIssueMatchLabelThreshold) {
                labels.Add("ix/match:linked-issue");
            } else {
                labels.Add("ix/match:needs-review");
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.DuplicateCluster)) {
            labels.Add("ix/duplicate:clustered");
        }

        return labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    internal static string? BuildIssueMatchSuggestionComment(ProjectSyncEntry entry, double minConfidence, int maxIssues) {
        if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
            return null;
        }

        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var limit = Math.Max(1, Math.Min(maxIssues, 10));
        var related = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
            .Where(candidate => candidate.Confidence >= threshold && !string.IsNullOrWhiteSpace(candidate.Url))
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

    private static string? MapVisionLabel(string? visionFit) {
        return visionFit?.ToLowerInvariant() switch {
            "aligned" => "ix/vision:aligned",
            "needs-human-review" => "ix/vision:needs-review",
            "likely-out-of-scope" => "ix/vision:out-of-scope",
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

    private static string NormalizeCommentReason(string reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return "token similarity";
        }

        var compact = string.Join(" ", reason
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
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
