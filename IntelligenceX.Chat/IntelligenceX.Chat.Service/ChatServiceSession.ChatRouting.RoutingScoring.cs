using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int PlannerRemoteCapablePriorityBoost = 300;
    private const int PlannerCrossPackContinuationPriorityBoost = 120;
    private const int PlannerEnvironmentDiscoverPriorityBoost = 160;
    private const int PlannerSetupAwarePriorityBoost = 110;
    private const double WeightedRoutingRemoteCapableScoreBoost = 3.5d;
    private const double WeightedRoutingCrossPackContinuationScoreBoost = 1.75d;
    private const double WeightedRoutingEnvironmentDiscoverScoreBoost = 2.4d;
    private const double WeightedRoutingSetupAwareScoreBoost = 1.65d;

    private IReadOnlyList<ToolDefinition> SelectDeterministicToolSubset(IReadOnlyList<ToolDefinition> definitions, int limit) {
        return SelectDeterministicToolSubset(definitions, limit, _toolOrchestrationCatalog);
    }

    private static IReadOnlyList<ToolDefinition> SelectDeterministicToolSubset(
        IReadOnlyList<ToolDefinition> definitions,
        int limit,
        ToolOrchestrationCatalog toolOrchestrationCatalog) {
        if (definitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var uniqueDefinitions = new List<ToolDefinition>(definitions.Count);
        var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !seenToolNames.Add(name)) {
                continue;
            }

            uniqueDefinitions.Add(definition);
        }

        if (uniqueDefinitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        if (limit >= uniqueDefinitions.Count) {
            return uniqueDefinitions;
        }

        // Deterministic but less registration-order-biased fallback:
        // round-robin one tool per catalog family before filling remaining slots.
        var familyOrder = new List<string>(uniqueDefinitions.Count);
        var toolsByFamily = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < uniqueDefinitions.Count; i++) {
            var definition = uniqueDefinitions[i];
            var family = ResolveDeterministicSubsetFamilyKey(definition, toolOrchestrationCatalog);
            if (!toolsByFamily.TryGetValue(family, out var familyTools)) {
                familyTools = new List<ToolDefinition>();
                toolsByFamily[family] = familyTools;
                familyOrder.Add(family);
            }

            familyTools.Add(definition);
        }

        var orderedToolsByFamily = new Dictionary<string, Queue<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < familyOrder.Count; i++) {
            var family = familyOrder[i];
            if (!toolsByFamily.TryGetValue(family, out var familyTools) || familyTools.Count == 0) {
                continue;
            }

            familyTools.Sort((left, right) => CompareDeterministicSubsetCandidates(left, right, toolOrchestrationCatalog));
            orderedToolsByFamily[family] = new Queue<ToolDefinition>(familyTools);
        }

        var selected = new List<ToolDefinition>(limit);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (selected.Count < limit) {
            var addedInPass = false;
            for (var familyIndex = 0; familyIndex < familyOrder.Count && selected.Count < limit; familyIndex++) {
                var family = familyOrder[familyIndex];
                if (!orderedToolsByFamily.TryGetValue(family, out var queue) || queue.Count == 0) {
                    continue;
                }

                var candidate = queue.Dequeue();
                var candidateName = (candidate.Name ?? string.Empty).Trim();
                if (candidateName.Length == 0 || !selectedNames.Add(candidateName)) {
                    continue;
                }

                selected.Add(candidate);
                addedInPass = true;
            }

            if (!addedInPass) {
                break;
            }
        }

        if (selected.Count < limit) {
            for (var i = 0; i < uniqueDefinitions.Count && selected.Count < limit; i++) {
                var definition = uniqueDefinitions[i];
                var name = (definition.Name ?? string.Empty).Trim();
                if (name.Length == 0 || !selectedNames.Add(name)) {
                    continue;
                }

                selected.Add(definition);
            }
        }

        return selected.Count == 0 ? Array.Empty<ToolDefinition>() : selected;
    }

    private static int CompareDeterministicSubsetCandidates(
        ToolDefinition? left,
        ToolDefinition? right,
        ToolOrchestrationCatalog toolOrchestrationCatalog) {
        var leftPriority = GetDeterministicSubsetPriority(left, toolOrchestrationCatalog);
        var rightPriority = GetDeterministicSubsetPriority(right, toolOrchestrationCatalog);
        var priorityCompare = leftPriority.CompareTo(rightPriority);
        if (priorityCompare != 0) {
            return priorityCompare;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left?.Name, right?.Name);
    }

    private static int GetDeterministicSubsetPriority(ToolDefinition? definition, ToolOrchestrationCatalog toolOrchestrationCatalog) {
        if (definition is null) {
            return 100;
        }

        if (toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            if (entry.IsEnvironmentDiscoverTool) {
                return 0;
            }

            if (entry.IsSetupAware) {
                return 1;
            }

            if (string.Equals(entry.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)
                || entry.SupportsRemoteHostTargeting
                || entry.RemoteHostArguments.Count > 0) {
                return 2;
            }

            if (HasCrossPackHandoff(entry)) {
                return 3;
            }

            return 4;
        }

        if (string.Equals(definition.Routing?.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (definition.Setup?.IsSetupAware == true) {
            return 1;
        }

        var schemaTraits = ToolSchemaTraitProjection.Project(definition);
        if (schemaTraits.SupportsRemoteHostTargeting
            || string.Equals(schemaTraits.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)) {
            return 2;
        }

        return 4;
    }

    private static bool HasCrossPackHandoff(ToolOrchestrationCatalogEntry entry) {
        if (!entry.IsHandoffAware || entry.HandoffEdges.Count == 0) {
            return false;
        }

        var normalizedPackId = ToolPackBootstrap.NormalizePackId(entry.PackId);
        for (var i = 0; i < entry.HandoffEdges.Count; i++) {
            var targetPackId = ToolPackBootstrap.NormalizePackId(entry.HandoffEdges[i].TargetPackId);
            if (targetPackId.Length > 0 && !string.Equals(targetPackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDeterministicSubsetFamilyKey(
        ToolDefinition definition,
        ToolOrchestrationCatalog toolOrchestrationCatalog) {
        var normalized = (definition?.Name ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (toolOrchestrationCatalog.TryGetEntry(normalized, out var catalogEntry)) {
            if (catalogEntry.PackId.Length > 0) {
                return catalogEntry.PackId;
            }

            if (catalogEntry.DomainIntentFamily.Length > 0) {
                return "family|" + catalogEntry.DomainIntentFamily;
            }
        }

        var routingPackId = ToolSelectionMetadata.NormalizePackId(definition?.Routing?.PackId);
        if (routingPackId.Length > 0) {
            return routingPackId;
        }

        if (ToolSelectionMetadata.TryNormalizeDomainIntentFamily(definition?.Routing?.DomainIntentFamily, out var family)
            && family.Length > 0) {
            return "family|" + family;
        }

        // Keep non-contract fallback generic so routing does not depend on tool-name prefixes/suffixes.
        return "unassigned";
    }

    private static List<ToolRoutingInsight> BuildRoutingInsights(
        IReadOnlyList<ToolScore> scored,
        IReadOnlyList<ToolDefinition> selectedDefs,
        WeightedRoutingSelectionDiagnostics selectionDiagnostics) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        var ambiguityReason = BuildWeightedRoutingAmbiguityReason(selectionDiagnostics);
        var emittedAmbiguityReason = false;
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.ExplicitToolMatch) {
                reasons.Add("explicit tool-id match");
            }
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add("token match");
            }
            if (toolScore.FocusTokenHits > 0) {
                reasons.Add("unresolved focus match");
            }
            if (toolScore.RemoteCapableBoost > 0.01d) {
                reasons.Add("remote-capable host targeting");
            }
            if (toolScore.CrossPackContinuationBoost > 0.01d) {
                reasons.Add("cross-pack continuation support");
            }
            if (toolScore.EnvironmentDiscoverBoost > 0.01d) {
                reasons.Add("environment discovery bootstrap");
            }
            if (toolScore.SetupAwareBoost > 0.01d) {
                reasons.Add("setup-aware preflight support");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            if (!emittedAmbiguityReason && ambiguityReason.Length > 0) {
                reasons.Add("ambiguous top-score cluster");
                reasons.Add(ambiguityReason);
                emittedAmbiguityReason = true;
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons),
                Strategy: ToolRoutingInsightStrategy.WeightedHeuristic));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

    private static string BuildWeightedRoutingAmbiguityReason(WeightedRoutingSelectionDiagnostics diagnostics) {
        if (!diagnostics.AmbiguityWidened) {
            return string.Empty;
        }

        return $"{WeightedRoutingAmbiguityMarker} baseline={diagnostics.BaselineMinSelection} effective={diagnostics.EffectiveMinSelection} cluster={diagnostics.AmbiguousClusterSize} second_ratio={diagnostics.SecondScoreRatio:0.###}";
    }

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousToken = string.Empty;
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && (char.IsLetterOrDigit(ch) || ch is '_' or '-');
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = normalized.Substring(tokenStart, i - tokenStart).Normalize(NormalizationForm.FormKC).Trim();
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var lower = token.ToLowerInvariant();
            var allowShortAsciiCandidate = string.Equals(previousToken, "pack", StringComparison.Ordinal);
            if (TryAddRoutingTokenCandidate(tokens, seen, lower, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            var separatorNormalized = NormalizeRoutingSeparatorToken(lower);
            if (TryAddRoutingTokenCandidate(tokens, seen, separatorNormalized, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            var compact = NormalizeCompactToken(lower.AsSpan());
            if (TryAddRoutingTokenCandidate(tokens, seen, compact, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            previousToken = lower;
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static bool TryAddRoutingTokenCandidate(List<string> tokens, HashSet<string> seen, string candidate, int maxTokens, bool allowShortAsciiCandidate) {
        var normalized = (candidate ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var hasNonAscii = false;
        for (var i = 0; i < normalized.Length; i++) {
            if (normalized[i] > 127) {
                hasNonAscii = true;
                break;
            }
        }

        var minLen = hasNonAscii ? 2 : allowShortAsciiCandidate ? 2 : 3;
        if (normalized.Length < minLen || !seen.Add(normalized)) {
            return false;
        }

        tokens.Add(normalized);
        return tokens.Count >= maxTokens;
    }

    private static string NormalizeRoutingSeparatorToken(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                sb.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
                continue;
            }

            if (ch is '_' or '-') {
                if (!previousWasSeparator && sb.Length > 0) {
                    sb.Append('_');
                    previousWasSeparator = true;
                }
            }
        }

        return sb.ToString().Trim('_');
    }

    private string BuildToolRoutingSearchText(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        var orchestrationEntry = TryResolveToolRoutingCatalogEntry(definition);
        sb.Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description)) {
            sb.Append(' ').Append(definition.Description!.Trim());
        }

        if (definition.Tags.Count > 0) {
            for (var i = 0; i < definition.Tags.Count; i++) {
                var tag = (definition.Tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }
                sb.Append(' ').Append(tag);
            }
        }

        if (definition.Aliases.Count > 0) {
            for (var i = 0; i < definition.Aliases.Count; i++) {
                var alias = definition.Aliases[i];
                if (alias is null || string.IsNullOrWhiteSpace(alias.Name)) {
                    continue;
                }
                sb.Append(' ').Append(alias.Name.Trim());
            }
        }

        AppendRoutingPackTokens(sb, definition);
        AppendRoutingRoleTokens(sb, definition);
        AppendRuntimePackMetadataTokens(sb, definition, orchestrationEntry);

        var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 12, out var schemaTraits);
        for (var i = 0; i < schemaArguments.Length; i++) {
            sb.Append(' ').Append(schemaArguments[i]);
        }

        var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 8);
        if (requiredArguments.Length > 0) {
            sb.Append(" required");
            for (var i = 0; i < requiredArguments.Length; i++) {
                sb.Append(' ').Append(requiredArguments[i]);
            }
        }

        var traitSearchAugmentation = ToolSchemaTraitProjection.BuildRoutingSearchAugmentation(schemaTraits);
        if (traitSearchAugmentation.Length > 0) {
            sb.Append(traitSearchAugmentation);
        }

        var contractSearchAugmentation = BuildContractSearchAugmentation(definition);
        if (contractSearchAugmentation.Length > 0) {
            sb.Append(contractSearchAugmentation);
        }

        var orchestrationSearchAugmentation = BuildOrchestrationCatalogSearchAugmentation(orchestrationEntry);
        if (orchestrationSearchAugmentation.Length > 0) {
            sb.Append(orchestrationSearchAugmentation);
        }

        return sb.ToString();
    }

    private ToolOrchestrationCatalogEntry? TryResolveToolRoutingCatalogEntry(ToolDefinition definition) {
        if (definition is null) {
            return null;
        }

        return _toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)
            ? entry
            : null;
    }

    private static string BuildContractSearchAugmentation(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var setupToolName = NormalizeRoutingSearchToken(definition.Setup?.SetupToolName);
        var setupAware = definition.Setup?.IsSetupAware == true;
        var environmentDiscover = string.Equals(definition.Routing?.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
        var recoveryToolNames = ExtractToolRecoveryHelperNames(definition, maxCount: 4);
        var handoffTargets = ExtractToolHandoffTargets(definition, maxCount: 6);
        var setupRequirementIds = ExtractSetupRequirementIds(definition, maxCount: 4);
        var setupRequirementKinds = ExtractSetupRequirementKinds(definition, maxCount: 4);
        var setupHintKeys = ExtractSetupHintKeys(definition, maxCount: 6);
        if (setupToolName.Length == 0
            && !setupAware
            && !environmentDiscover
            && recoveryToolNames.Length == 0
            && handoffTargets.Length == 0
            && setupRequirementIds.Length == 0
            && setupRequirementKinds.Length == 0
            && setupHintKeys.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(224);
        if (environmentDiscover) {
            sb.Append(" environment_discover");
            sb.Append(" environment_discovery");
            sb.Append(" scope_discovery");
            sb.Append(" preflight");
        }

        if (setupAware) {
            sb.Append(" setup_aware");
            sb.Append(" setup_discovery");
            sb.Append(" preflight");
        }

        if (setupToolName.Length > 0) {
            sb.Append(" setup ").Append(setupToolName);
            sb.Append(" setup_tool ").Append(setupToolName);
        }

        for (var i = 0; i < setupRequirementIds.Length; i++) {
            sb.Append(" setup_requirement ").Append(setupRequirementIds[i]);
        }

        for (var i = 0; i < setupRequirementKinds.Length; i++) {
            sb.Append(" setup_kind ").Append(setupRequirementKinds[i]);
        }

        for (var i = 0; i < setupHintKeys.Length; i++) {
            sb.Append(" setup_hint ").Append(setupHintKeys[i]);
        }

        for (var i = 0; i < recoveryToolNames.Length; i++) {
            sb.Append(" recovery ").Append(recoveryToolNames[i]);
            sb.Append(" recovery_tool ").Append(recoveryToolNames[i]);
        }

        for (var i = 0; i < handoffTargets.Length; i++) {
            sb.Append(" handoff ").Append(handoffTargets[i]);
            sb.Append(" handoff_target ").Append(handoffTargets[i]);
        }

        return sb.ToString();
    }

    private static string BuildOrchestrationCatalogSearchAugmentation(ToolOrchestrationCatalogEntry? entry) {
        if (entry is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(192);
        AppendRepresentativeExamples(sb, entry.RepresentativeExamples, maxCount: 4);

        if (entry.IsEnvironmentDiscoverTool) {
            sb.Append(" environment_discover");
        }

        if (entry.IsSetupAware && !string.IsNullOrWhiteSpace(entry.SetupToolName)) {
            sb.Append(" setup_catalog ").Append(entry.SetupToolName.Trim());
        }

        if (entry.IsRecoveryAware && entry.RecoveryToolNames.Count > 0) {
            for (var i = 0; i < entry.RecoveryToolNames.Count && i < 4; i++) {
                var toolName = (entry.RecoveryToolNames[i] ?? string.Empty).Trim();
                if (toolName.Length == 0) {
                    continue;
                }

                sb.Append(" recovery_catalog ").Append(toolName);
            }
        }

        if (entry.IsHandoffAware && entry.HandoffEdges.Count > 0) {
            for (var i = 0; i < entry.HandoffEdges.Count && i < 6; i++) {
                var edge = entry.HandoffEdges[i];
                if (!string.IsNullOrWhiteSpace(edge.TargetPackId)) {
                    sb.Append(" handoff_pack ").Append(edge.TargetPackId.Trim());
                }

                if (!string.IsNullOrWhiteSpace(edge.TargetToolName)) {
                    sb.Append(" handoff_tool ").Append(edge.TargetToolName.Trim());
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendRepresentativeExamples(StringBuilder sb, IReadOnlyList<string>? examples, int maxCount) {
        if (sb is null || examples is not { Count: > 0 } || maxCount <= 0) {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < examples.Count && i < maxCount; i++) {
            var example = (examples[i] ?? string.Empty).Trim();
            if (example.Length == 0 || !seen.Add(example)) {
                continue;
            }

            sb.Append(" example ").Append(example);
        }
    }

    private void AppendRoutingPackTokens(StringBuilder sb, ToolDefinition definition) {
        var packHint = ResolveRoutingPackHint(definition);
        if (packHint.Length == 0) {
            return;
        }

        var appended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AppendPackToken(string value) {
            var token = (value ?? string.Empty).Trim();
            if (token.Length == 0 || !appended.Add(token)) {
                return;
            }

            sb.Append(" pack ").Append(token);
            sb.Append(" pack:").Append(token);
        }

        var packSearchTokens = ResolvePackSearchTokens(packHint);
        foreach (var alias in packSearchTokens) {
            AppendPackToken(alias);
        }
    }

    private void AppendRuntimePackMetadataTokens(
        StringBuilder sb,
        ToolDefinition definition,
        ToolOrchestrationCatalogEntry? orchestrationEntry) {
        if (sb is null || definition is null) {
            return;
        }

        var packId = ResolvePackIdForRoutingSearch(definition, orchestrationEntry);
        if (packId.Length == 0) {
            return;
        }

        if (_packCategoriesById.TryGetValue(packId, out var category)
            && !string.IsNullOrWhiteSpace(category)) {
            var normalizedCategory = category.Trim();
            sb.Append(" category ").Append(normalizedCategory);
            sb.Append(" pack_category ").Append(normalizedCategory);
        }

        if (_packEngineIdsById.TryGetValue(packId, out var engineId)
            && !string.IsNullOrWhiteSpace(engineId)) {
            var normalizedEngineId = engineId.Trim();
            sb.Append(" engine ").Append(normalizedEngineId);
            sb.Append(" engine:").Append(normalizedEngineId);
        }

        if (_packCapabilityTagsById.TryGetValue(packId, out var capabilityTags)
            && capabilityTags is { Length: > 0 }) {
            var appended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < capabilityTags.Length; i++) {
                var capabilityTag = (capabilityTags[i] ?? string.Empty).Trim();
                if (capabilityTag.Length == 0 || !appended.Add(capabilityTag)) {
                    continue;
                }

                sb.Append(" capability ").Append(capabilityTag);
                sb.Append(' ').Append(capabilityTag);
            }
        }
    }

    private string ResolvePackIdForRoutingSearch(ToolDefinition definition, ToolOrchestrationCatalogEntry? orchestrationEntry) {
        if (definition is null) {
            return string.Empty;
        }

        var packId = ResolveRuntimePackId(orchestrationEntry?.PackId);
        if (packId.Length > 0) {
            return packId;
        }

        packId = ResolveRuntimePackId(definition.Routing?.PackId);
        if (packId.Length > 0) {
            return packId;
        }

        return ToolSelectionMetadata.TryResolvePackId(definition, out var inferredPackId)
            ? ResolveRuntimePackId(inferredPackId)
            : string.Empty;
    }

    private IReadOnlyList<string> ResolvePackSearchTokens(string packHint) {
        var normalizedPackId = ResolveRuntimePackId(packHint);
        if (normalizedPackId.Length > 0
            && _packSearchTokensById.TryGetValue(normalizedPackId, out var searchTokens)
            && searchTokens is { Length: > 0 }) {
            return searchTokens;
        }

        return ToolSelectionMetadata.GetPackSearchTokens(packHint);
    }

    private static void AppendRoutingRoleTokens(StringBuilder sb, ToolDefinition definition) {
        var role = (definition.Routing?.Role ?? string.Empty).Trim();
        if (role.Length == 0) {
            return;
        }

        sb.Append(" role ").Append(role);
        sb.Append(" role:").Append(role);
    }

    private static string ResolveRoutingPackHint(ToolDefinition definition) {
        var routingPackId = NormalizePackId(definition.Routing?.PackId);
        if (routingPackId.Length > 0) {
            return routingPackId;
        }

        return string.Empty;
    }

    private static string[] ExtractToolSchemaPropertyNames(ToolDefinition definition, int maxCount, out ToolSchemaTraits traits) {
        return ToolSchemaTraitProjection.ReadPropertyNames(definition, maxCount, out traits);
    }

    private static string[] ExtractToolSchemaRequiredNames(ToolDefinition definition, int maxCount) {
        return ToolSchemaTraitProjection.ReadRequiredNames(definition, maxCount);
    }

    private static string[] ExtractToolRecoveryHelperNames(ToolDefinition definition, int maxCount) {
        if (definition?.Recovery?.RecoveryToolNames is not { Count: > 0 } || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(maxCount, definition.Recovery.RecoveryToolNames.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Recovery.RecoveryToolNames.Count && names.Count < maxCount; i++) {
            var normalized = NormalizeRoutingSearchToken(definition.Recovery.RecoveryToolNames[i]);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            names.Add(normalized);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static string[] ExtractToolHandoffTargets(ToolDefinition definition, int maxCount) {
        if (definition?.Handoff?.OutboundRoutes is not { Count: > 0 } || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var targets = new List<string>(Math.Min(maxCount, definition.Handoff.OutboundRoutes.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Handoff.OutboundRoutes.Count && targets.Count < maxCount; i++) {
            var route = definition.Handoff.OutboundRoutes[i];
            if (route is null) {
                continue;
            }

            var targetPackId = NormalizeRoutingSearchToken(route.TargetPackId);
            var targetToolName = NormalizeRoutingSearchToken(route.TargetToolName);
            var targetRole = NormalizeRoutingSearchToken(route.TargetRole);
            var descriptor = targetPackId.Length > 0 && targetToolName.Length > 0
                ? targetPackId + "/" + targetToolName
                : targetPackId.Length > 0 && targetRole.Length > 0
                    ? targetPackId + "/" + targetRole
                    : targetToolName.Length > 0
                        ? targetToolName
                        : targetPackId;
            if (descriptor.Length == 0 || !seen.Add(descriptor)) {
                continue;
            }

            targets.Add(descriptor);
        }

        return targets.Count == 0 ? Array.Empty<string>() : targets.ToArray();
    }

    private static string[] ExtractSetupRequirementIds(ToolDefinition definition, int maxCount) {
        if (definition?.Setup?.Requirements is not { Count: > 0 } || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>(Math.Min(maxCount, definition.Setup.Requirements.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Setup.Requirements.Count && values.Count < maxCount; i++) {
            var normalized = NormalizeRoutingSearchToken(definition.Setup.Requirements[i]?.RequirementId);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            values.Add(normalized);
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static string[] ExtractSetupRequirementKinds(ToolDefinition definition, int maxCount) {
        if (definition?.Setup?.Requirements is not { Count: > 0 } || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>(Math.Min(maxCount, definition.Setup.Requirements.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Setup.Requirements.Count && values.Count < maxCount; i++) {
            var normalized = NormalizeRoutingSearchToken(definition.Setup.Requirements[i]?.Kind);
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            values.Add(normalized);
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static string[] ExtractSetupHintKeys(ToolDefinition definition, int maxCount) {
        if (maxCount <= 0 || definition?.Setup is null) {
            return Array.Empty<string>();
        }

        var values = new List<string>(Math.Min(maxCount, 8));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AppendValue(string? value) {
            var normalized = NormalizeRoutingSearchToken(value);
            if (normalized.Length == 0 || !seen.Add(normalized) || values.Count >= maxCount) {
                return;
            }

            values.Add(normalized);
        }

        for (var i = 0; i < definition.Setup.SetupHintKeys.Count && values.Count < maxCount; i++) {
            AppendValue(definition.Setup.SetupHintKeys[i]);
        }

        for (var i = 0; i < definition.Setup.Requirements.Count && values.Count < maxCount; i++) {
            var requirement = definition.Setup.Requirements[i];
            if (requirement?.HintKeys is not { Count: > 0 }) {
                continue;
            }

            for (var h = 0; h < requirement.HintKeys.Count && values.Count < maxCount; h++) {
                AppendValue(requirement.HintKeys[h]);
            }
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static string NormalizeRoutingSearchToken(string? value) {
        return (value ?? string.Empty).Trim();
    }

}
