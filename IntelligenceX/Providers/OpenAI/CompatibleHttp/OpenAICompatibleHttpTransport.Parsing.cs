using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Telemetry;
using IntelligenceX.Tools;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.CompatibleHttp;

internal sealed partial class OpenAICompatibleHttpTransport : IOpenAITransport {
    private static ChatCompletionResponse BuildTurnFromChatCompletions(JsonObject responseObj) {
        // OpenAI-compatible chat completions response shape.
        var choices = responseObj.GetArray("choices");
        var first = choices?.Count > 0 ? choices[0].AsObject() : null;
        var message = first?.GetObject("message");
        if (message is null) {
            throw new InvalidOperationException("Invalid chat response (missing choices[0].message).");
        }

        var assistantMessage = new JsonObject()
            .Add("role", "assistant");

        var content = ExtractMessageContentText(message);
        if (content is not null) {
            assistantMessage.Add("content", content);
        }

        var toolCalls = ExtractMessageToolCalls(message);
        if (toolCalls is not null) {
            assistantMessage.Add("tool_calls", toolCalls);
        }

        var usage = responseObj.GetObject("usage");
        var turn = BuildTurnFromAssistantMessage(assistantMessage, usage);
        return new ChatCompletionResponse(turn, assistantMessage);
    }

    private static string? ExtractMessageContentText(JsonObject message) {
        var direct = message.GetString("content");
        if (!string.IsNullOrWhiteSpace(direct)) {
            return direct;
        }

        var contentParts = message.GetArray("content");
        if (contentParts is null || contentParts.Count == 0) {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < contentParts.Count; i++) {
            var part = contentParts[i].AsObject();
            if (part is null) {
                continue;
            }

            var partType = (part.GetString("type") ?? string.Empty).Trim();
            var partText = string.Equals(partType, "refusal", StringComparison.OrdinalIgnoreCase)
                ? (part.GetString("refusal") ?? part.GetString("text"))
                : (part.GetString("text") ?? part.GetString("content"));
            if (string.IsNullOrWhiteSpace(partText)) {
                continue;
            }

            if (builder.Length > 0) {
                builder.AppendLine();
            }

            builder.Append(partText!.Trim());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static JsonArray? ExtractMessageToolCalls(JsonObject message) {
        var toolCalls = message.GetArray("tool_calls");
        if (toolCalls is { Count: > 0 }) {
            return toolCalls;
        }

        var contentParts = message.GetArray("content");
        if (contentParts is null || contentParts.Count == 0) {
            return null;
        }

        var parsedToolCalls = new JsonArray();
        for (var i = 0; i < contentParts.Count; i++) {
            var part = contentParts[i].AsObject();
            if (part is null) {
                continue;
            }

            var partType = (part.GetString("type") ?? string.Empty).Trim();
            if (!partType.Equals("tool_call", StringComparison.OrdinalIgnoreCase)
                && !partType.Equals("function_call", StringComparison.OrdinalIgnoreCase)
                && !partType.Equals("custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var function = part.GetObject("function");
            var name = (function?.GetString("name") ?? part.GetString("name") ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            var id = (part.GetString("id") ?? part.GetString("call_id") ?? $"call_{parsedToolCalls.Count}").Trim();
            var arguments = function?.GetString("arguments") ?? part.GetString("arguments") ?? "{}";

            parsedToolCalls.Add(new JsonObject()
                .Add("id", id)
                .Add("type", "function")
                .Add("function", new JsonObject()
                    .Add("name", name)
                    .Add("arguments", arguments)));
        }

        return parsedToolCalls.Count == 0 ? null : parsedToolCalls;
    }

    private static string? ExtractDeltaContentText(JsonObject delta) {
        var direct = delta.GetString("content");
        if (!string.IsNullOrEmpty(direct)) {
            return direct;
        }

        var contentParts = delta.GetArray("content");
        if (contentParts is null || contentParts.Count == 0) {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < contentParts.Count; i++) {
            var part = contentParts[i].AsObject();
            if (part is null) {
                continue;
            }

            var partType = (part.GetString("type") ?? string.Empty).Trim();
            var partText = string.Equals(partType, "refusal", StringComparison.OrdinalIgnoreCase)
                ? (part.GetString("refusal") ?? part.GetString("text"))
                : (part.GetString("text") ?? part.GetString("content"));
            if (string.IsNullOrEmpty(partText)) {
                continue;
            }

            builder.Append(partText);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static JsonArray? ExtractDeltaToolCallsFromContent(JsonObject delta) {
        var contentParts = delta.GetArray("content");
        if (contentParts is null || contentParts.Count == 0) {
            return null;
        }

        var toolCalls = new JsonArray();
        for (var i = 0; i < contentParts.Count; i++) {
            var part = contentParts[i].AsObject();
            if (part is null) {
                continue;
            }

            var partType = (part.GetString("type") ?? string.Empty).Trim();
            if (!partType.Equals("tool_call", StringComparison.OrdinalIgnoreCase)
                && !partType.Equals("function_call", StringComparison.OrdinalIgnoreCase)
                && !partType.Equals("custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var output = new JsonObject();
            var index = part.GetInt64("index");
            if (index.HasValue) {
                output.Add("index", index.Value);
            }

            var id = part.GetString("id") ?? part.GetString("call_id");
            if (!string.IsNullOrWhiteSpace(id)) {
                output.Add("id", id);
            }

            var function = part.GetObject("function");
            if (function is null) {
                var name = part.GetString("name");
                var arguments = part.GetString("arguments");
                if (!string.IsNullOrWhiteSpace(name) || arguments is not null) {
                    function = new JsonObject();
                    if (!string.IsNullOrWhiteSpace(name)) {
                        function.Add("name", name);
                    }

                    if (arguments is not null) {
                        function.Add("arguments", arguments);
                    }
                }
            }

            if (function is not null) {
                output.Add("function", function);
            }

            toolCalls.Add(output);
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private static TurnInfo BuildTurnFromAssistantMessage(JsonObject assistantMessageForHistory, JsonObject? usageObj) {
        var outputs = new List<TurnOutput>();
        var rawOutputs = new JsonArray();

        var toolCalls = assistantMessageForHistory.GetArray("tool_calls");
        if (toolCalls is not null && toolCalls.Count > 0) {
            for (var i = 0; i < toolCalls.Count; i++) {
                var tool = toolCalls[i].AsObject();
                if (tool is null) {
                    continue;
                }

                // Convert to the output item shape used across IntelligenceX (so ToolCallParser can extract).
                var outputObj = new JsonObject()
                    .Add("type", "tool_call");

                var id = tool.GetString("id");
                if (!string.IsNullOrWhiteSpace(id)) {
                    outputObj.Add("id", id!.Trim());
                    outputObj.Add("tool_call_id", id!.Trim());
                    outputObj.Add("call_id", id!.Trim());
                }

                var function = tool.GetObject("function");
                if (function is not null) {
                    outputObj.Add("function", function);

                    // Also include OpenAI-style fields at the root so ToolCall.FromJson can parse arguments reliably.
                    var name = function.GetString("name");
                    if (!string.IsNullOrWhiteSpace(name)) {
                        outputObj.Add("name", name!.Trim());
                    }
                    var args = function.GetString("arguments");
                    if (args is not null) {
                        outputObj.Add("arguments", args);
                    }
                }

                rawOutputs.Add(outputObj);
                outputs.Add(new TurnOutput(
                    type: "tool_call",
                    text: null,
                    imageUrl: null,
                    imagePath: null,
                    base64: null,
                    mimeType: null,
                    raw: outputObj,
                    additional: null));
            }
        }

        var content = assistantMessageForHistory.GetString("content");
        if (!string.IsNullOrWhiteSpace(content)) {
            var outputObj = new JsonObject()
                .Add("type", "text")
                .Add("text", content);
            rawOutputs.Add(outputObj);
            outputs.Add(new TurnOutput(
                type: "text",
                text: content,
                imageUrl: null,
                imagePath: null,
                base64: null,
                mimeType: null,
                raw: outputObj,
                additional: null));
        }

        var turnId = Guid.NewGuid().ToString("N");
        var turnRaw = new JsonObject()
            .Add("id", turnId)
            .Add("status", "completed")
            .Add("outputs", rawOutputs);

        TurnUsage? usage = null;
        if (usageObj is not null) {
            usage = TurnUsage.FromJson(usageObj);
            turnRaw.Add("usage", usageObj);
        }

        return new TurnInfo(turnId, responseId: null, status: "completed", outputs, Array.Empty<TurnOutput>(), turnRaw, additional: null, usage);
    }

    private static JsonObject BuildAssistantMessageForHistory(string content, Dictionary<int, ToolCallBuilder> toolCallsByIndex) {
        var msg = new JsonObject()
            .Add("role", "assistant");

        if (!string.IsNullOrEmpty(content)) {
            msg.Add("content", content);
        }

        if (toolCallsByIndex.Count > 0) {
            var arr = new JsonArray();
            foreach (var kvp in toolCallsByIndex.OrderBy(k => k.Key)) {
                var builder = kvp.Value;
                var id = string.IsNullOrWhiteSpace(builder.Id) ? $"call_{kvp.Key}" : builder.Id!.Trim();
                var name = string.IsNullOrWhiteSpace(builder.Name) ? "unknown_tool" : builder.Name!.Trim();
                var args = builder.Arguments.Length == 0 ? "{}" : builder.Arguments.ToString();

                var toolObj = new JsonObject()
                    .Add("id", id)
                    .Add("type", "function")
                    .Add("function", new JsonObject()
                        .Add("name", name)
                        .Add("arguments", args));
                arr.Add(toolObj);
            }
            msg.Add("tool_calls", arr);
        }

        return msg;
    }

    private static List<JsonObject> BuildMessagesFromInput(ChatInput input) {
        var items = input.ToJson();
        if (items is null || items.Count == 0) {
            return new List<JsonObject>();
        }

        var messages = new List<JsonObject>();
        var userText = new StringBuilder();

        void FlushUserText() {
            if (userText.Length == 0) {
                return;
            }
            messages.Add(new JsonObject()
                .Add("role", "user")
                .Add("content", userText.ToString()));
            userText.Clear();
        }

        for (var i = 0; i < items.Count; i++) {
            var item = items[i].AsObject();
            if (item is null) {
                continue;
            }

            var type = item.GetString("type") ?? string.Empty;
            switch (type) {
                case "text": {
                        var text = item.GetString("text");
                        if (string.IsNullOrWhiteSpace(text)) {
                            continue;
                        }

                        if (userText.Length > 0) {
                            userText.Append('\n');
                        }
                        userText.Append(text);
                        break;
                    }
                case "custom_tool_call": {
                        FlushUserText();
                        var callId = item.GetString("call_id");
                        if (string.IsNullOrWhiteSpace(callId)) {
                            throw new InvalidOperationException("Tool call item is missing call_id.");
                        }

                        var name = item.GetString("name");
                        if (string.IsNullOrWhiteSpace(name)) {
                            throw new InvalidOperationException("Tool call item is missing name.");
                        }

                        var arguments = item.GetString("input");
                        if (string.IsNullOrWhiteSpace(arguments)) {
                            arguments = item.GetString("arguments");
                        }
                        var normalizedArguments = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments!.Trim();

                        messages.Add(new JsonObject()
                            .Add("role", "assistant")
                            .Add("content", JsonValue.Null)
                            .Add("tool_calls", new JsonArray()
                                .Add(new JsonObject()
                                    .Add("id", callId!.Trim())
                                    .Add("type", "function")
                                    .Add("function", new JsonObject()
                                        .Add("name", name!.Trim())
                                        .Add("arguments", normalizedArguments)))));
                        break;
                    }
                case "custom_tool_call_output": {
                        FlushUserText();
                        var callId = item.GetString("call_id");
                        if (string.IsNullOrWhiteSpace(callId)) {
                            throw new InvalidOperationException("Tool output item is missing call_id.");
                        }

                        var output = item.GetString("output") ?? string.Empty;
                        messages.Add(new JsonObject()
                            .Add("role", "tool")
                            .Add("tool_call_id", callId!.Trim())
                            .Add("content", output));
                        break;
                    }
                case "image":
                    throw new NotSupportedException("CompatibleHttp transport does not currently support image inputs.");
                default:
                    throw new NotSupportedException($"Unsupported chat input item type '{type}'.");
            }
        }

        FlushUserText();

        return messages;
    }

}
