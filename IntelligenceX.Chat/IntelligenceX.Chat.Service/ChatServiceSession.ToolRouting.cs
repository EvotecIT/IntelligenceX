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
    private const string DomainIntentActionIdAd = "act_domain_scope_ad";
    private const string DomainIntentActionIdPublic = "act_domain_scope_public";
    private const string DomainIntentAcronymTokenAd = "AD";
    private const string DomainIntentMarker = "ix:domain-intent:v1";
    private const string DomainIntentChoiceMarker = "ix:domain-intent-choice:v1";
    private static readonly string[] DomainIntentAdTechnicalSignals = new[] {
        "dc",
        "ldap",
        "gpo",
        "kerberos",
        "adplayground",
        "ad_domain",
        "act_domain_scope_ad"
    };
    private static readonly string[] DomainIntentPublicTechnicalSignals = new[] {
        "dns",
        "mx",
        "spf",
        "dmarc",
        "dkim",
        "ns",
        "dnsclientx",
        "domaindetective",
        "public_domain",
        "act_domain_scope_public"
    };

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
                Reason: "continuation follow-up reuse"));
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
            return "continuation_subset";
        }

        if (HasPlannerInsight(insights)) {
            return "semantic_planner";
        }

        if (selectedToolCount < totalToolCount) {
            return "weighted_heuristic";
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

        var adCandidates = 0;
        var publicDomainCandidates = 0;
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var family = ResolveDomainIntentFamily(tool);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                adCandidates++;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                publicDomainCandidates++;
            }
        }

        if (adCandidates <= 0 || publicDomainCandidates <= 0) {
            return false;
        }

        var relevantCandidates = adCandidates + publicDomainCandidates;
        if (relevantCandidates < DomainIntentClarificationMinRelevantCandidates) {
            return false;
        }

        var dominantShare = Math.Max(adCandidates, publicDomainCandidates) / (double)relevantCandidates;
        return dominantShare < DomainIntentClarificationMaxDominantShare;
    }

    private static bool HasMixedDomainIntentFamilyCoverage(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions is null || definitions.Count == 0) {
            return false;
        }

        var hasAd = false;
        var hasPublic = false;

        for (var i = 0; i < definitions.Count; i++) {
            var family = ResolveDomainIntentFamily(definitions[i]);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                hasAd = true;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                hasPublic = true;
            }

            if (hasAd && hasPublic) {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldForceDomainIntentClarificationForConflictingSignals(string userRequest, IReadOnlyList<ToolDefinition> allDefinitions) {
        if (!HasMixedDomainIntentFamilyCoverage(allDefinitions)) {
            return false;
        }

        // If an explicit structured family selection is present, do not force clarification.
        if (TryResolveDomainIntentFamilyFromUserSignals(userRequest, out _)) {
            return false;
        }

        return HasConflictingDomainIntentSignals(userRequest)
               || LooksLikeMixedDomainScopeRequest(userRequest);
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

    private static bool IsAdDomainIntentToolName(string toolName) {
        return toolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicDomainIntentToolName(string toolName) {
        return toolName.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDomainIntentFamily(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var category = (definition.Category ?? string.Empty).Trim();
        if (category.Length == 0) {
            category = (ToolSelectionMetadata.Enrich(definition, toolType: null).Category ?? string.Empty).Trim();
        }

        if (string.Equals(category, "active_directory", StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentFamilyAd;
        }

        if (string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentFamilyPublic;
        }

        var toolName = (definition.Name ?? string.Empty).Trim();
        if (IsAdDomainIntentToolName(toolName)) {
            return DomainIntentFamilyAd;
        }

        if (IsPublicDomainIntentToolName(toolName)) {
            return DomainIntentFamilyPublic;
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

        if (IsAdDomainIntentToolName(normalizedToolName)) {
            return DomainIntentFamilyAd;
        }

        if (IsPublicDomainIntentToolName(normalizedToolName)) {
            return DomainIntentFamilyPublic;
        }

        return string.Empty;
    }

    private static string BuildDomainIntentClarificationText() {
        return """
               Domain scope selection required to avoid cross-family tool mixing.
               Any language is accepted. Structured selection is preferred.

               Choose one family:
               1. `ad_domain`
               2. `public_domain`

               Accepted quick replies: `1`, `2`, `ad_domain`, `public_domain`, `AD`, `LDAP`, `DNS`, `MX`, `SPF`, `DMARC`.

               [DomainIntent]
               ix:domain-intent-choice:v1
               option_1: ad_domain
               option_2: public_domain

               [DomainIntent]
               ix:domain-intent:v1
               family: ad_domain|public_domain

               [Action]
               ix:action:v1
               id: act_domain_scope_ad
               title: ad_domain
               request: {"ix_domain_scope":{"family":"ad_domain"}}
               reply: /act act_domain_scope_ad
               mutating: false

               [Action]
               ix:action:v1
               id: act_domain_scope_public
               title: public_domain
               request: {"ix_domain_scope":{"family":"public_domain"}}
               reply: /act act_domain_scope_public
               mutating: false
               """;
    }

    private static string BuildDomainIntentSelectionRoutingHint(string family) {
        var normalizedFamily = TryNormalizeDomainIntentFamily(family, out var parsedFamily)
            ? parsedFamily
            : DomainIntentFamilyAd;
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

        if (!TryParsePendingDomainIntentClarificationSelection(normalizedRequest, out var selectedFamily)) {
            return false;
        }

        RememberSelectedDomainIntentFamily(normalizedThreadId, selectedFamily);
        family = selectedFamily;
        return true;
    }

    private static bool TryParsePendingDomainIntentClarificationSelection(string userRequest, out string family) {
        family = string.Empty;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryParseOrdinalSelection(normalized, out var ordinal)) {
            if (ordinal == 1) {
                family = DomainIntentFamilyAd;
                return true;
            }

            if (ordinal == 2) {
                family = DomainIntentFamilyPublic;
                return true;
            }
        }

        return TryResolveDomainIntentFamilyFromUserSignals(normalized, out family);
    }

    private static bool TryResolveDomainIntentFamilyFromUserSignals(string userRequest, out string family) {
        family = string.Empty;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryParseExplicitActSelection(normalized, out var explicitActionId, out _) && explicitActionId.Length > 0) {
            if (string.Equals(explicitActionId, DomainIntentActionIdAd, StringComparison.OrdinalIgnoreCase)) {
                family = DomainIntentFamilyAd;
                return true;
            }

            if (string.Equals(explicitActionId, DomainIntentActionIdPublic, StringComparison.OrdinalIgnoreCase)) {
                family = DomainIntentFamilyPublic;
                return true;
            }
        }

        if (TryParseDomainIntentFamilyFromActionSelectionPayload(normalized, out family)) {
            return true;
        }

        if (TryParseDomainIntentMarkerSelection(normalized, DomainIntentMarker, out family)) {
            return true;
        }

        if (TryParseDomainIntentChoiceMarkerSelection(normalized, out family)) {
            return true;
        }

        var compact = NormalizeCompactText(normalized);
        if (TryNormalizeDomainIntentFamily(compact, out family)) {
            return true;
        }

        if (TryExtractActionSelectionPayloadJson(normalized, out var payload)
            && TryParseDomainIntentFamilyFromDomainScopePayload(payload, out family)) {
            return true;
        }

        if (TryParseDomainIntentFamilyFromTechnicalSignals(normalized, out family)) {
            return true;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromDomainScopePayload(string payload, out string family) {
        family = string.Empty;
        if (string.IsNullOrWhiteSpace(payload)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryParseDomainIntentFamilyProperty(
                    doc.RootElement,
                    out family,
                    "family",
                    "scope",
                    "domain_family",
                    "domainFamily")) {
                return true;
            }

            if (!TryGetObjectPropertyCaseInsensitive(
                    doc.RootElement,
                    out var scope,
                    "ix_domain_scope",
                    "ixDomainScope",
                    "ix_domain_intent",
                    "ixDomainIntent")
                || scope.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryParseDomainIntentFamilyProperty(
                    scope,
                    out family,
                    "family",
                    "scope",
                    "domain_family",
                    "domainFamily")) {
                return true;
            }

            if (TryGetObjectPropertyCaseInsensitive(scope, out var choiceNode, "choice", "selection")
                && choiceNode.ValueKind == JsonValueKind.Number
                && choiceNode.TryGetInt32(out var choiceNumber)) {
                if (choiceNumber == 1) {
                    family = DomainIntentFamilyAd;
                    return true;
                }

                if (choiceNumber == 2) {
                    family = DomainIntentFamilyPublic;
                    return true;
                }
            }
        } catch (JsonException) {
            return false;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyProperty(JsonElement node, out string family, params string[] names) {
        family = string.Empty;
        if (node.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!TryGetObjectPropertyCaseInsensitive(node, out var familyNode, names)
            || familyNode.ValueKind != JsonValueKind.String) {
            return false;
        }

        return TryNormalizeDomainIntentFamily(familyNode.GetString(), out family);
    }

    private static bool TryNormalizeDomainIntentFamily(string? value, out string family) {
        family = string.Empty;
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (string.Equals(normalized, DomainIntentFamilyAd, StringComparison.OrdinalIgnoreCase)) {
            family = DomainIntentFamilyAd;
            return true;
        }

        if (string.Equals(normalized, DomainIntentFamilyPublic, StringComparison.OrdinalIgnoreCase)) {
            family = DomainIntentFamilyPublic;
            return true;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromTechnicalSignals(string text, out string family) {
        family = string.Empty;
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0) {
            return false;
        }

        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, DomainIntentAdTechnicalSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd)
                           || IsStandaloneDomainSignalAlias(normalized, "ad");
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, DomainIntentPublicTechnicalSignals);
        if (hasAdSignals == hasPublicSignals) {
            return false;
        }

        family = hasAdSignals ? DomainIntentFamilyAd : DomainIntentFamilyPublic;
        return true;
    }

    private static bool HasConflictingDomainIntentSignals(string text) {
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0) {
            return false;
        }

        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, DomainIntentAdTechnicalSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd)
                           || IsStandaloneDomainSignalAlias(normalized, "ad");
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, DomainIntentPublicTechnicalSignals);
        return hasAdSignals && hasPublicSignals;
    }

    private static bool ContainsAnyDomainSignalToken(string text, IReadOnlyList<string> signals) {
        if (signals is null || signals.Count == 0) {
            return false;
        }

        for (var i = 0; i < signals.Count; i++) {
            var signal = (signals[i] ?? string.Empty).Trim();
            if (signal.Length == 0) {
                continue;
            }

            if (ContainsDomainSignalToken(text, signal)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDomainSignalToken(string text, string token) {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedToken = NormalizeDomainSignalTokenValue(token);
        if (normalizedText.Length == 0 || normalizedToken.Length == 0) {
            return false;
        }

        var index = 0;
        while (index < normalizedText.Length) {
            while (index < normalizedText.Length && !IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            if (index >= normalizedText.Length) {
                break;
            }

            var start = index;
            while (index < normalizedText.Length && IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            var length = index - start;
            if (length <= 0) {
                continue;
            }

            var candidate = NormalizeDomainSignalTokenValue(normalizedText.Substring(start, length));
            if (candidate.Length == 0) {
                continue;
            }

            if (string.Equals(candidate, normalizedToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDomainSignalAcronymToken(string text, string acronym) {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedAcronym = (acronym ?? string.Empty).Trim();
        if (normalizedText.Length == 0 || normalizedAcronym.Length == 0) {
            return false;
        }

        var index = 0;
        while (index < normalizedText.Length) {
            while (index < normalizedText.Length && !char.IsLetterOrDigit(normalizedText[index])) {
                index++;
            }

            if (index >= normalizedText.Length) {
                break;
            }

            var start = index;
            while (index < normalizedText.Length && char.IsLetterOrDigit(normalizedText[index])) {
                index++;
            }

            var length = index - start;
            if (length != normalizedAcronym.Length) {
                continue;
            }

            if (string.Compare(normalizedText, start, normalizedAcronym, 0, normalizedAcronym.Length, StringComparison.OrdinalIgnoreCase) != 0) {
                continue;
            }

            var allLettersUpper = true;
            for (var i = 0; i < length; i++) {
                var ch = normalizedText[start + i];
                if (!char.IsLetter(ch)) {
                    continue;
                }

                if (char.IsLower(ch)) {
                    allLettersUpper = false;
                    break;
                }
            }

            if (allLettersUpper) {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainSignalTokenCharacter(char ch) {
        return char.IsLetterOrDigit(ch) || ch is '_' or '-';
    }

    private static bool IsStandaloneDomainSignalAlias(string text, string alias) {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedAlias = NormalizeDomainSignalTokenValue(alias);
        if (normalizedText.Length == 0 || normalizedAlias.Length == 0) {
            return false;
        }

        var index = 0;
        var tokenCount = 0;
        string token = string.Empty;
        while (index < normalizedText.Length) {
            while (index < normalizedText.Length && !IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            if (index >= normalizedText.Length) {
                break;
            }

            var start = index;
            while (index < normalizedText.Length && IsDomainSignalTokenCharacter(normalizedText[index])) {
                index++;
            }

            var length = index - start;
            if (length <= 0) {
                continue;
            }

            tokenCount++;
            if (tokenCount > 1) {
                return false;
            }

            token = NormalizeDomainSignalTokenValue(normalizedText.Substring(start, length));
        }

        return tokenCount == 1
               && string.Equals(token, normalizedAlias, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDomainSignalTokenValue(string value) {
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

    private static bool TryParseDomainIntentChoiceMarkerSelection(string text, out string family) {
        return TryParseDomainIntentMarkerSelection(text, DomainIntentChoiceMarker, out family);
    }

    private static bool TryParseDomainIntentMarkerSelection(string text, string marker, out string family) {
        family = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return false;
        }

        var tail = normalized[(markerIndex + marker.Length)..];
        var lines = tail.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0) {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0) {
                continue;
            }

            if (string.Equals(key, "family", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "scope", StringComparison.OrdinalIgnoreCase)) {
                if (string.Equals(value, DomainIntentFamilyAd, StringComparison.OrdinalIgnoreCase)) {
                    family = DomainIntentFamilyAd;
                    return true;
                }

                if (string.Equals(value, DomainIntentFamilyPublic, StringComparison.OrdinalIgnoreCase)) {
                    family = DomainIntentFamilyPublic;
                    return true;
                }

                continue;
            }

            if (!string.Equals(key, "choice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "selection", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "option", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "index", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!TryParseOrdinalSelection(value, out var ordinalValue)) {
                continue;
            }

            if (ordinalValue == 1) {
                family = DomainIntentFamilyAd;
                return true;
            }

            if (ordinalValue == 2) {
                family = DomainIntentFamilyPublic;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromActionSelectionPayload(string text, out string family) {
        family = string.Empty;
        if (!TryExtractActionSelectionPayloadJson(text, out var payload)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetObjectPropertyCaseInsensitive(
                    doc.RootElement,
                    out var selection,
                    "ix_action_selection",
                    "ixActionSelection",
                    "action_selection",
                    "actionSelection")
                || selection.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryGetObjectPropertyCaseInsensitive(selection, out var idNode, "id", "action_id", "actionId")) {
                if (idNode.ValueKind == JsonValueKind.String) {
                    var id = (idNode.GetString() ?? string.Empty).Trim();
                    if (string.Equals(id, DomainIntentActionIdAd, StringComparison.OrdinalIgnoreCase)) {
                        family = DomainIntentFamilyAd;
                        return true;
                    }

                    if (string.Equals(id, DomainIntentActionIdPublic, StringComparison.OrdinalIgnoreCase)) {
                        family = DomainIntentFamilyPublic;
                        return true;
                    }
                }
            }

            if (TryParseDomainIntentFamilyProperty(
                    selection,
                    out family,
                    "family",
                    "scope",
                    "domain_family",
                    "domainFamily")) {
                return true;
            }

            if (TryGetObjectPropertyCaseInsensitive(selection, out var titleNode, "title")
                && titleNode.ValueKind == JsonValueKind.String) {
                var title = (titleNode.GetString() ?? string.Empty).Trim();
                var normalizedTitle = NormalizeCompactText(title);
                if (TryNormalizeDomainIntentFamily(normalizedTitle, out family)
                    || TryParseDomainIntentFamilyFromTechnicalSignals(normalizedTitle, out family)) {
                    return true;
                }
            }

            if (TryGetObjectPropertyCaseInsensitive(selection, out var requestNode, "request")
                && requestNode.ValueKind != JsonValueKind.Null
                && requestNode.ValueKind != JsonValueKind.Undefined) {
                if (requestNode.ValueKind == JsonValueKind.String) {
                    var nestedRequest = (requestNode.GetString() ?? string.Empty).Trim();
                    if (nestedRequest.Length > 0
                        && !string.Equals(nestedRequest, text, StringComparison.Ordinal)
                        && TryResolveDomainIntentFamilyFromUserSignals(nestedRequest, out family)) {
                        return true;
                    }
                } else if (requestNode.ValueKind == JsonValueKind.Object) {
                    var nestedPayload = requestNode.GetRawText();
                    if (TryParseDomainIntentFamilyFromDomainScopePayload(nestedPayload, out family)
                        || TryParseDomainIntentFamilyProperty(
                            requestNode,
                            out family,
                            "family",
                            "scope",
                            "domain_family",
                            "domainFamily")) {
                        return true;
                    }
                }
            }

            if (TryParseDomainIntentFamilyFromDomainScopePayload(payload, out family)) {
                return true;
            }
        } catch (JsonException) {
            return false;
        }

        return false;
    }

    private bool TryApplyDomainIntentAffinity(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        filteredTools = selectedTools;
        family = string.Empty;
        removedCount = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || selectedTools is null || selectedTools.Count == 0) {
            return false;
        }

        if (!TryGetCurrentDomainIntentFamily(normalizedThreadId, out var preferredFamily)) {
            return false;
        }

        if (!TryFilterToolsByDomainIntentFamily(selectedTools, preferredFamily, out filteredTools, out removedCount)) {
            return false;
        }

        family = preferredFamily;
        return true;
    }

    private bool TryApplyDomainIntentSignalRoutingHint(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        filteredTools = selectedTools;
        family = string.Empty;
        removedCount = 0;

        if (selectedTools is null || selectedTools.Count == 0) {
            return false;
        }

        if (!TryResolveDomainIntentFamilyFromUserSignals(userRequest, out var inferredFamily)) {
            return false;
        }

        if (!TryFilterToolsByDomainIntentFamily(selectedTools, inferredFamily, out var filtered, out removedCount)) {
            return false;
        }

        filteredTools = filtered;
        family = inferredFamily;
        RememberSelectedDomainIntentFamily(threadId, inferredFamily);
        return true;
    }

    private bool TryGetCurrentDomainIntentFamily(string threadId, out string family) {
        family = string.Empty;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_toolRoutingContextLock) {
            if (_domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var cachedFamily)
                && !string.IsNullOrWhiteSpace(cachedFamily)) {
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(normalizedThreadId, out var seenTicks)
                    && TryGetUtcDateTimeFromTicks(seenTicks, out var seenUtc)
                    && nowUtc - seenUtc <= DomainIntentFamilyContextMaxAge) {
                    family = cachedFamily;
                    _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
                    TrimWeightedRoutingContextsNoLock();
                    return true;
                }

                _domainIntentFamilyByThreadId.Remove(normalizedThreadId);
                _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId);
            }
        }

        if (!TryLoadDomainIntentFamilySnapshot(normalizedThreadId, out var snapshotFamily, out _)) {
            return false;
        }

        family = snapshotFamily;
        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = family;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowUtc.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }

        return true;
    }

    private static bool TryFilterToolsByDomainIntentFamily(
        IReadOnlyList<ToolDefinition> selectedTools,
        string preferredFamily,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out int removedCount) {
        filteredTools = selectedTools;
        removedCount = 0;
        if (selectedTools is null || selectedTools.Count == 0 || string.IsNullOrWhiteSpace(preferredFamily)) {
            return false;
        }

        var normalizedFamily = string.Equals(preferredFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)
            ? DomainIntentFamilyPublic
            : DomainIntentFamilyAd;
        var filtered = new List<ToolDefinition>(selectedTools.Count);
        for (var i = 0; i < selectedTools.Count; i++) {
            var tool = selectedTools[i];
            var toolName = (tool.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var candidateFamily = ResolveDomainIntentFamily(tool);
            if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)
                && string.Equals(candidateFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)
                && string.Equals(candidateFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            filtered.Add(tool);
        }

        if (removedCount <= 0 || filtered.Count == 0) {
            return false;
        }

        filteredTools = filtered;
        return true;
    }

    private void RememberPreferredDomainIntentFamily(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            return;
        }

        var toolNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var call = toolCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            toolNameByCallId[callId] = toolName;
        }

        if (toolNameByCallId.Count == 0) {
            return;
        }

        var adVotes = 0;
        var publicVotes = 0;
        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !toolNameByCallId.TryGetValue(callId, out var toolName)) {
                continue;
            }

            if (mutatingToolHintsByName.TryGetValue(toolName, out var mutating) && mutating) {
                continue;
            }

            var success = output.Ok != false
                          && string.IsNullOrWhiteSpace(output.ErrorCode)
                          && string.IsNullOrWhiteSpace(output.Error);
            if (!success) {
                continue;
            }

            var family = ResolveDomainIntentFamily(toolName);
            if (string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)) {
                adVotes++;
            } else if (string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                publicVotes++;
            }
        }

        var totalVotes = adVotes + publicVotes;
        if (totalVotes <= 0) {
            return;
        }

        var dominantVotes = Math.Max(adVotes, publicVotes);
        if (adVotes == publicVotes || dominantVotes / (double)totalVotes < DomainIntentAffinityRetentionMinDominantShare) {
            ClearPreferredDomainIntentFamily(normalizedThreadId);
            return;
        }

        var nextFamily = adVotes > publicVotes ? DomainIntentFamilyAd : DomainIntentFamilyPublic;
        RememberSelectedDomainIntentFamily(normalizedThreadId, nextFamily);
    }

    private void RememberSelectedDomainIntentFamily(string threadId, string family) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !IsSupportedDomainIntentFamily(normalizedFamily)) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            _domainIntentFamilyByThreadId[normalizedThreadId] = normalizedFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = nowTicks;
            TrimWeightedRoutingContextsNoLock();
        }

        RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
        PersistDomainIntentFamilySnapshot(normalizedThreadId, normalizedFamily, nowTicks);
    }

    private void ClearPreferredDomainIntentFamily(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var removed = false;
        var removedClarification = false;
        lock (_toolRoutingContextLock) {
            removedClarification = _pendingDomainIntentClarificationSeenUtcTicks.Remove(normalizedThreadId);
            removed = _domainIntentFamilyByThreadId.Remove(normalizedThreadId) || removed;
            removed = _domainIntentFamilySeenUtcTicks.Remove(normalizedThreadId) || removed;
            if (removed || removedClarification) {
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (removed) {
            RemoveDomainIntentFamilySnapshot(normalizedThreadId);
        }
        if (removedClarification) {
            RemovePendingDomainIntentClarificationSnapshot(normalizedThreadId);
        }
    }

    private static string BuildRoutingMetaPayload(
        string strategy,
        bool weightedToolRouting,
        bool executionContractApplies,
        bool usedContinuationSubset,
        int selectedToolCount,
        int totalToolCount,
        int insightCount,
        bool plannerInsightsDetected,
        int? requestedMaxCandidateTools,
        int? effectiveMaxCandidateTools,
        long? effectiveContextLength,
        bool contextAwareBudgetApplied) {
        var (selected, total) = NormalizeRoutingToolCounts(selectedToolCount, totalToolCount);
        var normalizedContextLength = effectiveContextLength is > 0 ? effectiveContextLength : null;
        return JsonSerializer.Serialize(new {
            strategy = (strategy ?? string.Empty).Trim(),
            weightedToolRouting,
            executionContractApplies,
            usedContinuationSubset,
            selectedToolCount = selected,
            totalToolCount = total,
            reducedToolSet = selected > 0 && selected < total,
            insightCount = Math.Max(0, insightCount),
            plannerInsightsDetected,
            toolCandidateBudget = new {
                requested = requestedMaxCandidateTools,
                effective = effectiveMaxCandidateTools,
                contextAwareBudgetApplied,
                effectiveModelContextLength = normalizedContextLength
            }
        });
    }

    private static (int SelectedToolCount, int TotalToolCount) NormalizeRoutingToolCounts(int selectedToolCount, int totalToolCount) {
        // Keep emitted count semantics consistent for all consumers.
        var total = Math.Max(0, totalToolCount);
        var selected = Math.Clamp(selectedToolCount, 0, total);
        return (selected, total);
    }

    private static bool HasPlannerInsight(IReadOnlyList<ToolRoutingInsight> insights) {
        if (insights.Count == 0) {
            return false;
        }

        for (var i = 0; i < insights.Count; i++) {
            var reason = insights[i].Reason ?? string.Empty;
            if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRoutingInsightStrategy(ToolRoutingInsight insight, string defaultStrategy) {
        var reason = (insight.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) {
            return defaultStrategy;
        }

        if (reason.IndexOf("continuation", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "continuation_subset";
        }

        if (reason.IndexOf("semantic planner", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "semantic_planner";
        }

        return defaultStrategy;
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !LooksLikeContinuationFollowUp(userRequest)) {
            return false;
        }

        string[]? previousNames;
        long seenUtcTicks;
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames);
            seenUtcTicks = _lastWeightedToolSubsetSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (previousNames is null || previousNames.Length == 0) {
            if (!TryLoadWeightedToolSubsetSnapshot(normalizedThreadId, out seenUtcTicks, out var persistedNames)
                || persistedNames.Length == 0) {
                return false;
            }

            previousNames = persistedNames;
            lock (_toolRoutingContextLock) {
                _lastWeightedToolNamesByThreadId[normalizedThreadId] = previousNames;
                _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (!TryGetUtcDateTimeFromTicks(seenUtcTicks, out var seenUtc)
            || seenUtc > DateTime.UtcNow
            || DateTime.UtcNow - seenUtc > UserIntentContextMaxAge) {
            lock (_toolRoutingContextLock) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                TrimWeightedRoutingContextsNoLock();
            }
            RemoveWeightedToolSubsetSnapshot(normalizedThreadId);
            return false;
        }

        var preferred = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ToolDefinition>();
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (preferred.Contains(definition.Name)) {
                selected.Add(definition);
            }
        }

        if (selected.Count < 2) {
            return false;
        }

        var refreshedTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = refreshedTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistWeightedToolSubsetSnapshot(normalizedThreadId, refreshedTicks, previousNames!);

        subset = selected;
        return true;
    }

    private void RememberWeightedToolSubset(string threadId, IReadOnlyList<ToolDefinition> selectedDefinitions, int allToolCount) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        long seenUtcTicks = 0;
        string[] namesSnapshot = Array.Empty<string>();
        var removeSnapshot = false;
        lock (_toolRoutingContextLock) {
            if (selectedDefinitions.Count == 0 || selectedDefinitions.Count >= allToolCount) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                removeSnapshot = true;
            } else {
                var names = new List<string>(selectedDefinitions.Count);
                for (var i = 0; i < selectedDefinitions.Count && i < 64; i++) {
                    var name = (selectedDefinitions[i].Name ?? string.Empty).Trim();
                    if (name.Length > 0) {
                        names.Add(name);
                    }
                }

                namesSnapshot = names.Count == 0 ? Array.Empty<string>() : names.ToArray();
                seenUtcTicks = DateTime.UtcNow.Ticks;
                _lastWeightedToolNamesByThreadId[normalizedThreadId] = namesSnapshot;
                _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (removeSnapshot) {
            RemoveWeightedToolSubsetSnapshot(normalizedThreadId);
            return;
        }

        if (namesSnapshot.Length > 0 && seenUtcTicks > 0) {
            PersistWeightedToolSubsetSnapshot(normalizedThreadId, seenUtcTicks, namesSnapshot);
        }
    }

    private void RememberUserIntent(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || LooksLikeContinuationFollowUp(normalized)) {
            return;
        }

        if (normalized.Length > 600) {
            normalized = normalized.Substring(0, 600);
        }

        var seenUtcTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastUserIntentByThreadId[normalizedThreadId] = normalized;
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = seenUtcTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistUserIntentSnapshot(normalizedThreadId, normalized, seenUtcTicks);
    }

    private string ExpandContinuationUserRequest(string threadId, string userRequest) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return userRequest;
        }

        var raw = userRequest ?? string.Empty;
        if (TryResolvePendingActionSelection(normalizedThreadId, raw, out var resolved)) {
            return resolved;
        }

        var normalized = raw.Trim();
        if (normalized.Length == 0 || !LooksLikeContinuationFollowUp(normalized)) {
            return raw;
        }

        string? intent;
        long intentTicks;
        lock (_toolRoutingContextLock) {
            _lastUserIntentByThreadId.TryGetValue(normalizedThreadId, out intent);
            intentTicks = _lastUserIntentSeenUtcTicks.TryGetValue(normalizedThreadId, out var ticks) ? ticks : 0;
        }

        if (string.IsNullOrWhiteSpace(intent)) {
            if (!TryLoadUserIntentSnapshot(normalizedThreadId, out var persistedIntent, out var persistedTicks)) {
                return raw;
            }

            intent = persistedIntent;
            intentTicks = persistedTicks;
            lock (_toolRoutingContextLock) {
                _lastUserIntentByThreadId[normalizedThreadId] = intent;
                _lastUserIntentSeenUtcTicks[normalizedThreadId] = intentTicks;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (intentTicks > 0) {
            if (intentTicks < DateTime.MinValue.Ticks || intentTicks > DateTime.MaxValue.Ticks) {
                // Defensive: avoid exceptions from ticks->DateTime conversion if ticks are corrupted/out of range.
                lock (_toolRoutingContextLock) {
                    _lastUserIntentByThreadId.Remove(normalizedThreadId);
                    _lastUserIntentSeenUtcTicks.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemoveUserIntentSnapshot(normalizedThreadId);
                return raw;
            }
            var age = DateTime.UtcNow - new DateTime(intentTicks, DateTimeKind.Utc);
            if (age > UserIntentContextMaxAge) {
                lock (_toolRoutingContextLock) {
                    _lastUserIntentByThreadId.Remove(normalizedThreadId);
                    _lastUserIntentSeenUtcTicks.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemoveUserIntentSnapshot(normalizedThreadId);
                return raw;
            }
        }

        var refreshedSeenTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _lastUserIntentSeenUtcTicks[normalizedThreadId] = refreshedSeenTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistUserIntentSnapshot(normalizedThreadId, intent!, refreshedSeenTicks);

        var expanded = $"{intent!.Trim()}\nFollow-up: {normalized}";
        return expanded.Length <= 900 ? expanded : expanded.Substring(0, 900);
    }

    private double ReadToolRoutingAdjustment(string toolName) {
        lock (_toolRoutingStatsLock) {
            if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                return 0d;
            }

            var score = 0d;
            if (stats.Successes > 0) {
                score += Math.Min(2.4d, stats.Successes * 0.2d);
            }
            if (stats.Failures > 0) {
                score -= Math.Min(2.4d, stats.Failures * 0.28d);
            }
            if (stats.LastSuccessUtcTicks > 0) {
                var sinceSuccess = DateTime.UtcNow - new DateTime(stats.LastSuccessUtcTicks, DateTimeKind.Utc);
                if (sinceSuccess <= TimeSpan.FromMinutes(20)) {
                    score += 0.35d;
                }
            }

            return score;
        }
    }

    private void UpdateToolRoutingStats(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name)) {
                continue;
            }

            nameByCallId[call.CallId.Trim()] = call.Name.Trim();
        }

        if (nameByCallId.Count == 0) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingStatsLock) {
            foreach (var output in outputs) {
                var normalizedOutputCallId = (output.CallId ?? string.Empty).Trim();
                if (normalizedOutputCallId.Length == 0 || !nameByCallId.TryGetValue(normalizedOutputCallId, out var toolName)) {
                    continue;
                }

                if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                    stats = new ToolRoutingStats();
                    _toolRoutingStats[toolName] = stats;
                }

                stats.Invocations++;
                stats.LastUsedUtcTicks = nowTicks;
                var success = output.Ok != false
                              && string.IsNullOrWhiteSpace(output.ErrorCode)
                              && string.IsNullOrWhiteSpace(output.Error);
                if (success) {
                    stats.Successes++;
                    stats.LastSuccessUtcTicks = nowTicks;
                } else {
                    stats.Failures++;
                }
            }
            TrimToolRoutingStatsNoLock();
        }
        PersistToolRoutingStatsSnapshot();
    }

    private void TrimWeightedRoutingContextsNoLock() {
        Debug.Assert(Monitor.IsEntered(_toolRoutingContextLock));

        // Weighted-tool-subset, user-intent, pending-action, structured-next-action, planner-thread, and domain-intent contexts share the same
        // key space (active thread id), so trim all when any grows beyond its cap.
        var weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        var intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
        var pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
        var structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
        var plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
        var domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
        var domainClarificationRemoveCount =
            _pendingDomainIntentClarificationSeenUtcTicks.Count - MaxTrackedDomainIntentClarificationContexts;
        var removeCount = Math.Max(
            Math.Max(
                Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                Math.Max(plannerRemoveCount, Math.Max(domainIntentRemoveCount, domainClarificationRemoveCount))),
            0);
        if (removeCount <= 0) {
            return;
        }

        // Defensive: if tick/value maps drift (missing/zero ticks), drop incomplete entries so they don't bias eviction.
        var removedInvalid = false;
        foreach (var threadId in _lastWeightedToolNamesByThreadId.Keys.ToArray()) {
            if (!_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastWeightedToolNamesByThreadId.Remove(threadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _lastUserIntentByThreadId.Keys.ToArray()) {
            if (!_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _lastUserIntentByThreadId.Remove(threadId);
                _lastUserIntentSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys.ToArray()) {
            if (!_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _pendingActionsByThreadId.Remove(threadId);
                _pendingActionsSeenUtcTicks.Remove(threadId);
                _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _structuredNextActionByThreadId.Keys.ToArray()) {
            if (!_structuredNextActionByThreadId.TryGetValue(threadId, out var snapshot) || snapshot.SeenUtcTicks <= 0) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (!TryGetUtcDateTimeFromTicks(snapshot.SeenUtcTicks, out var seenUtc)) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - seenUtc;
            if (age > StructuredNextActionContextMaxAge) {
                _structuredNextActionByThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys.ToArray()) {
            if (!_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var ticks) || ticks <= 0) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (age > PlannerThreadContextMaxAge) {
                _plannerThreadIdByActiveThreadId.Remove(threadId);
                _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys.ToArray()) {
            if (!_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var ticks) || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
                continue;
            }

            if (DateTime.UtcNow - seenUtc > DomainIntentFamilyContextMaxAge) {
                _domainIntentFamilyByThreadId.Remove(threadId);
                _domainIntentFamilySeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        foreach (var threadId in _pendingDomainIntentClarificationSeenUtcTicks.Keys.ToArray()) {
            if (!_pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(threadId, out var ticks)
                || !TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)
                || DateTime.UtcNow - seenUtc > DomainIntentClarificationContextMaxAge) {
                _pendingDomainIntentClarificationSeenUtcTicks.Remove(threadId);
                removedInvalid = true;
            }
        }
        if (removedInvalid) {
            weightedRemoveCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
            intentRemoveCount = _lastUserIntentByThreadId.Count - MaxTrackedUserIntentContexts;
            pendingRemoveCount = _pendingActionsByThreadId.Count - MaxTrackedPendingActionContexts;
            structuredNextActionRemoveCount = _structuredNextActionByThreadId.Count - MaxTrackedStructuredNextActionContexts;
            plannerRemoveCount = _plannerThreadIdByActiveThreadId.Count - MaxTrackedPlannerThreadContexts;
            domainIntentRemoveCount = _domainIntentFamilyByThreadId.Count - MaxTrackedDomainIntentFamilyContexts;
            domainClarificationRemoveCount =
                _pendingDomainIntentClarificationSeenUtcTicks.Count - MaxTrackedDomainIntentClarificationContexts;
            removeCount = Math.Max(
                Math.Max(
                    Math.Max(Math.Max(weightedRemoveCount, intentRemoveCount), Math.Max(pendingRemoveCount, structuredNextActionRemoveCount)),
                    Math.Max(plannerRemoveCount, Math.Max(domainIntentRemoveCount, domainClarificationRemoveCount))),
                0);
            if (removeCount <= 0) {
                return;
            }
        }

        var seenThreadIds = new HashSet<string>(_lastWeightedToolNamesByThreadId.Keys, StringComparer.Ordinal);
        foreach (var threadId in _lastUserIntentByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _pendingActionsByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _structuredNextActionByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _plannerThreadIdByActiveThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _domainIntentFamilyByThreadId.Keys) {
            seenThreadIds.Add(threadId);
        }
        foreach (var threadId in _pendingDomainIntentClarificationSeenUtcTicks.Keys) {
            seenThreadIds.Add(threadId);
        }

        var threadIdsToRemove = seenThreadIds
            .Select(threadId => {
                var ticks = 0L;
                if (_lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var weightedTicks) && weightedTicks > ticks) {
                    ticks = weightedTicks;
                }
                if (_lastUserIntentSeenUtcTicks.TryGetValue(threadId, out var intentTicks) && intentTicks > ticks) {
                    ticks = intentTicks;
                }
                if (_pendingActionsSeenUtcTicks.TryGetValue(threadId, out var actionTicks) && actionTicks > ticks) {
                    ticks = actionTicks;
                }
                if (_structuredNextActionByThreadId.TryGetValue(threadId, out var structuredNextAction)
                    && structuredNextAction.SeenUtcTicks > ticks) {
                    ticks = structuredNextAction.SeenUtcTicks;
                }
                if (_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(threadId, out var plannerTicks) && plannerTicks > ticks) {
                    ticks = plannerTicks;
                }
                if (_domainIntentFamilySeenUtcTicks.TryGetValue(threadId, out var domainIntentTicks) && domainIntentTicks > ticks) {
                    ticks = domainIntentTicks;
                }
                if (_pendingDomainIntentClarificationSeenUtcTicks.TryGetValue(threadId, out var clarificationTicks) && clarificationTicks > ticks) {
                    ticks = clarificationTicks;
                }
                return (ThreadId: threadId, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ThreadId)
            .ToArray();

        foreach (var threadId in threadIdsToRemove) {
            _lastWeightedToolNamesByThreadId.Remove(threadId);
            _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
            _lastUserIntentByThreadId.Remove(threadId);
            _lastUserIntentSeenUtcTicks.Remove(threadId);
            _pendingActionsByThreadId.Remove(threadId);
            _pendingActionsSeenUtcTicks.Remove(threadId);
            _pendingActionsCallToActionTokensByThreadId.Remove(threadId);
            _structuredNextActionByThreadId.Remove(threadId);
            _plannerThreadIdByActiveThreadId.Remove(threadId);
            _plannerThreadSeenUtcTicksByActiveThreadId.Remove(threadId);
            _domainIntentFamilyByThreadId.Remove(threadId);
            _domainIntentFamilySeenUtcTicks.Remove(threadId);
            _pendingDomainIntentClarificationSeenUtcTicks.Remove(threadId);
        }
    }

    private void TrimToolRoutingStatsNoLock() {
        var removeCount = _toolRoutingStats.Count - MaxTrackedToolRoutingStats;
        if (removeCount <= 0) {
            return;
        }

        var toolNamesToRemove = _toolRoutingStats
            .Select(pair => {
                var stats = pair.Value;
                var ticks = stats.LastUsedUtcTicks > 0
                    ? stats.LastUsedUtcTicks
                    : (stats.LastSuccessUtcTicks > 0 ? stats.LastSuccessUtcTicks : long.MinValue);
                return (ToolName: pair.Key, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ToolName, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ToolName)
            .ToArray();

        foreach (var toolName in toolNamesToRemove) {
            _toolRoutingStats.Remove(toolName);
        }
    }

    internal void SetToolRoutingStatsForTesting(IReadOnlyDictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> statsByToolName) {
        ArgumentNullException.ThrowIfNull(statsByToolName);

        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
            foreach (var pair in statsByToolName) {
                var name = (pair.Key ?? string.Empty).Trim();
                if (name.Length == 0) {
                    continue;
                }

                _toolRoutingStats[name] = new ToolRoutingStats {
                    LastUsedUtcTicks = pair.Value.LastUsedUtcTicks,
                    LastSuccessUtcTicks = pair.Value.LastSuccessUtcTicks
                };
            }
        }
    }

    internal void PersistToolRoutingStatsForTesting() {
        PersistToolRoutingStatsSnapshot();
    }

    internal double ReadToolRoutingAdjustmentForTesting(string toolName) {
        return ReadToolRoutingAdjustment(toolName);
    }

    internal void SetWeightedRoutingContextsForTesting(IReadOnlyDictionary<string, string[]> namesByThreadId, IReadOnlyDictionary<string, long> seenTicksByThreadId) {
        ArgumentNullException.ThrowIfNull(namesByThreadId);
        ArgumentNullException.ThrowIfNull(seenTicksByThreadId);

        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();

            foreach (var pair in namesByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0) {
                    continue;
                }

                var names = pair.Value ?? Array.Empty<string>();
                var namesClone = new string[names.Length];
                if (names.Length > 0) {
                    Array.Copy(names, namesClone, names.Length);
                }

                _lastWeightedToolNamesByThreadId[threadId] = namesClone;
            }

            foreach (var pair in seenTicksByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0 || !_lastWeightedToolNamesByThreadId.ContainsKey(threadId)) {
                    continue;
                }

                _lastWeightedToolSubsetSeenUtcTicks[threadId] = pair.Value;
            }
        }
    }

    internal void SetPreferredDomainIntentFamilyForTesting(string threadId, string family, long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedFamily.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _domainIntentFamilyByThreadId[normalizedThreadId] = normalizedFamily;
            _domainIntentFamilySeenUtcTicks[normalizedThreadId] = seenUtcTicks ?? DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    internal string? GetPreferredDomainIntentFamilyForTesting(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return null;
        }

        lock (_toolRoutingContextLock) {
            return _domainIntentFamilyByThreadId.TryGetValue(normalizedThreadId, out var family)
                ? family
                : null;
        }
    }

    internal bool TryGetCurrentDomainIntentFamilyForTesting(string threadId, out string family) {
        return TryGetCurrentDomainIntentFamily(threadId, out family);
    }

    internal bool TryApplyDomainIntentAffinityForTesting(
        string threadId,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentAffinity(threadId, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal bool TryApplyDomainIntentSignalRoutingHintForTesting(
        string threadId,
        string userRequest,
        IReadOnlyList<ToolDefinition> selectedTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        return TryApplyDomainIntentSignalRoutingHint(threadId, userRequest, selectedTools, out filteredTools, out family, out removedCount);
    }

    internal void RememberPreferredDomainIntentFamilyForTesting(
        string threadId,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName) {
        RememberPreferredDomainIntentFamily(threadId, toolCalls, toolOutputs, mutatingToolHintsByName);
    }

    internal static bool HasConflictingDomainIntentSignalsForTesting(string userRequest) {
        return HasConflictingDomainIntentSignals(userRequest);
    }

    internal static bool ShouldForceDomainIntentClarificationForConflictingSignalsForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition> allDefinitions) {
        return ShouldForceDomainIntentClarificationForConflictingSignals(userRequest, allDefinitions);
    }

    internal void RememberPendingDomainIntentClarificationRequestForTesting(string threadId) {
        RememberPendingDomainIntentClarificationRequest(threadId);
    }

    internal bool TryResolvePendingDomainIntentClarificationSelectionForTesting(string threadId, string userRequest, out string family) {
        return TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, out family);
    }

    internal IReadOnlyCollection<string> GetTrackedToolRoutingStatNamesForTesting() {
        lock (_toolRoutingStatsLock) {
            return _toolRoutingStats.Keys.ToArray();
        }
    }

    internal IReadOnlyCollection<string> GetTrackedWeightedRoutingContextThreadIdsForTesting() {
        lock (_toolRoutingContextLock) {
            return _lastWeightedToolNamesByThreadId.Keys.ToArray();
        }
    }

    internal void TrimToolRoutingStatsForTesting() {
        lock (_toolRoutingStatsLock) {
            TrimToolRoutingStatsNoLock();
        }
    }

    internal void TrimWeightedRoutingContextsForTesting() {
        lock (_toolRoutingContextLock) {
            TrimWeightedRoutingContextsNoLock();
        }
    }

}
