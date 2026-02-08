using System;
using System.Collections.Generic;
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
        ToolWireFormat toolWireFormat = ToolWireFormat.CustomParameters) {
        var input = new JsonArray();
        foreach (var message in messages) {
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

        if (!string.IsNullOrWhiteSpace(options.PreviousResponseId)) {
            body.Add("previous_response_id", options.PreviousResponseId);
        }

        if (options.Tools is not null && options.Tools.Count > 0) {
            var tools = new JsonArray();
            foreach (var tool in options.Tools) {
                tools.Add(SerializeToolDefinition(tool, toolWireFormat));
            }
            body.Add("tools", tools);

            var choice = options.ToolChoice ?? ToolChoice.Auto;
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

    private static object SerializeToolChoice(ToolChoice choice, ToolWireFormat toolWireFormat) {
        if (string.Equals(choice.Type, "custom", StringComparison.OrdinalIgnoreCase)) {
            var name = choice.Name ?? string.Empty;
            var isFunctionWireFormat = toolWireFormat == ToolWireFormat.FunctionFlatParameters ||
                                       toolWireFormat == ToolWireFormat.FunctionFlatInputSchema;
            if (isFunctionWireFormat) {
                // When falling back to function-style tools, forced tool choice must also be expressed as function-style.
                // Using the standard OpenAI schema: { type: "function", function: { name: "..." } }.
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

        // Defensive: ToolChoice has a private constructor but keep wire output constrained.
        return "auto";
    }

    private static JsonObject SerializeToolDefinition(ToolDefinition tool, ToolWireFormat toolWireFormat) {
        switch (toolWireFormat) {
            case ToolWireFormat.CustomInputSchema:
            case ToolWireFormat.CustomParameters: {
                var obj = new JsonObject()
                    .Add("type", "custom")
                    .Add("name", tool.Name);
                if (!string.IsNullOrWhiteSpace(tool.Description)) {
                    obj.Add("description", tool.Description);
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
                if (!string.IsNullOrWhiteSpace(tool.Description)) {
                    obj.Add("description", tool.Description);
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
}
