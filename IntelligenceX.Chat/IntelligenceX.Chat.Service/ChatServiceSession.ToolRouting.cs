using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int DomainIntentClarificationMinRelevantCandidates = 3;
    private const double DomainIntentClarificationMaxDominantShare = 0.80d;
    private const double DomainIntentAffinityRetentionMinDominantShare = 0.65d;
    private const int DomainIntentAmbiguousDomainTokenMinLength = 4;
    private const int DomainIntentAmbiguousDomainTokenMinLabels = 2;
    private const int DomainIntentAmbiguousDomainTokenMaxCandidates = 16;
    private const string DomainIntentFamilyAd = "ad_domain";
    private const string DomainIntentFamilyPublic = "public_domain";
    private const string DomainIntentAcronymTokenAd = "AD";
    private const string DomainIntentMarker = "ix:domain-intent:v1";
    private const string DomainIntentChoiceMarker = "ix:domain-intent-choice:v1";
    private readonly record struct DomainIntentFamilyAvailability(
        bool HasAd,
        bool HasPublic,
        IReadOnlyList<string>? Families = null) {
        internal bool HasMixedFamilies {
            get {
                if (Families is { Count: > 0 }) {
                    return Families.Count > 1;
                }

                return HasAd && HasPublic;
            }
        }

        internal bool HasFamily(string family) {
            if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                return false;
            }

            if (Families is { Count: > 0 }) {
                for (var i = 0; i < Families.Count; i++) {
                    if (string.Equals(Families[i], normalizedFamily, StringComparison.Ordinal)) {
                        return true;
                    }
                }
            }

            return string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)
                ? HasAd
                : string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal) && HasPublic;
        }

        internal IReadOnlyList<string> GetFamilies() {
            if (Families is { Count: > 0 }) {
                return Families;
            }

            var fallback = new List<string>(2);
            if (HasAd) {
                fallback.Add(DomainIntentFamilyAd);
            }
            if (HasPublic) {
                fallback.Add(DomainIntentFamilyPublic);
            }

            return fallback.Count == 0
                ? Array.Empty<string>()
                : fallback;
        }
    }
    private readonly record struct DomainIntentActionCatalog(
        IReadOnlyDictionary<string, string>? FamilyActionIds = null,
        IReadOnlyDictionary<string, string>? ActionIdFamilies = null) {
        internal bool TryGetActionId(string family, out string actionId) {
            actionId = string.Empty;
            if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                return false;
            }

            if (FamilyActionIds is not null && FamilyActionIds.TryGetValue(normalizedFamily, out var mappedActionId)) {
                var mapped = (mappedActionId ?? string.Empty).Trim();
                if (mapped.Length > 0) {
                    actionId = mapped;
                    return true;
                }
            }
            return false;
        }

        internal bool TryGetFamilyByActionId(string actionId, out string family) {
            family = string.Empty;
            var normalizedActionId = (actionId ?? string.Empty).Trim();
            if (normalizedActionId.Length == 0) {
                return false;
            }

            if (ActionIdFamilies is not null
                && ActionIdFamilies.TryGetValue(normalizedActionId, out var mappedFamily)
                && TryNormalizeDomainIntentFamily(mappedFamily, out family)) {
                return true;
            }

            if (ActionIdFamilies is not null) {
                return false;
            }

            if (FamilyActionIds is not null) {
                foreach (var pair in FamilyActionIds) {
                    if (string.Equals(pair.Value, normalizedActionId, StringComparison.OrdinalIgnoreCase)
                        && TryNormalizeDomainIntentFamily(pair.Key, out family)) {
                        return true;
                    }
                }
            }

            return false;
        }
    }
    private readonly record struct DomainIntentFamilyOption(int Ordinal, string Family, string ActionId);

    private static List<ToolRoutingInsight> BuildContinuationRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs) {
        var list = new List<ToolRoutingInsight>(selectedDefs.Count);
        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = selectedDefs[i].Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            list.Add(new ToolRoutingInsight(
                ToolName: name.Trim(),
                Confidence: "high",
                Score: 1d,
                Reason: "continuation follow-up reuse",
                Strategy: ToolRoutingInsightStrategy.ContinuationSubset));
        }

        return list;
    }

    private async Task EmitRoutingInsightsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolRoutingInsight> insights,
        string routingStrategy, int selectedToolCount, int totalToolCount) {
        if (insights.Count == 0) {
            return;
        }

        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        for (var i = 0; i < insights.Count; i++) {
            var insight = insights[i];
            var insightStrategy = ResolveRoutingInsightStrategy(insight, routingStrategy);
            var payload = JsonSerializer.Serialize(new {
                confidence = insight.Confidence,
                score = insight.Score,
                reason = insight.Reason,
                strategy = insightStrategy,
                rank = i + 1,
                selectedToolCount = selected,
                totalToolCount = total
            });
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.RoutingTool,
                    toolName: insight.ToolName,
                    message: payload)
                .ConfigureAwait(false);
        }
    }

    private static string ResolveRoutingStrategy(
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        IReadOnlyList<ToolRoutingInsight> insights,
        int selectedToolCount,
        int totalToolCount) {
        if (selectedToolCount <= 0 || totalToolCount <= 0) {
            return "no_tools";
        }

        if (!weightedToolRouting) {
            return "disabled";
        }

        if (executionContractApplies && selectedToolCount >= totalToolCount) {
            return "execution_contract_full_set";
        }

        if (usedContinuationSubset) {
            return ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy.ContinuationSubset);
        }

        if (HasPlannerInsight(insights)) {
            return ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy.SemanticPlanner);
        }

        if (selectedToolCount < totalToolCount) {
            return ResolveRoutingInsightStrategyLabel(ToolRoutingInsightStrategy.WeightedHeuristic);
        }

        return "full_toolset";
    }

    private static bool ShouldEmitRoutingTransparency(int selectedToolCount, int totalToolCount) {
        // Contract: always emit routing transparency for any non-negative state so turns remain
        // observable, then normalize counts in payload/message builders for consistency.
        return selectedToolCount >= 0
            && totalToolCount >= 0;
    }

    private static string BuildRoutingSelectionMessage(int selectedToolCount, int totalToolCount, string strategy) {
        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);

        return strategy switch {
            "execution_contract_full_set" =>
                $"Tool routing kept all {selected}/{total} tools for this explicit execution turn.",
            "continuation_subset" =>
                $"Tool routing reused continuation context and selected {selected} of {total} tools for this turn.",
            "semantic_planner" =>
                $"Tool routing used semantic planning and selected {selected} of {total} tools for this turn.",
            "weighted_heuristic" =>
                $"Tool routing used weighted relevance and selected {selected} of {total} tools for this turn.",
            "full_toolset" =>
                $"Tool routing kept the full tool set ({selected}/{total}) for this turn.",
            "disabled" =>
                $"Tool routing is disabled for this turn; using the full tool set ({selected}/{total}).",
            "no_tools" =>
                "No tools are currently available for this turn.",
            _ =>
                $"Tool routing selected {selected} of {total} tools for this turn."
        };
    }

    private static bool ShouldRequestDomainIntentClarification(
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        int selectedToolCount,
        int totalToolCount,
        IReadOnlyList<ToolDefinition> selectedTools) {
        if (!weightedToolRouting || executionContractApplies || usedContinuationSubset) {
            return false;
        }

        if (selectedTools is null) {
            return false;
        }

        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        if (selected <= 0 || total <= 0 || selected >= total || selectedTools.Count == 0) {
            return false;
        }

        var familyCandidateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var family = ResolveDomainIntentFamily(tool);
            if (family.Length == 0) {
                continue;
            }

            familyCandidateCounts[family] = familyCandidateCounts.TryGetValue(family, out var currentCount)
                ? currentCount + 1
                : 1;
        }

        if (familyCandidateCounts.Count < 2) {
            return false;
        }

        var relevantCandidates = familyCandidateCounts.Values.Sum();
        if (relevantCandidates < DomainIntentClarificationMinRelevantCandidates) {
            return false;
        }

        var dominantShare = familyCandidateCounts.Values.Max() / (double)relevantCandidates;
        return dominantShare < DomainIntentClarificationMaxDominantShare;
    }

    private static bool HasMixedDomainIntentFamilyCoverage(IReadOnlyList<ToolDefinition> definitions) {
        return ResolveDomainIntentFamilyAvailability(definitions).HasMixedFamilies;
    }

    private static bool ShouldForceDomainIntentClarificationForConflictingSignals(string userRequest, IReadOnlyList<ToolDefinition> allDefinitions) {
        var availability = ResolveDomainIntentFamilyAvailability(allDefinitions);
        if (!availability.HasMixedFamilies) {
            return false;
        }

        return ShouldForceDomainIntentClarificationForConflictingSignals(userRequest, availability, allDefinitions);
    }

    private static bool ShouldForceDomainIntentClarificationForConflictingSignals(
        string userRequest,
        DomainIntentFamilyAvailability availability) {
        return ShouldForceDomainIntentClarificationForConflictingSignals(userRequest, availability, availableDefinitions: null);
    }

    private static bool ShouldForceDomainIntentClarificationForConflictingSignals(
        string userRequest,
        DomainIntentFamilyAvailability availability,
        IReadOnlyList<ToolDefinition>? availableDefinitions) {
        if (!availability.HasMixedFamilies) {
            return false;
        }

        // If an explicit structured family selection is present, do not force clarification.
        if (TryResolveDomainIntentFamilyFromUserSignals(userRequest, availableDefinitions, out _)) {
            return false;
        }

        return HasConflictingDomainIntentSignals(userRequest, availableDefinitions)
               || LooksLikeMixedDomainScopeRequest(userRequest);
    }

    private static bool ShouldSuppressDomainIntentClarificationForCompactFollowUp(
        bool compactFollowUpTurn,
        bool hasPreferredDomainIntentFamily,
        bool hasFreshPendingActionContext,
        bool conflictingDomainSignals) {
        return compactFollowUpTurn
               && (hasPreferredDomainIntentFamily || hasFreshPendingActionContext)
               && !conflictingDomainSignals;
    }

    private static bool LooksLikeMixedDomainScopeRequest(string text) {
        var domains = ExtractDomainLikeTokens(text);
        if (domains.Count < 2) {
            return false;
        }

        for (var i = 0; i < domains.Count; i++) {
            var left = domains[i];
            for (var j = i + 1; j < domains.Count; j++) {
                var right = domains[j];
                if (IsParentChildDomainPair(left, right) || IsParentChildDomainPair(right, left)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> ExtractDomainLikeTokens(string text) {
        var normalizedText = NormalizeRoutingUserText((text ?? string.Empty).Trim());
        if (normalizedText.Length == 0) {
            return new List<string>(0);
        }

        var domains = new List<string>(DomainIntentAmbiguousDomainTokenMaxCandidates);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokenStart = -1;
        for (var i = 0; i <= normalizedText.Length; i++) {
            var tokenCharacter = false;
            if (i < normalizedText.Length) {
                var ch = normalizedText[i];
                tokenCharacter = char.IsLetterOrDigit(ch) || ch is '.' or '-';
            }

            if (tokenCharacter) {
                if (tokenStart < 0) {
                    tokenStart = i;
                }

                continue;
            }

            if (tokenStart < 0) {
                continue;
            }

            var token = normalizedText.Substring(tokenStart, i - tokenStart).Trim('.');
            tokenStart = -1;
            if (token.Length == 0 || !IsLikelyDomainToken(token) || !seen.Add(token)) {
                continue;
            }

            domains.Add(token);
            if (domains.Count >= DomainIntentAmbiguousDomainTokenMaxCandidates) {
                break;
            }
        }

        return domains;
    }

    private static bool IsLikelyDomainToken(string token) {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length < DomainIntentAmbiguousDomainTokenMinLength
            || normalized.Length > 255
            || normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)) {
            return false;
        }

        var labels = normalized.Split('.');
        if (labels.Length < DomainIntentAmbiguousDomainTokenMinLabels) {
            return false;
        }

        var hasLetter = false;
        for (var i = 0; i < labels.Length; i++) {
            var label = labels[i];
            if (label.Length is < 1 or > 63
                || label.StartsWith("-", StringComparison.Ordinal)
                || label.EndsWith("-", StringComparison.Ordinal)) {
                return false;
            }

            for (var j = 0; j < label.Length; j++) {
                var ch = label[j];
                if (!(char.IsLetterOrDigit(ch) || ch == '-')) {
                    return false;
                }

                if (char.IsLetter(ch)) {
                    hasLetter = true;
                }
            }
        }

        return hasLetter;
    }

    private static bool IsParentChildDomainPair(string child, string parent) {
        var normalizedChild = (child ?? string.Empty).Trim();
        var normalizedParent = (parent ?? string.Empty).Trim();
        if (normalizedChild.Length == 0
            || normalizedParent.Length == 0
            || normalizedChild.Length <= normalizedParent.Length
            || !normalizedChild.EndsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var separatorIndex = normalizedChild.Length - normalizedParent.Length - 1;
        if (separatorIndex < 0 || normalizedChild[separatorIndex] != '.') {
            return false;
        }

        return CountDomainLabels(normalizedChild) > CountDomainLabels(normalizedParent);
    }

    private static int CountDomainLabels(string value) {
        var normalized = (value ?? string.Empty).Trim().Trim('.');
        if (normalized.Length == 0) {
            return 0;
        }

        return normalized.Split('.').Length;
    }

    private static string ResolveDomainIntentFamily(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var routingFamily = (definition.Routing?.DomainIntentFamily ?? string.Empty).Trim();
        if (TryNormalizeDomainIntentFamily(routingFamily, out var normalizedRoutingFamily)) {
            return normalizedRoutingFamily;
        }

        if (ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var family)
            && TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return normalizedFamily;
        }

        return string.Empty;
    }

    private string ResolveDomainIntentFamily(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return string.Empty;
        }

        if (_registry.TryGetDefinition(normalizedToolName, out var definition) && definition is not null) {
            var family = ResolveDomainIntentFamily(definition);
            if (family.Length > 0) {
                return family;
            }
        }

        if (_toolOrchestrationCatalog.TryGetEntry(normalizedToolName, out var catalogEntry)
            && TryNormalizeDomainIntentFamily(catalogEntry.DomainIntentFamily, out var normalizedCatalogFamily)) {
            return normalizedCatalogFamily;
        }

        return string.Empty;
    }

    private static DomainIntentActionCatalog ResolveDomainIntentActionCatalog(IReadOnlyList<ToolDefinition>? definitions) {
        if (definitions is null) {
            return BuildDefaultDomainIntentActionCatalog();
        }
        if (definitions.Count == 0) {
            return default;
        }

        var familyActionCandidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var routingFamily = (definition.Routing?.DomainIntentFamily ?? string.Empty).Trim();
            if (!TryNormalizeDomainIntentFamily(routingFamily, out var normalizedFamily)) {
                continue;
            }

            var actionId = (definition.Routing?.DomainIntentActionId ?? string.Empty).Trim();
            if (actionId.Length == 0) {
                continue;
            }

            AddDomainIntentActionCandidate(familyActionCandidates, normalizedFamily, actionId);
        }

        var familyActionIds = BuildCanonicalDomainIntentActionIds(familyActionCandidates);
        var actionIdFamilies = BuildDomainIntentActionIdFamilies(familyActionCandidates);
        return new DomainIntentActionCatalog(familyActionIds, actionIdFamilies);
    }

    private static void AddDomainIntentActionCandidate(
        IDictionary<string, HashSet<string>> familyActionCandidates,
        string family,
        string actionId) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        var normalizedActionId = (actionId ?? string.Empty).Trim();
        if (normalizedFamily.Length == 0 || normalizedActionId.Length == 0) {
            return;
        }

        if (!familyActionCandidates.TryGetValue(normalizedFamily, out var actionIds)) {
            actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            familyActionCandidates[normalizedFamily] = actionIds;
        }

        actionIds.Add(normalizedActionId);
    }

    private static Dictionary<string, string> BuildCanonicalDomainIntentActionIds(
        IReadOnlyDictionary<string, HashSet<string>> familyActionCandidates) {
        var familyActionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in familyActionCandidates) {
            var actionId = ResolveCanonicalDomainIntentActionId(pair.Key, pair.Value);
            if (actionId.Length > 0) {
                familyActionIds[pair.Key] = actionId;
            }
        }

        return familyActionIds;
    }

    private static string ResolveCanonicalDomainIntentActionId(string family, IEnumerable<string> actionIds) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedFamily.Length == 0 || actionIds is null) {
            return string.Empty;
        }

        var orderedActionIds = actionIds
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(static candidate => candidate.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate, StringComparer.Ordinal)
            .ToArray();
        if (orderedActionIds.Length == 0) {
            return string.Empty;
        }

        var defaultActionId = ToolSelectionMetadata.GetDefaultDomainIntentActionId(normalizedFamily);
        if (!string.IsNullOrWhiteSpace(defaultActionId)) {
            for (var i = 0; i < orderedActionIds.Length; i++) {
                if (string.Equals(orderedActionIds[i], defaultActionId, StringComparison.OrdinalIgnoreCase)) {
                    return orderedActionIds[i];
                }
            }
        }

        return orderedActionIds[0];
    }

    private static Dictionary<string, string> BuildDomainIntentActionIdFamilies(
        IReadOnlyDictionary<string, HashSet<string>> familyActionCandidates) {
        var actionIdFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousActionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedFamilies = familyActionCandidates.Keys
            .Where(static family => !string.IsNullOrWhiteSpace(family))
            .OrderBy(static family => family, StringComparer.Ordinal)
            .ToArray();
        for (var familyIndex = 0; familyIndex < orderedFamilies.Length; familyIndex++) {
            var family = orderedFamilies[familyIndex];
            if (!familyActionCandidates.TryGetValue(family, out var actionIds) || actionIds is null || actionIds.Count == 0) {
                continue;
            }

            var orderedActionIds = actionIds
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(static candidate => candidate.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static candidate => candidate, StringComparer.Ordinal)
                .ToArray();
            for (var actionIndex = 0; actionIndex < orderedActionIds.Length; actionIndex++) {
                var actionId = orderedActionIds[actionIndex];
                if (ambiguousActionIds.Contains(actionId)) {
                    continue;
                }

                if (actionIdFamilies.TryGetValue(actionId, out var mappedFamily)
                    && !string.Equals(mappedFamily, family, StringComparison.Ordinal)) {
                    actionIdFamilies.Remove(actionId);
                    ambiguousActionIds.Add(actionId);
                    continue;
                }

                actionIdFamilies[actionId] = family;
            }
        }

        return actionIdFamilies;
    }

    private static DomainIntentActionCatalog BuildDefaultDomainIntentActionCatalog() {
        var adActionId = ToolSelectionMetadata.GetDefaultDomainIntentActionId(DomainIntentFamilyAd);
        var publicActionId = ToolSelectionMetadata.GetDefaultDomainIntentActionId(DomainIntentFamilyPublic);
        var actionIdFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(adActionId)) {
            actionIdFamilies[adActionId] = DomainIntentFamilyAd;
        }
        if (!string.IsNullOrWhiteSpace(publicActionId)
            && !actionIdFamilies.ContainsKey(publicActionId)) {
            actionIdFamilies[publicActionId] = DomainIntentFamilyPublic;
        }

        return new DomainIntentActionCatalog(
            new Dictionary<string, string>(StringComparer.Ordinal) {
                [DomainIntentFamilyAd] = adActionId,
                [DomainIntentFamilyPublic] = publicActionId
            },
            actionIdFamilies);
    }

    private static IReadOnlyList<DomainIntentFamilyOption> BuildDomainIntentFamilyOptions(
        DomainIntentFamilyAvailability availability,
        DomainIntentActionCatalog actionCatalog) {
        var families = availability.GetFamilies();
        if (families.Count < 2) {
            return Array.Empty<DomainIntentFamilyOption>();
        }

        var orderedFamilies = families
            .Where(static family => !string.IsNullOrWhiteSpace(family))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static family => string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
                    ? 0
                    : string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)
                        ? 1
                        : 2)
            .ThenBy(static family => family, StringComparer.Ordinal)
            .ToArray();
        if (orderedFamilies.Length < 2) {
            return Array.Empty<DomainIntentFamilyOption>();
        }

        var options = new List<DomainIntentFamilyOption>(orderedFamilies.Length);
        for (var i = 0; i < orderedFamilies.Length; i++) {
            var family = orderedFamilies[i];
            if (!actionCatalog.TryGetActionId(family, out var actionId)) {
                continue;
            }

            options.Add(new DomainIntentFamilyOption(
                Ordinal: i + 1,
                Family: family,
                ActionId: actionId));
        }

        return options.Count < 2
            ? Array.Empty<DomainIntentFamilyOption>()
            : options;
    }

    private static string BuildDomainIntentClarificationText() {
        return BuildDomainIntentClarificationText(
            new DomainIntentFamilyAvailability(
                HasAd: true,
                HasPublic: true,
                Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic }),
            BuildDefaultDomainIntentActionCatalog());
    }

    private static string BuildDomainIntentClarificationText(DomainIntentFamilyAvailability availability) {
        return BuildDomainIntentClarificationText(
            availability,
            BuildDefaultDomainIntentActionCatalog());
    }

    private static string BuildDomainIntentClarificationText(
        DomainIntentFamilyAvailability availability,
        DomainIntentActionCatalog actionCatalog) {
        var options = BuildDomainIntentFamilyOptions(availability, actionCatalog);
        if (options.Count < 2) {
            return string.Empty;
        }

        var choices = string.Join('|', options.Select(static option => option.Ordinal.ToString()));
        var families = string.Join('|', options.Select(static option => option.Family));
        var actionReplies = string.Join('|', options.Select(static option => $"/act {option.ActionId}"));

        var sb = new StringBuilder();
        sb.AppendLine("[DomainIntent]");
        sb.AppendLine(DomainIntentChoiceMarker);
        sb.Append("choice: ").AppendLine(choices);
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.Append("option_").Append(option.Ordinal).Append(": ").AppendLine(option.Family);
        }
        sb.AppendLine();
        sb.AppendLine("selection_map:");
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.Append(option.Ordinal).Append(": ").AppendLine(option.Family);
        }
        sb.AppendLine();
        sb.AppendLine("accepted_input:");
        sb.Append("- ordinal: ").Append(choices).AppendLine(" (Unicode digits supported)");
        sb.Append("- family: ").AppendLine(families);
        sb.Append("- marker: ").AppendLine(DomainIntentMarker);
        sb.Append("- action: ").AppendLine(actionReplies);
        sb.AppendLine();
        sb.AppendLine("examples:");
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.Append("- ").AppendLine(option.Ordinal.ToString());
            var unicodeAlternates = BuildUnicodeOrdinalAlternates(option.Ordinal);
            for (var alternateIndex = 0; alternateIndex < unicodeAlternates.Count; alternateIndex++) {
                sb.Append("- ").AppendLine(unicodeAlternates[alternateIndex]);
            }
        }
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.Append("- ").AppendLine(option.Family);
        }
        sb.AppendLine();
        sb.AppendLine("[DomainIntent]");
        sb.AppendLine(DomainIntentMarker);
        sb.Append("family: ").AppendLine(families);
        sb.AppendLine();
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.AppendLine("[Action]");
            sb.AppendLine("ix:action:v1");
            sb.Append("id: ").AppendLine(option.ActionId);
            sb.Append("title: ").AppendLine(option.Family);
            sb.Append("request: {\"ix_domain_scope\":{\"family\":\"").Append(option.Family).AppendLine("\"}}");
            sb.Append("reply: /act ").AppendLine(option.ActionId);
            sb.AppendLine("mutating: false");
            if (i < options.Count - 1) {
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> BuildUnicodeOrdinalAlternates(int ordinal) {
        if (ordinal < 0 || ordinal > 9) {
            return Array.Empty<string>();
        }

        var alternates = new List<string>(2);
        alternates.Add(new string(new[] { (char)('\uFF10' + ordinal) }));
        alternates.Add(new string(new[] { (char)('\u0660' + ordinal) }));
        return alternates;
    }

    private static string BuildDomainIntentClarificationVisibleText() {
        return BuildDomainIntentClarificationVisibleText(
            new DomainIntentFamilyAvailability(
                HasAd: true,
                HasPublic: true,
                Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic }),
            BuildDefaultDomainIntentActionCatalog());
    }

    private static string BuildDomainIntentClarificationVisibleText(DomainIntentFamilyAvailability availability) {
        return BuildDomainIntentClarificationVisibleText(
            availability,
            BuildDefaultDomainIntentActionCatalog());
    }

    private static string BuildDomainIntentClarificationVisibleText(
        DomainIntentFamilyAvailability availability,
        DomainIntentActionCatalog actionCatalog) {
        var options = BuildDomainIntentFamilyOptions(availability, actionCatalog);
        if (options.Count < 2) {
            return string.Empty;
        }

        var ordinalChoices = string.Join(" or ", options.Select(static option => option.Ordinal.ToString()));
        var familyChoices = string.Join(" or ", options.Select(static option => option.Family));
        var actionChoices = string.Join('|', options.Select(static option => $"/act {option.ActionId}"));

        var sb = new StringBuilder();
        sb.AppendLine("I need a quick scope choice before continuing.");
        sb.AppendLine();
        for (var i = 0; i < options.Count; i++) {
            var option = options[i];
            sb.Append(option.Ordinal).Append(". ").AppendLine(BuildDomainIntentFamilyChoiceDescription(option.Family));
        }
        sb.AppendLine();
        sb.AppendLine("Reply with:");
        sb.Append("- ").Append(ordinalChoices).AppendLine(" (Unicode digits supported),");
        sb.Append("- ").Append(familyChoices).AppendLine(",");
        sb.Append("- or ").Append(actionChoices).Append('.');

        return sb.ToString().TrimEnd();
    }

    private static string BuildDomainIntentFamilyChoiceDescription(string family) {
        if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return "AD domain (internal AD checks like replication, LDAP, DC health)";
        }

        if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return "Public domain (external DNS/mail checks like MX/SPF/DMARC)";
        }

        var label = (family ?? string.Empty).Trim();
        if (label.Length == 0) {
            return "Custom domain scope";
        }

        var humanized = label.Replace('_', ' ');
        return $"{humanized} scope";
    }

    private static string BuildDomainIntentSelectionRoutingHint(string family) {
        if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return string.Empty;
        }

        return $$"""
                 ix:domain-intent:v1
                 family: {{normalizedFamily}}
                 """;
    }

    private static string DescribeDomainIntentFamily(string family) {
        return TryNormalizeDomainIntentFamily(family, out var normalizedFamily)
            ? normalizedFamily
            : "unknown";
    }

    private void RememberPendingDomainIntentClarificationRequest(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var seenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _pendingDomainIntentClarificationSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPendingDomainIntentClarificationSnapshot(normalizedThreadId, seenUtcTicks);
    }

    private bool TryResolvePendingDomainIntentClarificationSelection(string threadId, string userRequest, out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, availableDefinitions: null, out family);
    }

    private bool TryResolvePendingDomainIntentClarificationSelection(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var normalizedRequest = (userRequest ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0) {
            return false;
        }

        long clarificationSeenTicks;
        lock (_toolRoutingContextLock) {
            _pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(normalizedThreadId, out clarificationSeenTicks);
        }

        if (clarificationSeenTicks <= 0) {
            if (!TryLoadPendingDomainIntentClarificationSnapshot(normalizedThreadId, out clarificationSeenTicks)) {
                return false;
            }

            lock (_toolRoutingContextLock) {
                _pendingDomainIntentClarificationSeenUtcTicks[normalizedThreadId] = clarificationSeenTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (!TryGetUtcDateTimeFromTicks(clarificationSeenTicks, out var clarificationSeenUtc)
            || clarificationSeenUtc > DateTime.UtcNow
            || DateTime.UtcNow - clarificationSeenUtc > DomainIntentClarificationContextMaxAge) {
            lock (_toolRoutingContextLock) {
                _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            }
            RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
            return false;
        }

        var availability = availableDefinitions is null
            ? new DomainIntentFamilyAvailability(
                HasAd: true,
                HasPublic: true,
                Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic })
            : ResolveDomainIntentFamilyAvailability(availableDefinitions);
        if (!TryParsePendingDomainIntentClarificationSelection(normalizedRequest, availability, availableDefinitions, out var selectedFamily)) {
            return false;
        }

        RememberSelectedDomainIntentFamily(normalizedThreadId, selectedFamily);
        family = selectedFamily;
        return true;
    }

    private static bool TryParsePendingDomainIntentClarificationSelection(string userRequest, out string family) {
        return TryParsePendingDomainIntentClarificationSelectionCore(userRequest, out family);
    }

    private static bool TryParsePendingDomainIntentClarificationSelection(
        string userRequest,
        DomainIntentFamilyAvailability availability,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        if (!TryParsePendingDomainIntentClarificationSelectionCore(userRequest, availableDefinitions, out var selectedFamily)) {
            return false;
        }

        if (!IsDomainIntentFamilyAvailable(availability, selectedFamily)) {
            return false;
        }

        family = selectedFamily;
        return true;
    }

    private static bool TryParsePendingDomainIntentClarificationSelectionCore(string userRequest, out string family) {
        return TryParsePendingDomainIntentClarificationSelectionCore(userRequest, availableDefinitions: null, out family);
    }

    private static bool TryParsePendingDomainIntentClarificationSelectionCore(
        string userRequest,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var availability = availableDefinitions is null
            ? new DomainIntentFamilyAvailability(
                HasAd: true,
                HasPublic: true,
                Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic })
            : ResolveDomainIntentFamilyAvailability(availableDefinitions);
        var actionCatalog = ResolveDomainIntentActionCatalog(availableDefinitions);
        var options = BuildDomainIntentFamilyOptions(availability, actionCatalog);
        if (TryParseOrdinalSelection(normalized, out var ordinal)) {
            for (var i = 0; i < options.Count; i++) {
                var option = options[i];
                if (option.Ordinal == ordinal) {
                    family = option.Family;
                    return true;
                }
            }

            var families = availability.GetFamilies();
            if (families.Count == 1 && ordinal == 1) {
                family = families[0];
                return true;
            }
        }

        return TryResolveDomainIntentFamilyFromUserSignals(normalized, availableDefinitions, out family);
    }

    private static DomainIntentFamilyAvailability ResolveDomainIntentFamilyAvailability(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions is null || definitions.Count == 0) {
            return default;
        }

        var families = new HashSet<string>(StringComparer.Ordinal);
        var hasAd = false;
        var hasPublic = false;
        for (var i = 0; i < definitions.Count; i++) {
            var family = ResolveDomainIntentFamily(definitions[i]);
            if (family.Length == 0) {
                continue;
            }

            families.Add(family);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                hasAd = true;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                hasPublic = true;
            }
        }

        IReadOnlyList<string> orderedFamilies = families.Count == 0
            ? Array.Empty<string>()
            : families
                .OrderBy(static family => string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
                        ? 0
                        : string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)
                            ? 1
                            : 2)
                .ThenBy(static family => family, StringComparer.Ordinal)
                .ToArray();
        return new DomainIntentFamilyAvailability(
            HasAd: hasAd,
            HasPublic: hasPublic,
            Families: orderedFamilies);
    }

    private static bool IsDomainIntentFamilyAvailable(DomainIntentFamilyAvailability availability, string family) {
        return availability.HasFamily(family);
    }

}
