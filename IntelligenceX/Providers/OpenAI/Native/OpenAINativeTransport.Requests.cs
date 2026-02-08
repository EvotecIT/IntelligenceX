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
    private JsonObject BuildRequestBody(string model, IReadOnlyList<JsonObject> messages, string sessionId, ChatOptions options) {
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
                tools.Add(SerializeToolDefinition(tool));
            }
            body.Add("tools", tools);

            var choice = options.ToolChoice ?? ToolChoice.Auto;
            if (string.Equals(choice.Type, "custom", StringComparison.OrdinalIgnoreCase)) {
                body.Add("tool_choice", new JsonObject()
                    .Add("type", "custom")
                    .Add("name", choice.Name ?? string.Empty));
            } else {
                body.Add("tool_choice", choice.Type);
            }

            if (options.ParallelToolCalls.HasValue) {
                body.Add("parallel_tool_calls", options.ParallelToolCalls.Value);
            } else {
                body.Add("parallel_tool_calls", true);
            }
        }

        return body;
    }

    private static JsonObject SerializeToolDefinition(ToolDefinition tool) {
        var obj = new JsonObject()
            .Add("type", "custom")
            .Add("name", tool.Name);
        if (!string.IsNullOrWhiteSpace(tool.Description)) {
            obj.Add("description", tool.Description);
        }
        if (tool.Parameters is not null) {
            // OpenAI Responses API expects custom tools to use `input_schema` (not `parameters`).
            obj.Add("input_schema", tool.Parameters);
        }
        return obj;
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

