using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    internal static string ExtractScenarioUserRequestForDomainIntentRoutingForTesting(string text) {
        return ExtractScenarioUserRequestForDomainIntentRouting(text);
    }

    internal static bool TryResolveDomainIntentFamilySelectionForTesting(
        string userRequest,
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? pendingFamilies,
        out string family) {
        return TryResolveDomainIntentFamilySelection(userRequest, toolDefinitions, pendingFamilies, out family);
    }

    internal static bool HasMixedDomainIntentSignalsForTesting(string userRequest) {
        return HasMixedDomainIntentSignals(userRequest);
    }

    internal static IReadOnlyList<string> ExtractDomainLikeTokensForTesting(string text) {
        return ExtractDomainLikeTokens(text);
    }

    internal static bool TryFilterToolsByDomainIntentFamilyForTesting(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string family,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out int removedCount) {
        return TryFilterToolsByDomainIntentFamily(toolDefinitions, family, out filteredTools, out removedCount);
    }

    private sealed partial class ReplSession {
        private static readonly TimeSpan PendingDomainIntentClarificationContextMaxAge = TimeSpan.FromMinutes(30);

        private IReadOnlyList<ToolDefinition> ApplyDomainIntentTurnContext(
            string inputText,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            out string rewrittenInputText,
            out string forcedBootstrapToolName) {
            rewrittenInputText = inputText ?? string.Empty;
            forcedBootstrapToolName = string.Empty;
            if (toolDefinitions is null || toolDefinitions.Count == 0) {
                return toolDefinitions ?? Array.Empty<ToolDefinition>();
            }

            var userRequest = ExtractScenarioUserRequestForDomainIntentRouting(rewrittenInputText);
            RememberDomainIntentTargetsFromUserRequest(userRequest);
            var availableFamilies = ResolveOrderedDomainIntentFamilies(families: null, toolDefinitions);
            if (availableFamilies.Count == 0) {
                return toolDefinitions;
            }

            var pendingFamilies = GetActivePendingDomainIntentClarificationFamilies();
            if (TryResolveDomainIntentFamilySelection(userRequest, toolDefinitions, pendingFamilies, out var selectedFamily)) {
                RememberPendingDomainIntentClarificationFamilies(availableFamilies);
                RememberPreferredDomainIntentFamily(selectedFamily);
                var rememberedTarget = GetRememberedDomainTargetForFamily(selectedFamily);
                var selectedTools = toolDefinitions;
                if (TryFilterToolsByDomainIntentFamily(toolDefinitions, selectedFamily, out var filteredSelectionTools, out _)) {
                    selectedTools = filteredSelectionTools;
                }

                if (LooksLikePureDomainIntentSelection(userRequest)) {
                    forcedBootstrapToolName = ResolveDomainIntentBootstrapToolName(selectedFamily, selectedTools, rememberedTarget);
                }

                rewrittenInputText = AppendDomainIntentSelectionRoutingHint(
                    rewrittenInputText,
                    selectedFamily,
                    rememberedTarget,
                    forcedBootstrapToolName);
                if (!ReferenceEquals(selectedTools, toolDefinitions)) {
                    return selectedTools;
                }

                return toolDefinitions;
            }

            if (HasMixedDomainIntentSignals(userRequest)) {
                RememberPendingDomainIntentClarificationFamilies(availableFamilies);
                return toolDefinitions;
            }

            if (TryGetPreferredDomainIntentFamily(out var preferredFamily)
                && TryFilterToolsByDomainIntentFamily(toolDefinitions, preferredFamily, out var preferredTools, out _)) {
                return preferredTools;
            }

            return toolDefinitions;
        }

        private void RememberPreferredDomainIntentFamily(string family) {
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                _preferredDomainIntentFamily = string.Empty;
                return;
            }

            _preferredDomainIntentFamily = normalizedFamily;
        }

        private bool TryGetPreferredDomainIntentFamily(out string family) {
            family = string.Empty;
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(_preferredDomainIntentFamily, out var normalizedFamily)) {
                return false;
            }

            family = normalizedFamily;
            return true;
        }

        private void RememberPendingDomainIntentClarificationFamilies(IReadOnlyList<string> families) {
            if (families is null || families.Count == 0) {
                _pendingDomainIntentClarificationFamilies = Array.Empty<string>();
                _pendingDomainIntentClarificationSeenUtcTicks = 0;
                return;
            }

            _pendingDomainIntentClarificationFamilies = families.ToArray();
            _pendingDomainIntentClarificationSeenUtcTicks = DateTime.UtcNow.Ticks;
        }

        private IReadOnlyList<string> GetActivePendingDomainIntentClarificationFamilies() {
            if (_pendingDomainIntentClarificationFamilies.Length == 0 || _pendingDomainIntentClarificationSeenUtcTicks <= 0) {
                return Array.Empty<string>();
            }

            var seenUtc = new DateTime(_pendingDomainIntentClarificationSeenUtcTicks, DateTimeKind.Utc);
            var age = DateTime.UtcNow - seenUtc;
            if (age < TimeSpan.Zero || age > PendingDomainIntentClarificationContextMaxAge) {
                _pendingDomainIntentClarificationFamilies = Array.Empty<string>();
                _pendingDomainIntentClarificationSeenUtcTicks = 0;
                return Array.Empty<string>();
            }

            return _pendingDomainIntentClarificationFamilies;
        }

        private void RememberDomainIntentTargetsFromUserRequest(string userRequest) {
            var domains = ExtractDomainLikeTokens(userRequest);
            if (domains.Count == 0) {
                return;
            }

            if (domains.Count == 1) {
                if (TryGetPreferredDomainIntentFamily(out var preferredFamily)) {
                    RememberDomainTargetForFamily(preferredFamily, domains[0]);
                }
                return;
            }

            var ordered = domains
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static value => CountDomainLabels(value))
                .ThenByDescending(static value => value.Length)
                .ToArray();
            if (ordered.Length > 0) {
                RememberDomainTargetForFamily(ToolSelectionMetadata.DomainIntentFamilyAd, ordered[0]);
                RememberDomainTargetForFamily(ToolSelectionMetadata.DomainIntentFamilyPublic, ordered[^1]);
            }
        }

        private void RememberDomainTargetForFamily(string family, string target) {
            var normalizedTarget = NormalizeRememberedDomainTarget(target);
            if (normalizedTarget.Length == 0 || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                return;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
                _rememberedAdDomainTarget = normalizedTarget;
                return;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                _rememberedPublicDomainTarget = normalizedTarget;
            }
        }

        private string GetRememberedDomainTargetForFamily(string family) {
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                return string.Empty;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
                return _rememberedAdDomainTarget;
            }

            if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
                return _rememberedPublicDomainTarget;
            }

            return string.Empty;
        }
    }

    private static string ExtractScenarioUserRequestForDomainIntentRouting(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        const string marker = "User request:";
        var markerIndex = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return normalized;
        }

        var request = normalized[(markerIndex + marker.Length)..].Trim();
        return request.Length == 0 ? normalized : request;
    }

    private static bool TryResolveDomainIntentFamilySelection(
        string userRequest,
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? pendingFamilies,
        out string family) {
        family = string.Empty;
        var normalized = ExtractScenarioUserRequestForDomainIntentRouting(userRequest);
        if (normalized.Length == 0) {
            return false;
        }

        if (TryParseDomainIntentFamilyFromActionSelectionPayload(normalized, out family)) {
            return true;
        }

        if (TryParseDomainIntentMarkerSelection(normalized, "ix:domain-intent:v1", out family)
            || TryParseDomainIntentMarkerSelection(normalized, "ix:domain-intent-choice:v1", out family)) {
            return true;
        }

        if (ToolSelectionMetadata.TryNormalizeDomainIntentFamily(normalized, out family)) {
            return true;
        }

        if (TryParseOrdinalSelection(normalized, out var ordinal)
            && TryMapOrdinalSelectionToDomainIntentFamily(pendingFamilies, toolDefinitions, ordinal, out family)) {
            return true;
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyFromActionSelectionPayload(string text, out string family) {
        family = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized[0] != '{') {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(normalized);
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
                && titleNode.ValueKind == JsonValueKind.String
                && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(titleNode.GetString(), out family)) {
                return true;
            }

            if (TryGetObjectPropertyCaseInsensitive(selection, out var requestNode, "request")
                && requestNode.ValueKind == JsonValueKind.Object
                && TryParseDomainIntentFamilyProperty(
                    requestNode,
                    out family,
                    "family",
                    "scope",
                    "domain_family",
                    "domainFamily")) {
                return true;
            }

            if (TryGetObjectPropertyCaseInsensitive(selection, out requestNode, "request")
                && requestNode.ValueKind == JsonValueKind.Object
                && TryGetObjectPropertyCaseInsensitive(
                    requestNode,
                    out var domainScopeNode,
                    "ix_domain_scope",
                    "ixDomainScope",
                    "ix_domain_intent",
                    "ixDomainIntent")
                && domainScopeNode.ValueKind == JsonValueKind.Object
                && TryParseDomainIntentFamilyProperty(
                    domainScopeNode,
                    out family,
                    "family",
                    "scope",
                    "domain_family",
                    "domainFamily")) {
                return true;
            }
        } catch (JsonException) {
            return false;
        }

        return false;
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

            if ((string.Equals(key, "family", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(key, "scope", StringComparison.OrdinalIgnoreCase))
                && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(value, out family)) {
                return true;
            }

            if ((key.StartsWith("option_", StringComparison.OrdinalIgnoreCase) || key.All(char.IsDigit))
                && TryParseOrdinalSelection(key, out var optionOrdinal)
                && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(value, out var optionFamily)) {
                mappedFamiliesByOrdinal[optionOrdinal] = optionFamily;
                continue;
            }

            if (string.Equals(key, "choice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "selection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "option", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "index", StringComparison.OrdinalIgnoreCase)) {
                selectedValue = value;
                if (ToolSelectionMetadata.TryNormalizeDomainIntentFamily(value, out family)) {
                    return true;
                }
            }
        }

        if (selectedValue.Length == 0 || !TryParseOrdinalSelection(selectedValue, out var selectedOrdinal)) {
            return false;
        }

        return mappedFamiliesByOrdinal.TryGetValue(selectedOrdinal, out var mappedFamily)
               && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(mappedFamily, out family);
    }

    private static bool TryParseDomainIntentFamilyProperty(JsonElement node, out string family, params string[] names) {
        family = string.Empty;
        if (node.ValueKind != JsonValueKind.Object) {
            return false;
        }

        return TryGetObjectPropertyCaseInsensitive(node, out var familyNode, names)
               && familyNode.ValueKind == JsonValueKind.String
               && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(familyNode.GetString(), out family);
    }

    private static bool TryGetObjectPropertyCaseInsensitive(JsonElement node, out JsonElement value, params string[] names) {
        value = default;
        if (node.ValueKind != JsonValueKind.Object || names is null || names.Length == 0) {
            return false;
        }

        foreach (var property in node.EnumerateObject()) {
            for (var i = 0; i < names.Length; i++) {
                if (string.Equals(property.Name, names[i], StringComparison.OrdinalIgnoreCase)) {
                    value = property.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryMapOrdinalSelectionToDomainIntentFamily(
        IReadOnlyList<string>? pendingFamilies,
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        int ordinal,
        out string family) {
        family = string.Empty;
        if (ordinal <= 0) {
            return false;
        }

        var orderedFamilies = ResolveOrderedDomainIntentFamilies(pendingFamilies, toolDefinitions);
        if (orderedFamilies.Count == 0) {
            return false;
        }

        if (orderedFamilies.Count == 1 && ordinal == 1) {
            family = orderedFamilies[0];
            return true;
        }

        var index = ordinal - 1;
        if (index < 0 || index >= orderedFamilies.Count) {
            return false;
        }

        family = orderedFamilies[index];
        return true;
    }

    private static IReadOnlyList<string> ResolveOrderedDomainIntentFamilies(
        IReadOnlyList<string>? families,
        IReadOnlyList<ToolDefinition>? toolDefinitions = null) {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddFamily(string? candidate) {
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(candidate, out var normalizedFamily)) {
                return;
            }

            if (seen.Add(normalizedFamily)) {
                normalized.Add(normalizedFamily);
            }
        }

        if (families is not null) {
            for (var i = 0; i < families.Count; i++) {
                AddFamily(families[i]);
            }
        }

        if (toolDefinitions is not null) {
            for (var i = 0; i < toolDefinitions.Count; i++) {
                if (ToolSelectionMetadata.TryResolveDomainIntentFamily(toolDefinitions[i], out var family)) {
                    AddFamily(family);
                }
            }
        }

        normalized.Sort(static (left, right) => GetDomainIntentFamilySortOrder(left).CompareTo(GetDomainIntentFamilySortOrder(right)));
        return normalized;
    }

    private static int GetDomainIntentFamilySortOrder(string family) {
        if (string.Equals(family, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return 0;
        }

        if (string.Equals(family, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return 1;
        }

        return 2;
    }

    private static bool HasMixedDomainIntentSignals(string userRequest) {
        var normalized = NormalizeCompactDomainIntentText(ExtractScenarioUserRequestForDomainIntentRouting(userRequest));
        if (normalized.Length == 0) {
            return false;
        }

        var hasAdSignals = ContainsAnyDomainSignalToken(normalized, ToolSelectionMetadata.GetDefaultDomainSignalTokens(ToolSelectionMetadata.DomainIntentFamilyAd));
        var hasPublicSignals = ContainsAnyDomainSignalToken(normalized, ToolSelectionMetadata.GetDefaultDomainSignalTokens(ToolSelectionMetadata.DomainIntentFamilyPublic));
        return hasAdSignals && hasPublicSignals;
    }

    private static string NormalizeCompactDomainIntentText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                builder.Append(char.ToLowerInvariant(ch));
            } else {
                builder.Append(' ');
            }
        }

        return CollapseDomainIntentWhitespace(builder.ToString());
    }

    private static bool ContainsAnyDomainSignalToken(string normalizedText, IReadOnlyList<string> tokens) {
        if (string.IsNullOrWhiteSpace(normalizedText) || tokens is null || tokens.Count == 0) {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var token = NormalizeCompactDomainIntentText(tokens[i]);
            if (token.Length == 0) {
                continue;
            }

            if (normalizedText.Contains(token, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static string CollapseDomainIntentWhitespace(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var inWhitespace = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsWhiteSpace(ch)) {
                if (!inWhitespace) {
                    builder.Append(' ');
                    inWhitespace = true;
                }

                continue;
            }

            inWhitespace = false;
            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static bool TryFilterToolsByDomainIntentFamily(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string family,
        out IReadOnlyList<ToolDefinition> filteredTools,
        out int removedCount) {
        filteredTools = toolDefinitions;
        removedCount = 0;
        if (toolDefinitions is null || toolDefinitions.Count == 0 || !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return false;
        }

        var filtered = new List<ToolDefinition>(toolDefinitions.Count);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var definition = toolDefinitions[i];
            if (definition is null) {
                continue;
            }

            if (ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var toolFamily)
                && ToolSelectionMetadata.TryNormalizeDomainIntentFamily(toolFamily, out var normalizedToolFamily)
                && !string.Equals(normalizedToolFamily, normalizedFamily, StringComparison.Ordinal)) {
                removedCount++;
                continue;
            }

            filtered.Add(definition);
        }

        if (removedCount <= 0 || filtered.Count == 0) {
            return false;
        }

        filteredTools = filtered;
        return true;
    }

    private static string AppendDomainIntentSelectionRoutingHint(string inputText, string family, string target, string forcedToolName) {
        if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return inputText ?? string.Empty;
        }

        var normalizedInput = inputText ?? string.Empty;
        var hintBuilder = new StringBuilder()
            .AppendLine("ix:domain-intent:v1")
            .Append("family: ")
            .Append(normalizedFamily);
        var normalizedTarget = NormalizeRememberedDomainTarget(target);
        if (normalizedTarget.Length > 0) {
            hintBuilder
                .AppendLine()
                .AppendLine()
                .AppendLine("[Resolved domain target]")
                .Append("target: ")
                .Append(normalizedTarget)
                .AppendLine()
                .AppendLine("Proceed with read-only execution for the selected family and remembered target in this turn.")
                .Append("Do not ask a clarifying question before the first tool call.");
        }
        if (!string.IsNullOrWhiteSpace(forcedToolName)) {
            hintBuilder
                .AppendLine()
                .AppendLine()
                .AppendLine("[Bootstrap tool hint]")
                .Append("Use tool `")
                .Append(forcedToolName.Trim())
                .AppendLine("` first for this selection turn.");
            AppendBootstrapToolArgumentHint(hintBuilder, forcedToolName, normalizedTarget);
        }

        var hint = hintBuilder.ToString();
        if (normalizedInput.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) {
            return normalizedInput;
        }

        if (normalizedInput.Length == 0) {
            return hint;
        }

        return normalizedInput.TrimEnd() + "\n\n" + hint;
    }

    private static bool TryParseOrdinalSelection(string text, out int value) {
        value = 0;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var startIndex = IsOrdinalLeadingWrapper(normalized[0]) ? 1 : 0;
        if (startIndex >= normalized.Length) {
            return false;
        }

        var i = startIndex;
        var parsed = 0;
        while (i < normalized.Length) {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(normalized, i);
            if (digit < 0) {
                break;
            }

            if (parsed > (int.MaxValue - digit) / 10) {
                return false;
            }

            parsed = (parsed * 10) + digit;
            i++;
        }

        if (i == startIndex) {
            if (!TryParseDecoratedUnicodeOrdinalPrefix(normalized[startIndex..], out parsed, out var consumedChars)) {
                return false;
            }

            i = startIndex + consumedChars;
        }

        value = parsed;
        var rest = normalized[i..].Trim();
        if (rest.Length == 0) {
            return true;
        }

        if (startIndex > 0 && IsOrdinalClosingWrapper(rest[0])) {
            rest = rest[1..].Trim();
            if (rest.Length == 0) {
                return true;
            }
        }

        return IsAllowedOrdinalTrailingPunctuation(rest);
    }

    private static bool TryParseDecoratedUnicodeOrdinalPrefix(string text, out int value, out int consumedChars) {
        value = 0;
        consumedChars = 0;
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        if (!TryMapDecoratedUnicodeOrdinal(text[0], out value)) {
            return false;
        }

        consumedChars = 1;
        return true;
    }

    private static bool TryMapDecoratedUnicodeOrdinal(char ch, out int value) {
        value = 0;
        if (ch >= '\u2460' && ch <= '\u2473') {
            value = (ch - '\u2460') + 1;
            return true;
        }

        if (ch >= '\u2474' && ch <= '\u2487') {
            value = (ch - '\u2474') + 1;
            return true;
        }

        if (ch >= '\u2488' && ch <= '\u249B') {
            value = (ch - '\u2488') + 1;
            return true;
        }

        if (ch >= '\u2776' && ch <= '\u277F') {
            value = (ch - '\u2776') + 1;
            return true;
        }

        if (ch >= '\u2780' && ch <= '\u2789') {
            value = (ch - '\u2780') + 1;
            return true;
        }

        if (ch >= '\u278A' && ch <= '\u2793') {
            value = (ch - '\u278A') + 1;
            return true;
        }

        return false;
    }

    private static bool IsOrdinalLeadingWrapper(char ch) {
        return ch is '(' or '[' or '\uFF08' or '\uFF3B';
    }

    private static bool IsOrdinalClosingWrapper(char ch) {
        return ch is ')' or ']' or '\uFF09' or '\uFF3D';
    }

    private static bool IsAllowedOrdinalTrailingPunctuation(string rest) {
        return rest is "." or ")" or "]" or ":" or "．" or "）" or "］" or "：" or "。";
    }

    private static bool LooksLikePureDomainIntentSelection(string userRequest) {
        var normalized = ExtractScenarioUserRequestForDomainIntentRouting(userRequest).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.IndexOf("ix:domain-intent:v1", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("ix:domain-intent-choice:v1", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("\"ix_action_selection\"", StringComparison.OrdinalIgnoreCase) >= 0
               || (TryParseOrdinalSelection(normalized, out _) && normalized.Length <= 16)
               || ToolSelectionMetadata.TryNormalizeDomainIntentFamily(normalized, out _);
    }

    private static string ResolveDomainIntentBootstrapToolName(
        string family,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string rememberedTarget) {
        if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            return string.Empty;
        }

        string[] candidates;
        if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            candidates = rememberedTarget.Length > 0
                ? new[] { "domaindetective_domain_summary", "dnsclientx_query" }
                : new[] { "domaindetective_pack_info", "dnsclientx_pack_info" };
        } else if (string.Equals(normalizedFamily, ToolSelectionMetadata.DomainIntentFamilyAd, StringComparison.Ordinal)) {
            candidates = new[] { "ad_scope_discovery", "ad_environment_discover", "ad_forest_discover" };
        } else {
            return string.Empty;
        }

        for (var i = 0; i < candidates.Length; i++) {
            var candidate = candidates[i];
            for (var t = 0; t < toolDefinitions.Count; t++) {
                var definition = toolDefinitions[t];
                if (definition is null || !string.Equals(definition.Name, candidate, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                return definition.Name;
            }
        }

        return string.Empty;
    }

    private static void AppendBootstrapToolArgumentHint(StringBuilder builder, string forcedToolName, string rememberedTarget) {
        var normalizedToolName = (forcedToolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return;
        }

        if (string.Equals(normalizedToolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)
            && rememberedTarget.Length > 0) {
            builder.Append("Set `domain` to `").Append(rememberedTarget).Append("`.");
            return;
        }

        if (string.Equals(normalizedToolName, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)
            && rememberedTarget.Length > 0) {
            builder.Append("Set `name` to `").Append(rememberedTarget).Append("`.");
            return;
        }

        if (string.Equals(normalizedToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            && rememberedTarget.Length > 0) {
            builder.Append("Set `domain_name` to `").Append(rememberedTarget).Append("` and `discovery_fallback` to `current_forest`.");
            return;
        }

        if (string.Equals(normalizedToolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)) {
            builder.Append("Use this to bootstrap the effective AD scope before deeper execution.");
            return;
        }

        builder.Append("Use the selected family scope for this tool execution.");
    }

    private static IReadOnlyList<string> ExtractDomainLikeTokens(string text) {
        var normalized = ExtractScenarioUserRequestForDomainIntentRouting(text);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return Array.Empty<string>();
        }

        var matches = Regex.Matches(
            normalized,
            @"\b[\p{L}\p{N}][\p{L}\p{N}-]*\.[\p{L}\p{N}][\p{L}\p{N}\.-]*\b",
            RegexOptions.CultureInvariant);
        if (matches.Count == 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>(matches.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < matches.Count; i++) {
            var candidate = NormalizeRememberedDomainTarget(matches[i].Value);
            if (candidate.Length == 0
                || Uri.CheckHostName(candidate) is UriHostNameType.IPv4 or UriHostNameType.IPv6
                || !seen.Add(candidate)) {
                continue;
            }

            values.Add(candidate);
        }

        return values;
    }

    private static string NormalizeRememberedDomainTarget(string value) {
        var normalized = (value ?? string.Empty).Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'');
        return normalized;
    }

    private static int CountDomainLabels(string value) {
        var normalized = NormalizeRememberedDomainTarget(value);
        if (normalized.Length == 0) {
            return 0;
        }

        return normalized.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
