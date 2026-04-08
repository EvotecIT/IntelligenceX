using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private enum ToolCapabilityFollowUpIntent {
        None,
        DescribeTool,
        CompareTools,
        JsonExamples,
        CompactSummary
    }

    private static bool TryBuildInformationalToolCapabilityFallbackText(
        string userRequest,
        string routedUserRequest,
        string lastAssistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        out string text) {
        text = string.Empty;
        if (toolDefinitions is not { Count: > 0 }) {
            return false;
        }

        var rawRequest = (userRequest ?? string.Empty).Trim();
        if (rawRequest.Length == 0) {
            return false;
        }

        var requestedToolNames = ExtractExplicitRequestedToolNames(rawRequest);
        if (requestedToolNames.Length == 0) {
            requestedToolNames = ExtractExplicitRequestedToolNames(routedUserRequest);
        }

        var selectedTools = ResolveRequestedToolDefinitions(requestedToolNames, toolDefinitions, maxCount: 2);
        if (selectedTools.Count == 0) {
            selectedTools = ResolveMentionedToolDefinitions(rawRequest, routedUserRequest, lastAssistantDraft, toolDefinitions, maxCount: 2);
        }

        var intent = ResolveToolCapabilityFollowUpIntent(rawRequest, selectedTools.Count);
        if (intent == ToolCapabilityFollowUpIntent.None) {
            return false;
        }

        if (selectedTools.Count == 0) {
            return false;
        }

        if (intent != ToolCapabilityFollowUpIntent.DescribeTool && selectedTools.Count < 2) {
            return false;
        }

        text = intent switch {
            ToolCapabilityFollowUpIntent.DescribeTool => BuildToolCapabilityDescriptionText(selectedTools[0]),
            ToolCapabilityFollowUpIntent.CompareTools => BuildToolCapabilityComparisonText(selectedTools),
            ToolCapabilityFollowUpIntent.JsonExamples => BuildToolCapabilityJsonExamplesText(selectedTools),
            ToolCapabilityFollowUpIntent.CompactSummary => BuildToolCapabilitySummaryText(selectedTools),
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(text);
    }

    private static ToolCapabilityFollowUpIntent ResolveToolCapabilityFollowUpIntent(string userRequest, int selectedToolCount) {
        var rawRequest = (userRequest ?? string.Empty).Trim();
        var normalized = NormalizeCompactText(rawRequest);
        if (normalized.Length == 0) {
            return ToolCapabilityFollowUpIntent.None;
        }

        if (ContainsPhraseWithBoundaries(normalized, "json")
            || normalized.Contains('{', StringComparison.Ordinal)
            || normalized.Contains('}', StringComparison.Ordinal)) {
            return ToolCapabilityFollowUpIntent.JsonExamples;
        }

        if (normalized.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase) >= 0
            || selectedToolCount >= 2 && ContainsQuestionSignal(rawRequest)) {
            return ToolCapabilityFollowUpIntent.CompareTools;
        }

        if (selectedToolCount <= 1 && LooksLikeExplicitToolQuestionTurn(rawRequest)) {
            return ToolCapabilityFollowUpIntent.DescribeTool;
        }

        if (selectedToolCount >= 2) {
            return ToolCapabilityFollowUpIntent.CompactSummary;
        }

        return ToolCapabilityFollowUpIntent.None;
    }

    private static List<ToolDefinition> ResolveRequestedToolDefinitions(
        IReadOnlyList<string> requestedToolNames,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        int maxCount) {
        var selected = new List<ToolDefinition>(Math.Min(maxCount, requestedToolNames.Count));
        if (requestedToolNames.Count == 0 || toolDefinitions.Count == 0 || maxCount <= 0) {
            return selected;
        }

        for (var requestedIndex = 0; requestedIndex < requestedToolNames.Count; requestedIndex++) {
            var requestedToolName = (requestedToolNames[requestedIndex] ?? string.Empty).Trim();
            if (requestedToolName.Length == 0) {
                continue;
            }

            for (var definitionIndex = 0; definitionIndex < toolDefinitions.Count; definitionIndex++) {
                var definition = toolDefinitions[definitionIndex];
                if (!string.Equals(definition.Name, requestedToolName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(definition.CanonicalName, requestedToolName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var alreadySelected = false;
                for (var selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++) {
                    if (string.Equals(selected[selectedIndex].Name, definition.Name, StringComparison.OrdinalIgnoreCase)) {
                        alreadySelected = true;
                        break;
                    }
                }

                if (!alreadySelected) {
                    selected.Add(definition);
                }

                break;
            }

            if (selected.Count >= maxCount) {
                break;
            }
        }

        return selected;
    }

    private static List<ToolDefinition> ResolveMentionedToolDefinitions(
        string userRequest,
        string routedUserRequest,
        string lastAssistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        int maxCount) {
        var selected = new List<ToolDefinition>(Math.Min(maxCount, toolDefinitions.Count));
        if (toolDefinitions.Count == 0 || maxCount <= 0) {
            return selected;
        }

        var searchTexts = new[] {
            (userRequest ?? string.Empty).Trim(),
            (routedUserRequest ?? string.Empty).Trim(),
            (lastAssistantDraft ?? string.Empty).Trim()
        };

        for (var searchIndex = 0; searchIndex < searchTexts.Length; searchIndex++) {
            var searchText = searchTexts[searchIndex];
            if (searchText.Length == 0) {
                continue;
            }

            for (var definitionIndex = 0; definitionIndex < toolDefinitions.Count; definitionIndex++) {
                var definition = toolDefinitions[definitionIndex];
                if (searchText.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) < 0
                    && searchText.IndexOf(definition.CanonicalName, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                var alreadySelected = false;
                for (var selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++) {
                    if (string.Equals(selected[selectedIndex].Name, definition.Name, StringComparison.OrdinalIgnoreCase)) {
                        alreadySelected = true;
                        break;
                    }
                }

                if (!alreadySelected) {
                    selected.Add(definition);
                }

                if (selected.Count >= maxCount) {
                    return selected;
                }
            }
        }

        return selected;
    }

    private static string BuildToolCapabilityDescriptionText(ToolDefinition definition) {
        var sb = new StringBuilder(512);
        var displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Name : definition.DisplayName!;
        sb.Append('`').Append(definition.Name).Append("`");
        if (!string.Equals(displayName, definition.Name, StringComparison.OrdinalIgnoreCase)) {
            sb.Append(" - ").Append(displayName);
        }

        sb.AppendLine();
        sb.Append("- Co robi: ").AppendLine(NormalizeSentence(definition.Description, fallback: "Narzedzie opisowe bez dodatkowego opisu."));
        var requiredFields = GetRequiredFieldNames(definition.Parameters);
        if (requiredFields.Count > 0) {
            sb.Append("- Wymagane pola: ");
            AppendInlineCodeList(sb, requiredFields);
            sb.AppendLine();
        }

        var shortUsage = BuildShortUsageHint(definition);
        if (shortUsage.Length > 0) {
            sb.Append("- Kiedy uzyc: ").AppendLine(shortUsage);
        }

        return sb.ToString().Trim();
    }

    private static string BuildToolCapabilityComparisonText(IReadOnlyList<ToolDefinition> definitions) {
        var first = definitions[0];
        var second = definitions[1];
        var sb = new StringBuilder(768);
        sb.Append('`').Append(first.Name).Append("` vs `").Append(second.Name).AppendLine("`");
        sb.Append("- `").Append(first.Name).Append("`: ").AppendLine(BuildShortUsageHint(first));
        sb.Append("- `").Append(second.Name).Append("`: ").AppendLine(BuildShortUsageHint(second));
        sb.Append("- 1 praktyczna roznica: `")
            .Append(first.Name)
            .Append("` opiera sie na ")
            .Append(DescribePrimaryInput(first))
            .Append(", a `")
            .Append(second.Name)
            .Append("` opiera sie na ")
            .Append(DescribePrimaryInput(second))
            .AppendLine(".");
        return sb.ToString().Trim();
    }

    private static string BuildToolCapabilityJsonExamplesText(IReadOnlyList<ToolDefinition> definitions) {
        var sb = new StringBuilder(1024);
        sb.AppendLine("Krotkie przyklady JSON:");
        for (var i = 0; i < definitions.Count; i++) {
            var example = BuildToolCapabilityExampleArguments(definitions[i]);
            if (example.Count == 0) {
                continue;
            }

            if (i > 0) {
                sb.AppendLine();
            }

            sb.AppendLine("```json");
            sb.AppendLine(JsonLite.Serialize(JsonValue.From(example)));
            sb.AppendLine("```");
        }

        return sb.ToString().Trim();
    }

    private static string BuildToolCapabilityBlockersText(IReadOnlyList<ToolDefinition> definitions) {
        var sb = new StringBuilder(768);
        sb.AppendLine("Krotka lista blokerow:");
        for (var i = 0; i < definitions.Count; i++) {
            var tool = definitions[i];
            sb.Append("- `").Append(tool.Name).Append("`");
            var modeLabel = ResolveToolModeLabel(tool);
            if (modeLabel.Length > 0) {
                sb.Append(" / `").Append(modeLabel).Append('`');
            }

            sb.Append(": ").AppendLine(BuildToolBlockerSummary(tool));
        }

        return sb.ToString().Trim();
    }

    private static string BuildToolCapabilitySequenceText(IReadOnlyList<ToolDefinition> definitions) {
        var first = definitions[0];
        var second = definitions[1];
        var sb = new StringBuilder(896);
        sb.AppendLine("Praktyczna sekwencja triage:");
        sb.Append("1. Zacznij od `").Append(first.Name).AppendLine("`, zeby szybko zebrac pierwszy sygnal.");
        sb.Append("2. Zawez zakres po podstawowym polu `").Append(ResolveRepresentativeFieldName(first)).AppendLine("` i czasie.");
        sb.Append("3. Gdy potrzebujesz materialu offline albo powtarzalnego eksportu, przejdz do `").Append(second.Name).AppendLine("`.");
        sb.Append("4. Uzyj tych samych event IDs lub providerow, zeby porownac live i zapisany material bez zmiany kryteriow.");
        sb.Append("5. Po zebraniu obu widokow dopiero pivotuj dalej do kolejnych diagnostyk lub korelacji.");
        return sb.ToString().Trim();
    }

    private static string BuildToolCapabilitySummaryText(IReadOnlyList<ToolDefinition> definitions) {
        var first = definitions[0];
        var second = definitions[1];
        var sb = new StringBuilder(896);
        sb.AppendLine("Finalne wskazowki:");
        sb.Append("1. `").Append(first.Name).Append("` jest lepszy, gdy masz ")
            .Append(DescribePrimaryInput(first)).AppendLine(".");
        sb.Append("2. `").Append(second.Name).Append("` jest lepszy, gdy masz ")
            .Append(DescribePrimaryInput(second)).AppendLine(".");
        sb.AppendLine("3. Zachowuj te same filtry event IDs, providera i zakres czasu miedzy obu przebiegami.");
        sb.AppendLine("4. Traktuj live odczyt jako szybki stan biezacy, a EVTX jako material do powtornej analizy.");
        sb.AppendLine("5. Gdy live dostep pada, eksport EVTX daje stabilniejszy material do dalszej korelacji.");
        sb.AppendLine("6. Zakresy czasu i porownania zapisuj w UTC, zeby nie mieszac lokalnych stref i opoznionych eksportow.");
        return sb.ToString().Trim();
    }

    private static JsonObject BuildToolCapabilityExampleArguments(ToolDefinition definition) {
        var example = new JsonObject(StringComparer.Ordinal);
        example.Add("tool", definition.Name);
        var requiredFields = GetRequiredFieldNames(definition.Parameters);
        for (var i = 0; i < requiredFields.Count; i++) {
            AppendExampleValue(example, definition, requiredFields[i], required: true);
        }

        if (!example.TryGetValue("max_events", out _)
            && HasSchemaProperty(definition.Parameters, "max_events")) {
            AppendExampleValue(example, definition, "max_events", required: false);
        }

        if (!example.TryGetValue("machine_name", out _)
            && HasSchemaProperty(definition.Parameters, "machine_name")
            && example.TryGetValue("log_name", out _)) {
            AppendExampleValue(example, definition, "machine_name", required: false);
        }

        return example;
    }

    private static void AppendExampleValue(JsonObject example, ToolDefinition definition, string fieldName, bool required) {
        if (example.TryGetValue(fieldName, out _)) {
            return;
        }

        switch (fieldName) {
            case "path":
                example.Add(fieldName, @"C:\Evidence\Security.evtx");
                return;
            case "log_name":
                example.Add(fieldName, "Security");
                return;
            case "machine_name":
                example.Add(fieldName, "AD2.ad.evotec.xyz");
                return;
            case "max_events":
                example.Add(fieldName, 20L);
                return;
            case "oldest_first":
                if (required) {
                    example.Add(fieldName, false);
                }
                return;
            case "include_message":
                if (required) {
                    example.Add(fieldName, false);
                }
                return;
        }

        var property = definition.Parameters?.GetObject("properties")?.GetObject(fieldName);
        var type = property?.GetString("type") ?? string.Empty;
        if (string.Equals(type, "integer", StringComparison.OrdinalIgnoreCase)) {
            example.Add(fieldName, 1L);
            return;
        }

        if (string.Equals(type, "boolean", StringComparison.OrdinalIgnoreCase)) {
            example.Add(fieldName, false);
            return;
        }

        if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)) {
            example.Add(fieldName, new JsonArray().Add(4624L));
            return;
        }

        example.Add(fieldName, "value");
    }

    private static string BuildToolBlockerSummary(ToolDefinition definition) {
        var requiredFields = GetRequiredFieldNames(definition.Parameters);
        var sb = new StringBuilder(256);
        var primaryConstraint = ResolvePrimaryConstraint(definition);
        if (primaryConstraint.Length > 0) {
            sb.Append(primaryConstraint);
        } else {
            sb.Append("wymaga poprawnego wejscia i zgodnego runtime dla tego narzedzia");
        }

        if (requiredFields.Count > 0) {
            sb.Append("; kluczowe pole to `").Append(requiredFields[0]).Append('`');
        }

        return sb.ToString();
    }

    private static string BuildShortUsageHint(ToolDefinition definition) {
        var primaryConstraint = ResolvePrimaryConstraint(definition);
        if (primaryConstraint.Length > 0) {
            return primaryConstraint;
        }

        return NormalizeSentence(definition.Description, fallback: "uzyj tego narzedzia zgodnie z jego schema wejscia");
    }

    private static string ResolvePrimaryConstraint(ToolDefinition definition) {
        var description = NormalizeSentence(definition.Description, fallback: string.Empty);
        var name = definition.Name;
        if (name.IndexOf("evtx", StringComparison.OrdinalIgnoreCase) >= 0
            || HasSchemaProperty(definition.Parameters, "path")) {
            return "potrzebuje pliku `.evtx` w dozwolonej sciezce i pracuje na materiale offline";
        }

        if (name.IndexOf("live", StringComparison.OrdinalIgnoreCase) >= 0
            || HasSchemaProperty(definition.Parameters, "log_name")) {
            return "potrzebuje dostepu do kanalu Event Log i, dla hosta zdalnego, lacznosci oraz uprawnien";
        }

        return description;
    }

    private static string ResolveToolModeLabel(ToolDefinition definition) {
        if (definition.Name.IndexOf("evtx", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "evtx";
        }

        if (definition.Name.IndexOf("live", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "live";
        }

        return string.Empty;
    }

    private static string DescribePrimaryInput(ToolDefinition definition) {
        if (HasSchemaProperty(definition.Parameters, "path")) {
            return "plik `.evtx` przez `path`";
        }

        if (HasSchemaProperty(definition.Parameters, "log_name")) {
            return HasSchemaProperty(definition.Parameters, "machine_name")
                ? "kanal `log_name` i opcjonalnie host `machine_name`"
                : "kanal `log_name`";
        }

        var requiredFields = GetRequiredFieldNames(definition.Parameters);
        if (requiredFields.Count > 0) {
            return "pole `" + requiredFields[0] + "`";
        }

        return "schemat wejscia narzedzia";
    }

    private static string ResolveRepresentativeFieldName(ToolDefinition definition) {
        if (HasSchemaProperty(definition.Parameters, "log_name")) {
            return "log_name";
        }

        if (HasSchemaProperty(definition.Parameters, "path")) {
            return "path";
        }

        var requiredFields = GetRequiredFieldNames(definition.Parameters);
        return requiredFields.Count > 0 ? requiredFields[0] : "input";
    }

    private static bool HasSchemaProperty(JsonObject? schema, string propertyName) {
        return schema?.GetObject("properties")?.TryGetValue(propertyName, out _) is true;
    }

    private static List<string> GetRequiredFieldNames(JsonObject? schema) {
        var required = new List<string>();
        var requiredArray = schema?.GetArray("required");
        if (requiredArray is null) {
            return required;
        }

        foreach (var value in requiredArray) {
            var fieldName = value.AsString();
            if (string.IsNullOrWhiteSpace(fieldName)) {
                continue;
            }

            required.Add(fieldName.Trim());
        }

        return required;
    }

    private static void AppendInlineCodeList(StringBuilder sb, IReadOnlyList<string> values) {
        for (var i = 0; i < values.Count; i++) {
            if (i > 0) {
                sb.Append(i == values.Count - 1 ? " i " : ", ");
            }

            sb.Append('`').Append(values[i]).Append('`');
        }
    }

    private static string NormalizeSentence(string? text, string fallback) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return fallback;
        }

        normalized = normalized.TrimEnd('.', '!', '?');
        return normalized.Length == 0 ? fallback : normalized;
    }

    internal static string? BuildInformationalToolCapabilityFallbackTextForTesting(
        string userRequest,
        string routedUserRequest,
        string lastAssistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions) {
        return TryBuildInformationalToolCapabilityFallbackText(userRequest, routedUserRequest, lastAssistantDraft, toolDefinitions, out var text)
            ? text
            : null;
    }
}
