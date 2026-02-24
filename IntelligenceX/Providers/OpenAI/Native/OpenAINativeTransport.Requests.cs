using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport {
    private enum ToolWireFormat {
        FunctionNestedParameters,
        FunctionNestedInputSchema,
        CustomParameters,
        CustomInputSchema,
        FunctionFlatParameters,
        FunctionFlatInputSchema
    }

    private enum ToolSchemaKey {
        Parameters,
        InputSchema
    }

    private JsonObject BuildRequestBody(string model, IReadOnlyList<JsonObject> messages, string sessionId, ChatOptions options,
        ToolWireFormat toolWireFormat = ToolWireFormat.FunctionNestedParameters) {
        var input = new JsonArray();
        var replayItems = NormalizeAndFilterReplayInputItems(messages);
        foreach (var message in replayItems) {
            input.Add(message);
        }

        var instructions = string.IsNullOrWhiteSpace(options.Instructions)
            ? _options.Instructions
            : options.Instructions!;
        var verbosity = options.TextVerbosity ?? _options.TextVerbosity;
        var reasoningEffort = options.ReasoningEffort ?? _options.ReasoningEffort;
        var reasoningSummary = options.ReasoningSummary ?? _options.ReasoningSummary;
        var temperature = options.Temperature;

        var body = new JsonObject()
            .Add("model", model)
            .Add("store", false)
            .Add("stream", true)
            .Add("instructions", instructions)
            .Add("input", input)
            .Add("text", new JsonObject().Add("verbosity", verbosity.ToApiString()))
            .Add("prompt_cache_key", sessionId);

        if (temperature.HasValue) {
            body.Add("temperature", temperature.Value);
        }

        if (reasoningEffort.HasValue || reasoningSummary.HasValue) {
            var reasoning = new JsonObject();
            if (reasoningEffort.HasValue) {
                reasoning.Add("effort", reasoningEffort.Value.ToApiString());
            }
            if (reasoningSummary.HasValue) {
                reasoning.Add("summary", reasoningSummary.Value.ToApiString());
            }
            body.Add("reasoning", reasoning);
        }

        if (_options.IncludeReasoningEncryptedContent) {
            var include = new JsonArray();
            include.Add("reasoning.encrypted_content");
            body.Add("include", include);
        }

        // ChatGPT-native "responses" backend has been observed to reject previous_response_id.
        // We keep conversation state client-side via NativeThreadState.Messages.

        var requestTools = GetValidTools(options.Tools);
        if (requestTools.Count > 0) {
            var tools = new JsonArray();
            foreach (var tool in requestTools) {
                tools.Add(SerializeToolDefinition(tool, toolWireFormat));
            }
            body.Add("tools", tools);

            var choice = NormalizeToolChoice(options.ToolChoice, requestTools);
            var toolChoice = SerializeToolChoice(choice, toolWireFormat);
            if (toolChoice is JsonObject obj) {
                body.Add("tool_choice", obj);
            } else {
                body.Add("tool_choice", (string)toolChoice);
            }

            if (options.ParallelToolCalls.HasValue) {
                body.Add("parallel_tool_calls", options.ParallelToolCalls.Value);
            } else {
                body.Add("parallel_tool_calls", true);
            }
        }

        return body;
    }

    private static IReadOnlyList<ToolDefinition> GetValidTools(IReadOnlyList<ToolDefinition>? tools) {
        if (tools is null || tools.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var valid = new List<ToolDefinition>(tools.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tools.Count; i++) {
            var tool = tools[i];
            if (tool is null) {
                continue;
            }

            var name = (tool.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            if (seen.Add(name)) {
                valid.Add(tool);
            }
        }

        return valid.Count == 0 ? Array.Empty<ToolDefinition>() : valid;
    }

    private static ToolChoice NormalizeToolChoice(ToolChoice? choice, IReadOnlyList<ToolDefinition> requestTools) {
        if (choice is null) {
            return ToolChoice.Auto;
        }

        if (!string.Equals(choice.Type, "custom", StringComparison.OrdinalIgnoreCase)) {
            return choice;
        }

        var requestedName = (choice.Name ?? string.Empty).Trim();
        if (requestedName.Length == 0) {
            return ToolChoice.Auto;
        }

        for (var i = 0; i < requestTools.Count; i++) {
            var toolName = requestTools[i].Name;
            if (string.Equals(toolName, requestedName, StringComparison.OrdinalIgnoreCase)) {
                return string.Equals(toolName, requestedName, StringComparison.Ordinal)
                    ? choice
                    : ToolChoice.Custom(toolName);
            }
        }

        return ToolChoice.Auto;
    }

    private static object SerializeToolChoice(ToolChoice choice, ToolWireFormat toolWireFormat) {
        if (string.Equals(choice.Type, "custom", StringComparison.OrdinalIgnoreCase)) {
            var name = (choice.Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                return "auto";
            }
            var isFunctionWireFormat = toolWireFormat == ToolWireFormat.FunctionNestedParameters ||
                                       toolWireFormat == ToolWireFormat.FunctionNestedInputSchema ||
                                       toolWireFormat == ToolWireFormat.FunctionFlatParameters ||
                                       toolWireFormat == ToolWireFormat.FunctionFlatInputSchema;
            if (isFunctionWireFormat) {
                // Forced tool choice must match the wire format used for tool definitions.
                if (toolWireFormat == ToolWireFormat.FunctionFlatParameters ||
                    toolWireFormat == ToolWireFormat.FunctionFlatInputSchema) {
                    return new JsonObject()
                        .Add("type", "function")
                        .Add("name", name);
                }

                return new JsonObject()
                    .Add("type", "function")
                    .Add("function", new JsonObject().Add("name", name));
            }

            return new JsonObject()
                .Add("type", "custom")
                .Add("name", name);
        }

        if (string.Equals(choice.Type, "auto", StringComparison.OrdinalIgnoreCase)) {
            return "auto";
        }
        if (string.Equals(choice.Type, "none", StringComparison.OrdinalIgnoreCase)) {
            return "none";
        }

        throw new InvalidOperationException($"Unsupported tool choice type: {choice.Type ?? "<null>"}");
    }

    private static JsonObject SerializeToolDefinition(ToolDefinition tool, ToolWireFormat toolWireFormat) {
        switch (toolWireFormat) {
            case ToolWireFormat.FunctionNestedInputSchema:
            case ToolWireFormat.FunctionNestedParameters: {
                var function = new JsonObject()
                    .Add("name", tool.Name);
                var description = tool.GetDescriptionWithTags();
                if (!string.IsNullOrWhiteSpace(description)) {
                    function.Add("description", description);
                }
                if (tool.Parameters is not null) {
                    function.Add(toolWireFormat == ToolWireFormat.FunctionNestedInputSchema ? "input_schema" : "parameters", tool.Parameters);
                }

                return new JsonObject()
                    .Add("type", "function")
                    .Add("function", function);
            }
            case ToolWireFormat.CustomInputSchema:
            case ToolWireFormat.CustomParameters: {
                var obj = new JsonObject()
                    .Add("type", "custom")
                    .Add("name", tool.Name);
                var description = tool.GetDescriptionWithTags();
                if (!string.IsNullOrWhiteSpace(description)) {
                    obj.Add("description", description);
                }
                if (tool.Parameters is not null) {
                    // ChatGPT native API has historically accepted either `parameters` or `input_schema` for custom tools.
                    // We start with `parameters`, retry `input_schema`, and finally fall back to function-style tools if needed.
                    obj.Add(toolWireFormat == ToolWireFormat.CustomInputSchema ? "input_schema" : "parameters", tool.Parameters);
                }
                return obj;
            }
            case ToolWireFormat.FunctionFlatInputSchema:
            case ToolWireFormat.FunctionFlatParameters: {
                // ChatGPT native variants have been observed to require `tools[].name` at the top level (not nested).
                var obj = new JsonObject()
                    .Add("type", "function")
                    .Add("name", tool.Name);
                var description = tool.GetDescriptionWithTags();
                if (!string.IsNullOrWhiteSpace(description)) {
                    obj.Add("description", description);
                }
                if (tool.Parameters is not null) {
                    obj.Add(toolWireFormat == ToolWireFormat.FunctionFlatInputSchema ? "input_schema" : "parameters", tool.Parameters);
                }
                return obj;
            }
            default:
                throw new InvalidOperationException($"Unsupported tool wire format: {toolWireFormat}");
        }
    }

    private IEnumerable<KeyValuePair<string, string>> BuildHeaders(string accessToken, string accountId, string sessionId) {
        yield return new KeyValuePair<string, string>("Authorization", $"Bearer {accessToken}");
        yield return new KeyValuePair<string, string>("chatgpt-account-id", accountId);
        yield return new KeyValuePair<string, string>("OpenAI-Beta", "responses=experimental");
        yield return new KeyValuePair<string, string>("originator", _options.Originator);
        yield return new KeyValuePair<string, string>("session_id", sessionId);
        yield return new KeyValuePair<string, string>("accept", "text/event-stream");

        var agent = string.IsNullOrWhiteSpace(_options.UserAgent) ? BuildDefaultUserAgent() : _options.UserAgent!;
        yield return new KeyValuePair<string, string>("User-Agent", agent);
    }

    private static string BuildDefaultUserAgent() {
        try {
            var os = Environment.OSVersion.VersionString;
            return $"intelligencex ({os})";
        } catch {
            return "intelligencex";
        }
    }

    private static string NormalizeModelId(string? model, string fallback) {
        var value = string.IsNullOrWhiteSpace(model) ? fallback : model!;
        if (string.IsNullOrWhiteSpace(value)) {
            return "gpt-5.1";
        }
        var slash = value.LastIndexOf('/');
        return slash >= 0 && slash + 1 < value.Length ? value.Substring(slash + 1) : value;
    }

    private async Task<IReadOnlyList<JsonObject>> BuildInputItemsAsync(ChatInput input, CancellationToken cancellationToken) {
        var content = new JsonArray();
        var extras = new List<JsonObject>();
        foreach (var item in input.ToJson()) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }
            var type = obj.GetString("type");
            if (string.Equals(type, "text", StringComparison.Ordinal)) {
                var text = obj.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    content.Add(new JsonObject().Add("type", "input_text").Add("text", text));
                }
                continue;
            }
            if (string.Equals(type, "image", StringComparison.Ordinal)) {
                var url = obj.GetString("url");
                if (!string.IsNullOrWhiteSpace(url)) {
                    content.Add(new JsonObject().Add("type", "input_image").Add("image_url", url));
                    continue;
                }
                var path = obj.GetString("path");
                if (!string.IsNullOrWhiteSpace(path)) {
                    var bytes = await ReadFileBytesAsync(path!, cancellationToken).ConfigureAwait(false);
                    var base64 = Convert.ToBase64String(bytes);
                    var mime = GuessMimeType(path!);
                    var dataUrl = $"data:{mime};base64,{base64}";
                    content.Add(new JsonObject()
                        .Add("type", "input_image")
                        .Add("image_url", dataUrl)
                        .Add("detail", "auto"));
                }
                continue;
            }

            // Non-message items (tool outputs, custom items) are sent as-is.
            extras.Add(obj);
        }

        var items = new List<JsonObject>();
        if (content.Count > 0) {
            items.Add(new JsonObject()
                .Add("role", "user")
                .Add("content", content));
        }
        if (extras.Count > 0) {
            items.AddRange(extras);
        }
        return items;
    }

    private static JsonObject NormalizeInputItemForRequest(JsonObject message) {
        if (message is null) {
            return new JsonObject();
        }

        var type = (GetStringIgnoreCase(message, "type") ?? string.Empty).Trim();
        if (IsToolCallInputType(type) || LooksLikeToolCallInputShape(message)) {
            return NormalizeToolCallInputItem(message);
        }
        if (IsToolOutputInputType(type) || LooksLikeToolOutputInputShape(message)) {
            return NormalizeToolOutputInputItem(message);
        }

        return RemoveLegacyArgumentsFieldForNonToolInput(message);
    }

    private static IReadOnlyList<JsonObject> NormalizeAndFilterReplayInputItems(IReadOnlyList<JsonObject> messages) {
        if (messages is null || messages.Count == 0) {
            return Array.Empty<JsonObject>();
        }

        var normalized = new List<JsonObject>(messages.Count);
        var callIndexesById = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var outputIndexesById = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var hasToolReplayItems = false;

        for (var i = 0; i < messages.Count; i++) {
            var item = NormalizeInputItemForRequest(messages[i] ?? new JsonObject());
            normalized.Add(item);

            var type = (GetStringIgnoreCase(item, "type") ?? string.Empty).Trim();
            var isToolCall = IsToolCallInputType(type);
            var isToolOutput = IsToolOutputInputType(type);
            if (!isToolCall && !isToolOutput) {
                continue;
            }

            hasToolReplayItems = true;
            var callId = ExtractToolReplayCallId(item);
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            if (isToolCall) {
                AddReplayIndex(callIndexesById, callId!, i);
            } else if (isToolOutput) {
                AddReplayIndex(outputIndexesById, callId!, i);
            }
        }

        if (!hasToolReplayItems) {
            return normalized;
        }

        var selectedCallIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedOutputIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in callIndexesById) {
            var callId = entry.Key;
            if (!outputIndexesById.TryGetValue(callId, out var outputIndexes)) {
                continue;
            }

            if (!TrySelectReplayPairIndexes(entry.Value, outputIndexes, out var selectedCallIndex, out var selectedOutputIndex)) {
                continue;
            }

            selectedCallIndexById[callId] = selectedCallIndex;
            selectedOutputIndexById[callId] = selectedOutputIndex;
        }

        if (selectedCallIndexById.Count == 0) {
            var noToolReplay = new List<JsonObject>(normalized.Count);
            for (var i = 0; i < normalized.Count; i++) {
                var item = normalized[i];
                var type = (GetStringIgnoreCase(item, "type") ?? string.Empty).Trim();
                if (!IsToolCallInputType(type) && !IsToolOutputInputType(type)) {
                    noToolReplay.Add(item);
                }
            }
            return noToolReplay;
        }

        var filtered = new List<JsonObject>(normalized.Count);
        for (var i = 0; i < normalized.Count; i++) {
            var item = normalized[i];
            var type = (GetStringIgnoreCase(item, "type") ?? string.Empty).Trim();
            if (!IsToolCallInputType(type) && !IsToolOutputInputType(type)) {
                filtered.Add(item);
                continue;
            }

            var callId = ExtractToolReplayCallId(item);
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            if (IsToolCallInputType(type)
                && selectedCallIndexById.TryGetValue(callId!, out var selectedCallIndex)
                && selectedCallIndex == i) {
                filtered.Add(item);
                continue;
            }

            if (IsToolOutputInputType(type)
                && selectedOutputIndexById.TryGetValue(callId!, out var selectedOutputIndex)
                && selectedOutputIndex == i) {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    private static bool IsToolCallInputType(string type) {
        return string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsToolOutputInputType(string type) {
        return string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "tool_call_output", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "function_call_output", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeToolCallInputShape(JsonObject message) {
        if (message is null) {
            return false;
        }

        var hasExplicitCallId = !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "call_id"))
                                || !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "tool_call_id"));
        var hasNameLikeField = !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "name"))
                               || GetObjectIgnoreCase(message, "function") is not null
                               || GetObjectIgnoreCase(message, "action") is not null;
        var hasInputLikeField = !string.IsNullOrWhiteSpace(GetStringOrSerializedIgnoreCase(message, "arguments"))
                                || !string.IsNullOrWhiteSpace(GetStringOrSerializedIgnoreCase(message, "input"));
        var hasMessageEnvelopeShape = !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "role"))
                                      || message.GetArray("content") is not null;
        if (hasExplicitCallId && (hasNameLikeField || hasInputLikeField)) {
            return true;
        }

        if (hasMessageEnvelopeShape) {
            return false;
        }

        var hasId = !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "id"));
        return hasId && hasNameLikeField && hasInputLikeField;
    }

    private static bool LooksLikeToolOutputInputShape(JsonObject message) {
        if (message is null) {
            return false;
        }

        var hasExplicitCallId = !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "call_id"))
                                || !string.IsNullOrWhiteSpace(GetStringIgnoreCase(message, "tool_call_id"));
        var hasOutputLikeField = !string.IsNullOrWhiteSpace(GetStringOrSerializedIgnoreCase(message, "output"))
                                 || !string.IsNullOrWhiteSpace(GetStringOrSerializedIgnoreCase(message, "result"));
        return hasExplicitCallId && hasOutputLikeField;
    }

    private static JsonObject NormalizeToolCallInputItem(JsonObject message) {
        var sourceId = GetStringIgnoreCase(message, "id");
        var callId = FirstNonEmpty(
            GetStringIgnoreCase(message, "call_id"),
            GetStringIgnoreCase(message, "tool_call_id"),
            sourceId);

        var function = GetObjectIgnoreCase(message, "function");
        var action = GetObjectIgnoreCase(message, "action");
        var name = FirstNonEmpty(
            GetStringIgnoreCase(message, "name"),
            function?.GetString("name"),
            action?.GetString("name"));
        var arguments = FirstNonEmpty(
            GetStringOrSerializedIgnoreCase(message, "arguments"),
            GetStringOrSerializedIgnoreCase(message, "input"),
            function is null ? null : GetStringOrSerializedIgnoreCase(function, "arguments"),
            action is null ? null : GetStringOrSerializedIgnoreCase(action, "arguments"));
        var normalizedArguments = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments!;
        var wireId = BuildCustomToolCallWireId(sourceId, callId, name, normalizedArguments);

        var normalized = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("id", wireId)
            .Add("input", normalizedArguments);
        if (!string.IsNullOrWhiteSpace(callId)) {
            normalized.Add("call_id", callId!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(name)) {
            normalized.Add("name", name!.Trim());
        }

        return normalized;
    }

    private static JsonObject NormalizeToolOutputInputItem(JsonObject message) {
        var callId = FirstNonEmpty(
            GetStringIgnoreCase(message, "call_id"),
            GetStringIgnoreCase(message, "tool_call_id"),
            GetStringIgnoreCase(message, "id"));
        var output = FirstNonEmpty(
            GetStringOrSerializedIgnoreCase(message, "output"),
            GetStringOrSerializedIgnoreCase(message, "result"),
            GetStringOrSerializedIgnoreCase(message, "content")) ?? string.Empty;

        var normalized = new JsonObject()
            .Add("type", "custom_tool_call_output")
            .Add("output", output);
        if (!string.IsNullOrWhiteSpace(callId)) {
            normalized.Add("call_id", callId!.Trim());
        }

        return normalized;
    }

    private static JsonObject RemoveLegacyArgumentsFieldForNonToolInput(JsonObject message) {
        var removed = false;
        var normalized = new JsonObject();
        foreach (var pair in message) {
            if (string.Equals(pair.Key, "arguments", StringComparison.OrdinalIgnoreCase)) {
                removed = true;
                continue;
            }

            normalized.Add(pair.Key, pair.Value ?? JsonValue.Null);
        }

        return removed ? normalized : message;
    }

    private static string? ExtractToolReplayCallId(JsonObject message) {
        return FirstNonEmpty(
            GetStringIgnoreCase(message, "call_id"),
            GetStringIgnoreCase(message, "tool_call_id"),
            GetStringIgnoreCase(message, "id"));
    }

    private static void AddReplayIndex(IDictionary<string, List<int>> indexesById, string callId, int index) {
        if (!indexesById.TryGetValue(callId, out var indexes)) {
            indexes = new List<int>();
            indexesById[callId] = indexes;
        }

        indexes.Add(index);
    }

    private static bool TrySelectReplayPairIndexes(
        IReadOnlyList<int> callIndexes,
        IReadOnlyList<int> outputIndexes,
        out int selectedCallIndex,
        out int selectedOutputIndex) {
        selectedCallIndex = -1;
        selectedOutputIndex = -1;
        if (callIndexes is null || outputIndexes is null || callIndexes.Count == 0 || outputIndexes.Count == 0) {
            return false;
        }

        var outputCursor = 0;
        for (var i = 0; i < callIndexes.Count; i++) {
            var callIndex = callIndexes[i];
            while (outputCursor < outputIndexes.Count && outputIndexes[outputCursor] <= callIndex) {
                outputCursor++;
            }

            if (outputCursor >= outputIndexes.Count) {
                break;
            }

            selectedCallIndex = callIndex;
            selectedOutputIndex = outputIndexes[outputCursor];
        }

        return selectedCallIndex >= 0 && selectedOutputIndex >= 0;
    }

    private static string BuildCustomToolCallWireId(string? sourceId, string? callId, string? name, string arguments) {
        var candidate = FirstNonEmpty(sourceId, callId, name);
        if (!string.IsNullOrWhiteSpace(candidate)) {
            var trimmed = candidate!.Trim();
            if (trimmed.StartsWith("ctc", StringComparison.OrdinalIgnoreCase)) {
                return trimmed;
            }
        }

        var seed = string.IsNullOrWhiteSpace(candidate)
            ? "call"
            : candidate!.Trim();
        var sb = new StringBuilder(seed.Length);
        for (var i = 0; i < seed.Length; i++) {
            var c = seed[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') {
                sb.Append(c);
            }
        }

        if (sb.Length == 0) {
            sb.Append("call");
        }

        if (sb.Length > 48) {
            sb.Length = 48;
        }

        var hash = Math.Abs((arguments ?? string.Empty).GetHashCode()).ToString("x");
        return "ctc_" + sb + "_" + hash;
    }

    private static string? GetStringIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value?.AsString();
            }
        }

        return null;
    }

    private static JsonObject? GetObjectIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value?.AsObject();
            }
        }

        return null;
    }

    private static string? GetStringOrSerializedIgnoreCase(JsonObject source, string key) {
        if (source is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var pair in source) {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var asString = pair.Value?.AsString();
            if (!string.IsNullOrWhiteSpace(asString)) {
                return asString;
            }

            if (pair.Value is not null) {
                return JsonLite.Serialize(pair.Value);
            }

            return null;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) {
        if (values is null || values.Length == 0) {
            return null;
        }

        for (var i = 0; i < values.Length; i++) {
            var value = values[i];
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }
}
