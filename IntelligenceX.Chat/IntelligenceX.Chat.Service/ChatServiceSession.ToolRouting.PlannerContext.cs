using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string PlannerContextMarker = "ix:planner-context:v1";
    private const int MaxPlannerContextPackIds = 8;
    private const int MaxPlannerContextToolNames = 8;
    private const int MaxPlannerContextSkills = 6;
    private const int MaxPlannerContextHandoffTargets = 8;

    private readonly record struct PlannerContextMetadata(
        bool RequiresLiveExecution,
        string MissingLiveEvidence,
        string[] PreferredPackIds,
        string[] PreferredToolNames,
        string[] HandoffTargetPackIds,
        string[] HandoffTargetToolNames,
        string[] MatchingSkills,
        bool AllowCachedEvidenceReuse);

    private string BuildPlannerContextAugmentedRequest(string threadId, string requestText, IReadOnlyList<ToolDefinition> definitions) {
        var normalizedRequest = (requestText ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0
            || normalizedRequest.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
            return normalizedRequest;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint)) {
            return normalizedRequest;
        }

        var preferredPackIds = NormalizeDistinctStrings(
            (checkpoint.PriorAnswerPlanPreferredPackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            MaxPlannerContextPackIds);
        var preferredToolNames = NormalizeDistinctStrings(
            checkpoint.PriorAnswerPlanPreferredToolNames ?? Array.Empty<string>(),
            MaxPlannerContextToolNames);

        var handoffSourceToolNames = NormalizeDistinctStrings(
            (checkpoint.PriorAnswerPlanPreferredToolNames ?? Array.Empty<string>())
            .Concat(checkpoint.RecentToolNames ?? Array.Empty<string>())
            .Concat(checkpoint.CapabilityHealthyToolNames ?? Array.Empty<string>()),
            MaxPlannerContextToolNames);
        var (handoffTargetPackIds, handoffTargetToolNames) = CollectPlannerHandoffTargets(handoffSourceToolNames);

        var matchingSkills = ResolvePlannerMatchingSkills(
            normalizedRequest,
            checkpoint,
            preferredPackIds,
            preferredToolNames,
            handoffTargetPackIds,
            handoffTargetToolNames);

        if (!checkpoint.PriorAnswerPlanRequiresLiveExecution
            && checkpoint.PriorAnswerPlanMissingLiveEvidence.Length == 0
            && preferredPackIds.Length == 0
            && preferredToolNames.Length == 0
            && handoffTargetPackIds.Length == 0
            && handoffTargetToolNames.Length == 0
            && matchingSkills.Length == 0
            && !checkpoint.PriorAnswerPlanAllowCachedEvidenceReuse) {
            return normalizedRequest;
        }

        var builder = new StringBuilder(normalizedRequest.Length + 512);
        builder.AppendLine(normalizedRequest);
        builder.AppendLine();
        builder.AppendLine("[Planner context]");
        builder.AppendLine(PlannerContextMarker);
        builder.Append("requires_live_execution: ")
            .AppendLine(checkpoint.PriorAnswerPlanRequiresLiveExecution ? "true" : "false");
        if (checkpoint.PriorAnswerPlanMissingLiveEvidence.Length > 0) {
            builder.Append("missing_live_evidence: ")
                .AppendLine(checkpoint.PriorAnswerPlanMissingLiveEvidence);
        }

        if (preferredPackIds.Length > 0) {
            builder.Append("preferred_pack_ids: ")
                .AppendLine(string.Join(", ", preferredPackIds));
        }

        if (preferredToolNames.Length > 0) {
            builder.Append("preferred_tool_names: ")
                .AppendLine(string.Join(", ", preferredToolNames));
        }

        if (handoffTargetPackIds.Length > 0) {
            builder.Append("handoff_target_pack_ids: ")
                .AppendLine(string.Join(", ", handoffTargetPackIds));
        }

        if (handoffTargetToolNames.Length > 0) {
            builder.Append("handoff_target_tool_names: ")
                .AppendLine(string.Join(", ", handoffTargetToolNames));
        }

        if (matchingSkills.Length > 0) {
            builder.Append("matching_skills: ")
                .AppendLine(string.Join(", ", matchingSkills));
        }

        builder.Append("allow_cached_evidence_reuse: ")
            .AppendLine(checkpoint.PriorAnswerPlanAllowCachedEvidenceReuse ? "true" : "false");
        return builder.ToString().TrimEnd();
    }

    private (string[] TargetPackIds, string[] TargetToolNames) CollectPlannerHandoffTargets(IReadOnlyList<string> sourceToolNames) {
        if (sourceToolNames is null || sourceToolNames.Count == 0 || _toolOrchestrationCatalog is null) {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var targetPackIds = new List<string>();
        var targetToolNames = new List<string>();
        for (var i = 0; i < sourceToolNames.Count; i++) {
            var sourceToolName = (sourceToolNames[i] ?? string.Empty).Trim();
            if (sourceToolName.Length == 0 || !_toolOrchestrationCatalog.TryGetEntry(sourceToolName, out var entry)) {
                continue;
            }

            for (var e = 0; e < entry.HandoffEdges.Count; e++) {
                var edge = entry.HandoffEdges[e];
                var targetPackId = NormalizePackId(edge.TargetPackId);
                if (targetPackId.Length > 0) {
                    targetPackIds.Add(targetPackId);
                }

                var targetToolName = NormalizeToolNameForAnswerPlan(edge.TargetToolName);
                if (targetToolName.Length > 0) {
                    targetToolNames.Add(targetToolName);
                }
            }
        }

        return (
            NormalizeDistinctStrings(targetPackIds, MaxPlannerContextHandoffTargets),
            NormalizeDistinctStrings(targetToolNames, MaxPlannerContextHandoffTargets));
    }

    private string[] ResolvePlannerMatchingSkills(
        string requestText,
        WorkingMemoryCheckpoint checkpoint,
        IReadOnlyList<string> preferredPackIds,
        IReadOnlyList<string> preferredToolNames,
        IReadOnlyList<string> handoffTargetPackIds,
        IReadOnlyList<string> handoffTargetToolNames) {
        var skillInventory = NormalizeSkillInventoryValues(
            (checkpoint.CapabilitySkills ?? Array.Empty<string>())
            .Concat(_connectedRuntimeSkillInventory ?? Array.Empty<string>()),
            maxItems: 0);
        if (skillInventory.Length == 0) {
            return Array.Empty<string>();
        }

        var requestTokens = new HashSet<string>(
            TokenizeRoutingTokens(ExtractPrimaryUserRequest(requestText), maxTokens: 12),
            StringComparer.OrdinalIgnoreCase);
        AddPlannerTokenSeeds(requestTokens, preferredPackIds);
        AddPlannerTokenSeeds(requestTokens, preferredToolNames);
        AddPlannerTokenSeeds(requestTokens, handoffTargetPackIds);
        AddPlannerTokenSeeds(requestTokens, handoffTargetToolNames);
        if (checkpoint.DomainIntentFamily.Length > 0) {
            requestTokens.Add(checkpoint.DomainIntentFamily);
        }

        var candidates = new List<(string Skill, int Score)>(skillInventory.Length);
        for (var i = 0; i < skillInventory.Length; i++) {
            var skill = (skillInventory[i] ?? string.Empty).Trim();
            if (skill.Length == 0) {
                continue;
            }

            var score = 0;
            score += CountPlannerSkillMatches(skill, preferredPackIds) * 3;
            score += CountPlannerSkillMatches(skill, handoffTargetPackIds) * 2;
            score += CountPlannerSkillMatches(skill, preferredToolNames) * 3;
            score += CountPlannerSkillMatches(skill, handoffTargetToolNames) * 2;
            if (checkpoint.DomainIntentFamily.Length > 0
                && skill.IndexOf(checkpoint.DomainIntentFamily, StringComparison.OrdinalIgnoreCase) >= 0) {
                score += 2;
            }

            foreach (var token in requestTokens) {
                if (token.Length == 0) {
                    continue;
                }

                if (skill.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score++;
                }
            }

            if (score > 0) {
                candidates.Add((skill, score));
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Skill)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPlannerContextSkills)
            .ToArray();
    }

    private static int CountPlannerSkillMatches(string skill, IReadOnlyList<string> seeds) {
        if (seeds is null || seeds.Count == 0) {
            return 0;
        }

        var matches = 0;
        for (var i = 0; i < seeds.Count; i++) {
            var seed = (seeds[i] ?? string.Empty).Trim();
            if (seed.Length == 0) {
                continue;
            }

            if (skill.IndexOf(seed, StringComparison.OrdinalIgnoreCase) >= 0) {
                matches++;
            }
        }

        return matches;
    }

    private static void AddPlannerTokenSeeds(ISet<string> tokens, IReadOnlyList<string> values) {
        if (tokens is null || values is null || values.Count == 0) {
            return;
        }

        for (var i = 0; i < values.Count; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            tokens.Add(value);
            var normalizedTokens = TokenizeRoutingTokens(value, maxTokens: 6);
            for (var t = 0; t < normalizedTokens.Length; t++) {
                tokens.Add(normalizedTokens[t]);
            }
        }
    }

    private static bool TryReadPlannerContextFromRequestText(string requestText, out PlannerContextMetadata context) {
        context = new PlannerContextMetadata(
            RequiresLiveExecution: false,
            MissingLiveEvidence: string.Empty,
            PreferredPackIds: Array.Empty<string>(),
            PreferredToolNames: Array.Empty<string>(),
            HandoffTargetPackIds: Array.Empty<string>(),
            HandoffTargetToolNames: Array.Empty<string>(),
            MatchingSkills: Array.Empty<string>(),
            AllowCachedEvidenceReuse: false);
        var raw = requestText ?? string.Empty;
        if (raw.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        var requiresLiveExecution = false;
        var missingLiveEvidence = string.Empty;
        var preferredPackIds = Array.Empty<string>();
        var preferredToolNames = Array.Empty<string>();
        var handoffTargetPackIds = Array.Empty<string>();
        var handoffTargetToolNames = Array.Empty<string>();
        var matchingSkills = Array.Empty<string>();
        var allowCachedEvidenceReuse = false;
        var sawMarker = false;

        using var reader = new StringReader(raw);
        while (reader.ReadLine() is { } line) {
            var trimmed = line.Trim();
            if (!sawMarker) {
                if (trimmed.IndexOf(PlannerContextMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
                    sawMarker = true;
                }

                continue;
            }

            if (trimmed.Length == 0 || LooksLikeStructuredSectionHeader(trimmed)) {
                break;
            }

            if (TryParseStructuredBooleanLine(trimmed, "requires_live_execution", out var parsedRequiresLiveExecution)) {
                requiresLiveExecution = parsedRequiresLiveExecution;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "allow_cached_evidence_reuse", out var parsedAllowCachedEvidenceReuse)) {
                allowCachedEvidenceReuse = parsedAllowCachedEvidenceReuse;
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "missing_live_evidence", out var missingLiveEvidenceValue)) {
                missingLiveEvidence = NormalizeWorkingMemoryAnswerPlanFocus(missingLiveEvidenceValue.ToString());
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_pack_ids", out var preferredPackIdsValue)) {
                preferredPackIds = NormalizeStructuredMetadataCsv(preferredPackIdsValue, NormalizePackId, MaxPlannerContextPackIds);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_tool_names", out var preferredToolNamesValue)) {
                preferredToolNames = NormalizeStructuredMetadataCsv(
                    preferredToolNamesValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextToolNames);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "handoff_target_pack_ids", out var handoffTargetPackIdsValue)) {
                handoffTargetPackIds = NormalizeStructuredMetadataCsv(
                    handoffTargetPackIdsValue,
                    NormalizePackId,
                    MaxPlannerContextHandoffTargets);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "handoff_target_tool_names", out var handoffTargetToolNamesValue)) {
                handoffTargetToolNames = NormalizeStructuredMetadataCsv(
                    handoffTargetToolNamesValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    MaxPlannerContextHandoffTargets);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "matching_skills", out var matchingSkillsValue)) {
                matchingSkills = NormalizeStructuredMetadataCsv(
                    matchingSkillsValue,
                    static value => NormalizeSkillSnapshotValue(value),
                    MaxPlannerContextSkills);
            }
        }

        context = new PlannerContextMetadata(
            RequiresLiveExecution: requiresLiveExecution,
            MissingLiveEvidence: missingLiveEvidence,
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            HandoffTargetPackIds: handoffTargetPackIds,
            HandoffTargetToolNames: handoffTargetToolNames,
            MatchingSkills: matchingSkills,
            AllowCachedEvidenceReuse: allowCachedEvidenceReuse);
        return sawMarker;
    }
}
