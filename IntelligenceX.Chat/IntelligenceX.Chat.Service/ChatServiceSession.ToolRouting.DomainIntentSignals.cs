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
    private readonly record struct DomainIntentRequestAssessment(
        bool HasResolvedFamily,
        string Family,
        bool HasConflictingSignals,
        string AmbiguousDomainTarget);

    private static bool TryResolveDomainIntentFamilyFromUserSignals(string userRequest, out string family) {
        return TryResolveDomainIntentFamilyFromUserSignals(userRequest, availableDefinitions: null, out family);
    }

    private static bool TryResolveDomainIntentFamilyFromUserSignals(
        string userRequest,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var actionCatalog = ResolveDomainIntentActionCatalog(availableDefinitions);
        if (TryParseExplicitActSelection(normalized, out var explicitActionId, out _) && explicitActionId.Length > 0) {
            if (actionCatalog.TryGetFamilyByActionId(explicitActionId, out family)) {
                return true;
            }

            // When users reply with an explicit /act action id, require a direct catalog match.
            // Do not reinterpret unknown ids via lexical signal fallbacks.
            return false;
        }

        if (TryParseDomainIntentFamilyFromActionSelectionPayload(normalized, availableDefinitions, out family)) {
            return true;
        }

        if (TryParseDomainIntentMarkerSelection(normalized, DomainIntentMarker, availableDefinitions, out family)) {
            return true;
        }

        if (TryParseDomainIntentChoiceMarkerSelection(normalized, availableDefinitions, out family)) {
            return true;
        }

        var compact = NormalizeCompactText(normalized);
        if (TryNormalizeDomainIntentFamily(compact, out var compactFamily)) {
            var availability = availableDefinitions is null
                ? new DomainIntentFamilyAvailability(
                    HasAd: true,
                    HasPublic: true,
                    Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic })
                : ResolveDomainIntentFamilyAvailability(availableDefinitions);
            if (IsDomainIntentFamilyAvailable(availability, compactFamily)) {
                family = compactFamily;
                return true;
            }
        }

        if (TryExtractActionSelectionPayloadJson(normalized, out var payload)
            && TryParseDomainIntentFamilyFromDomainScopePayload(payload, availableDefinitions, out family)) {
            return true;
        }

        if (TryParseDomainIntentFamilyFromTechnicalSignals(normalized, availableDefinitions, out family)) {
            return true;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromDomainScopePayload(string payload, out string family) {
        return TryParseDomainIntentFamilyFromDomainScopePayload(payload, availableDefinitions: null, out family);
    }

    private static bool TryParseDomainIntentFamilyFromDomainScopePayload(
        string payload,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
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
                var availability = availableDefinitions is null
                    ? new DomainIntentFamilyAvailability(
                        HasAd: true,
                        HasPublic: true,
                        Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic })
                    : ResolveDomainIntentFamilyAvailability(availableDefinitions);
                var actionCatalog = ResolveDomainIntentActionCatalog(availableDefinitions);
                var options = BuildDomainIntentFamilyOptions(availability, actionCatalog);
                for (var i = 0; i < options.Count; i++) {
                    var option = options[i];
                    if (option.Ordinal == choiceNumber) {
                        family = option.Family;
                        return true;
                    }
                }
            }
        } catch (JsonException) {
            return false;
        } catch (ArgumentException) {
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
        return ToolSelectionMetadata.TryNormalizeDomainIntentFamily(value, out family);
    }

    private static DomainIntentRequestAssessment AssessDomainIntentRequest(
        string userRequest,
        IReadOnlyList<ToolDefinition>? availableDefinitions) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return new DomainIntentRequestAssessment(
                HasResolvedFamily: false,
                Family: string.Empty,
                HasConflictingSignals: false,
                AmbiguousDomainTarget: string.Empty);
        }

        if (TryResolveDomainIntentFamilyFromUserSignals(normalized, availableDefinitions, out var family)) {
            return new DomainIntentRequestAssessment(
                HasResolvedFamily: true,
                Family: family,
                HasConflictingSignals: false,
                AmbiguousDomainTarget: string.Empty);
        }

        var conflictingSignals = HasConflictingDomainIntentSignals(normalized, availableDefinitions)
                                 || LooksLikeMixedDomainScopeRequest(normalized);
        if (conflictingSignals) {
            return new DomainIntentRequestAssessment(
                HasResolvedFamily: false,
                Family: string.Empty,
                HasConflictingSignals: true,
                AmbiguousDomainTarget: string.Empty);
        }

        var domains = ExtractDomainLikeTokens(normalized);
        var ambiguousDomainTarget = domains.Count == 1 ? domains[0] : string.Empty;
        return new DomainIntentRequestAssessment(
            HasResolvedFamily: false,
            Family: string.Empty,
            HasConflictingSignals: false,
            AmbiguousDomainTarget: ambiguousDomainTarget);
    }

    private static bool TryParseDomainIntentFamilyFromTechnicalSignals(
        string text,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0) {
            return false;
        }

        var lexicon = ResolveDomainIntentSignalLexicon(availableDefinitions);
        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, lexicon.AdSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd);
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, lexicon.PublicSignals);
        if (hasAdSignals == hasPublicSignals) {
            return false;
        }

        family = hasAdSignals ? DomainIntentFamilyAd : DomainIntentFamilyPublic;
        return true;
    }

    private static bool HasConflictingDomainIntentSignals(string text) {
        return HasConflictingDomainIntentSignals(text, availableDefinitions: null);
    }

    private static bool HasConflictingDomainIntentSignals(string text, IReadOnlyList<ToolDefinition>? availableDefinitions) {
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0) {
            return false;
        }

        // This anchored variant is for affinity/preference resets only. We require
        // an explicit domain or tool anchor so generic mixed jargon does not wipe
        // remembered scope before the user identifies the target.
        if (!HasTechnicalDomainSignalAnchor(normalized)) {
            return false;
        }

        var lexicon = ResolveDomainIntentSignalLexicon(availableDefinitions);
        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, lexicon.AdSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd);
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, lexicon.PublicSignals);
        return hasAdSignals && hasPublicSignals;
    }

    private static bool HasMixedTechnicalDomainIntentSignals(string text, IReadOnlyList<ToolDefinition>? availableDefinitions) {
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0) {
            return false;
        }

        // This unanchored variant is intentionally broader: it catches mixed AD vs
        // public technical language early so we can clarify scope before spending
        // additional model/tool turns on an ambiguous route.
        var lexicon = ResolveDomainIntentSignalLexicon(availableDefinitions);
        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, lexicon.AdSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd);
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, lexicon.PublicSignals);
        return hasAdSignals && hasPublicSignals;
    }

    private static bool HasTechnicalDomainSignalAnchor(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return ExtractDomainLikeTokens(normalized).Count > 0
               || ExtractExplicitRequestedToolNames(normalized).Length > 0;
    }

    private static bool TryParseDomainIntentChoiceMarkerSelection(string text, out string family) {
        return TryParseDomainIntentChoiceMarkerSelection(text, availableDefinitions: null, out family);
    }

    private static bool TryParseDomainIntentChoiceMarkerSelection(
        string text,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        return TryParseDomainIntentMarkerSelection(text, DomainIntentChoiceMarker, availableDefinitions, out family);
    }

    private static bool TryParseDomainIntentMarkerSelection(string text, string marker, out string family) {
        return TryParseDomainIntentMarkerSelection(text, marker, availableDefinitions: null, out family);
    }

    private static bool TryParseDomainIntentMarkerSelection(
        string text,
        string marker,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
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
        var mappedFamiliesByOrdinal = new Dictionary<int, string>();
        var selectedValue = string.Empty;
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
                if (TryNormalizeDomainIntentFamily(value, out family)) {
                    return true;
                }

                continue;
            }

            if ((key.StartsWith("option_", StringComparison.OrdinalIgnoreCase) || key.All(char.IsDigit))
                && TryParseOrdinalSelection(key, out var optionOrdinal)
                && TryNormalizeDomainIntentFamily(value, out var optionFamily)) {
                mappedFamiliesByOrdinal[optionOrdinal] = optionFamily;
                continue;
            }

            if (!string.Equals(key, "choice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "selection", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "option", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "index", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            selectedValue = value;
            if (TryNormalizeDomainIntentFamily(value, out family)) {
                return true;
            }
        }

        if (selectedValue.Length == 0 || !TryParseOrdinalSelection(selectedValue, out var selectedOrdinal)) {
            return false;
        }

        if (mappedFamiliesByOrdinal.TryGetValue(selectedOrdinal, out var mappedFamily)
            && TryNormalizeDomainIntentFamily(mappedFamily, out family)) {
            return true;
        }

        if (availableDefinitions is not null) {
            return false;
        }

        if (selectedOrdinal == 1) {
            family = DomainIntentFamilyAd;
            return true;
        }

        if (selectedOrdinal == 2) {
            family = DomainIntentFamilyPublic;
            return true;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromActionSelectionPayload(string text, out string family) {
        return TryParseDomainIntentFamilyFromActionSelectionPayload(text, availableDefinitions: null, out family);
    }

    private static bool TryParseDomainIntentFamilyFromActionSelectionPayload(
        string text,
        IReadOnlyList<ToolDefinition>? availableDefinitions,
        out string family) {
        family = string.Empty;
        var actionCatalog = ResolveDomainIntentActionCatalog(availableDefinitions);
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
                    if (actionCatalog.TryGetFamilyByActionId(id, out family)) {
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
                    || TryParseDomainIntentFamilyFromTechnicalSignals(normalizedTitle, availableDefinitions, out family)) {
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
                        && TryResolveDomainIntentFamilyFromUserSignals(nestedRequest, availableDefinitions, out family)) {
                        return true;
                    }
                } else if (requestNode.ValueKind == JsonValueKind.Object) {
                    var nestedPayload = requestNode.GetRawText();
                    if (TryParseDomainIntentFamilyFromDomainScopePayload(nestedPayload, availableDefinitions, out family)
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

            if (TryParseDomainIntentFamilyFromDomainScopePayload(payload, availableDefinitions, out family)) {
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
        IReadOnlyList<ToolDefinition> fullCandidateTools,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out string family,
        out int removedCount) {
        filteredTools = selectedTools;
        family = string.Empty;
        removedCount = 0;

        if ((selectedTools is null || selectedTools.Count == 0)
            && (fullCandidateTools is null || fullCandidateTools.Count == 0)) {
            return false;
        }

        var assessment = AssessDomainIntentRequest(userRequest, _registry.GetDefinitions());
        if (!assessment.HasResolvedFamily) {
            return false;
        }
        var inferredFamily = assessment.Family;

        if (selectedTools is { Count: > 0 }
            && TryFilterToolsByDomainIntentFamily(selectedTools, inferredFamily, out var selectedFiltered, out removedCount)) {
            filteredTools = selectedFiltered;
            family = inferredFamily;
            RememberSelectedDomainIntentFamily(threadId, inferredFamily);
            return true;
        }

        if (fullCandidateTools is not { Count: > 0 }) {
            return false;
        }

        if (!TryFilterToolsByDomainIntentFamily(fullCandidateTools, inferredFamily, out var fullFiltered, out var fullRemovedCount)) {
            return false;
        }

        filteredTools = fullFiltered;
        family = inferredFamily;
        removedCount = fullRemovedCount;
        RememberSelectedDomainIntentFamily(threadId, inferredFamily);
        return true;
    }

}
