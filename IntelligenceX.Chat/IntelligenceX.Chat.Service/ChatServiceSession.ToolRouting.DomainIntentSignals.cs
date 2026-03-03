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
        }

        if (TryParseDomainIntentFamilyFromActionSelectionPayload(normalized, availableDefinitions, out family)) {
            return true;
        }

        if (TryParseDomainIntentMarkerSelection(normalized, DomainIntentMarker, out family)) {
            return true;
        }

        if (TryParseDomainIntentChoiceMarkerSelection(normalized, out family)) {
            return true;
        }

        var compact = NormalizeCompactText(normalized);
        if (TryNormalizeDomainIntentFamily(compact, out var compactFamily)) {
            var availability = availableDefinitions is { Count: > 0 }
                ? ResolveDomainIntentFamilyAvailability(availableDefinitions)
                : new DomainIntentFamilyAvailability(
                    HasAd: true,
                    HasPublic: true,
                    Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic });
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
                var availability = availableDefinitions is { Count: > 0 }
                    ? ResolveDomainIntentFamilyAvailability(availableDefinitions)
                    : new DomainIntentFamilyAvailability(
                        HasAd: true,
                        HasPublic: true,
                        Families: new[] { DomainIntentFamilyAd, DomainIntentFamilyPublic });
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

        var lexicon = ResolveDomainIntentSignalLexicon(availableDefinitions);
        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, lexicon.AdSignals)
                           || ContainsDomainSignalAcronymToken(normalized, DomainIntentAcronymTokenAd);
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, lexicon.PublicSignals);
        return hasAdSignals && hasPublicSignals;
    }

    private readonly record struct DomainIntentSignalLexicon(
        IReadOnlyList<string> AdSignals,
        IReadOnlyList<string> PublicSignals);

    private static DomainIntentSignalLexicon ResolveDomainIntentSignalLexicon(IReadOnlyList<ToolDefinition>? availableDefinitions) {
        var adSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publicSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDomainSignalTokens(adSignals, ToolSelectionMetadata.GetDefaultDomainSignalTokens(DomainIntentFamilyAd));
        AddDomainSignalTokens(publicSignals, ToolSelectionMetadata.GetDefaultDomainSignalTokens(DomainIntentFamilyPublic));

        if (availableDefinitions is not null && availableDefinitions.Count > 0) {
            for (var i = 0; i < availableDefinitions.Count; i++) {
                var definition = availableDefinitions[i];
                if (definition is null) {
                    continue;
                }

                var family = ResolveDomainIntentFamily(definition);
                if (family.Length == 0) {
                    continue;
                }

                var signalSet = string.Equals(family, DomainIntentFamilyAd, StringComparison.Ordinal)
                    ? adSignals
                    : string.Equals(family, DomainIntentFamilyPublic, StringComparison.Ordinal)
                        ? publicSignals
                        : null;
                if (signalSet is null) {
                    continue;
                }

                var routingSignals = definition.Routing?.DomainSignalTokens;
                if (routingSignals is { Count: > 0 }) {
                    AddDomainSignalTokens(signalSet, routingSignals);
                    continue;
                }

                AddDomainSignalTokens(signalSet, ToolSelectionMetadata.GetDomainSignalTokens(definition));
            }
        }

        return new DomainIntentSignalLexicon(
            AdSignals: ToSortedDomainSignalArray(adSignals),
            PublicSignals: ToSortedDomainSignalArray(publicSignals));
    }

    private static void AddDomainSignalTokens(HashSet<string> destination, IReadOnlyList<string>? tokens) {
        if (destination is null || tokens is null || tokens.Count == 0) {
            return;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var normalized = NormalizeDomainSignalTokenValue(tokens[i] ?? string.Empty);
            if (normalized.Length < 2) {
                continue;
            }

            destination.Add(normalized);
        }
    }

    private static IReadOnlyList<string> ToSortedDomainSignalArray(HashSet<string> signals) {
        if (signals is null || signals.Count == 0) {
            return Array.Empty<string>();
        }

        var list = signals.ToArray();
        Array.Sort(list, StringComparer.OrdinalIgnoreCase);
        return list;
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
        var compactToken = NormalizeCompactToken(normalizedToken.AsSpan());
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

            if (compactToken.Length > 0
                && string.Equals(NormalizeCompactToken(candidate.AsSpan()), compactToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (MatchesDomainSignalCandidateBySegments(candidate, normalizedToken, compactToken)) {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDomainSignalCandidateBySegments(string candidate, string normalizedToken, string compactToken) {
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return false;
        }

        var segments = normalizedCandidate.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++) {
            var segment = (segments[i] ?? string.Empty).Trim();
            if (segment.Length == 0) {
                continue;
            }

            if (string.Equals(segment, normalizedToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (compactToken.Length > 0
                && string.Equals(NormalizeCompactToken(segment.AsSpan()), compactToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (IsDomainSignalSuffixVariant(segment, normalizedToken)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsDomainSignalSuffixVariant(string candidate, string token) {
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        var normalizedToken = (token ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0 || normalizedToken.Length < 5) {
            return false;
        }

        if (normalizedCandidate.Length <= normalizedToken.Length || normalizedCandidate.Length > normalizedToken.Length + 3) {
            return false;
        }

        if (!normalizedCandidate.StartsWith(normalizedToken, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        for (var i = normalizedToken.Length; i < normalizedCandidate.Length; i++) {
            if (!char.IsLetter(normalizedCandidate[i])) {
                return false;
            }
        }

        return true;
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

        if (!TryResolveDomainIntentFamilyFromUserSignals(userRequest, _registry.GetDefinitions(), out var inferredFamily)) {
            return false;
        }

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
