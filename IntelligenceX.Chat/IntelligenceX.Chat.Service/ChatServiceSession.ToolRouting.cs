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
        "active_directory",
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
        "dns_client_x",
        "domaindetective",
        "domain_detective",
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
        return toolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("active_directory_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("adplayground_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicDomainIntentToolName(string toolName) {
        return toolName.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("dns_client_x_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase)
               || toolName.StartsWith("domain_detective_", StringComparison.OrdinalIgnoreCase);
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
               [DomainIntent]
               ix:domain-intent-choice:v1
               choice: 1|2
               option_1: ad_domain
               option_2: public_domain

               selection_map:
               1: ad_domain
               2: public_domain

               accepted_input:
               - ordinal: 1|2 (Unicode digits supported)
               - family: ad_domain|public_domain
               - marker: ix:domain-intent:v1
               - action: /act act_domain_scope_ad|/act act_domain_scope_public

               examples:
               - 1
               - ２
               - ١
               - ad_domain
               - public_domain

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

}
