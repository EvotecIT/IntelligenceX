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
}
