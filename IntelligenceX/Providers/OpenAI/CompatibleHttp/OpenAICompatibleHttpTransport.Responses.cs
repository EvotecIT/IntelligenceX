using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.CompatibleHttp;

internal partial class OpenAICompatibleHttpTransport {
    // Kept only in local history. It is removed before any chat-completions request is serialized.
    private const string ResponseReplayKey = "__intelligencex_response_items";

    private static JsonObject BuildResponsesRequest(string model, IReadOnlyList<JsonObject> messages, ChatOptions options, bool streaming) {
        var input = new JsonArray();
        foreach (var message in messages) {
            string role = message.GetString("role") ?? "user";
            if (message.GetArray(ResponseReplayKey) is { } replay) {
                var calls = message.GetArray("tool_calls");
                foreach (var value in replay) {
                    var item = value.AsObject();
                    if (item is null) continue;
                    if (item.GetString("type") == "function_call" &&
                        (calls is null || !calls.Any(call => call.AsObject()?.GetString("id") == item.GetString("call_id")))) continue;
                    input.Add(item);
                }
                continue;
            }
            if (role == "tool") {
                input.Add(new JsonObject().Add("type", "function_call_output")
                    .Add("call_id", message.GetString("tool_call_id")).Add("output", message.GetString("content") ?? string.Empty));
                continue;
            }
            var content = new JsonArray();
            if (message.GetString("content") is { Length: > 0 } text)
                content.Add(new JsonObject().Add("type", role == "assistant" ? "output_text" : "input_text").Add("text", text));
            if (message.GetArray("content") is { } parts) {
                foreach (var value in parts) {
                    var part = value.AsObject();
                    if (part?.GetString("type") == "text")
                        content.Add(new JsonObject().Add("type", role == "assistant" ? "output_text" : "input_text").Add("text", part.GetString("text")));
                    else if (part?.GetString("type") == "image_url")
                        content.Add(new JsonObject().Add("type", "input_image").Add("image_url", part.GetObject("image_url")?.GetString("url")));
                    else throw new NotSupportedException("The Responses protocol cannot represent this input part.");
                }
            }
            if (content.Count > 0) input.Add(new JsonObject().Add("type", "message").Add("role", role).Add("content", content));
            if (message.GetArray("tool_calls") is { } toolCalls) {
                foreach (var value in toolCalls) {
                    var call = value.AsObject();
                    var function = call?.GetObject("function");
                    if (call is null || function is null) throw new InvalidOperationException("Invalid tool call history.");
                    input.Add(new JsonObject().Add("type", "function_call").Add("call_id", call.GetString("id"))
                        .Add("name", function.GetString("name")).Add("arguments", function.GetString("arguments") ?? "{}"));
                }
            }
        }
        var body = new JsonObject().Add("model", model).Add("input", input).Add("store", false).Add("stream", streaming)
            .Add("include", new JsonArray().Add("reasoning.encrypted_content"));
        var textOptions = new JsonObject();
        if (options.ResponseFormat is not null) textOptions.Add("format", options.ResponseFormat.ToJsonSchema().Add("type", "json_schema"));
        if (options.TextVerbosity.HasValue) textOptions.Add("verbosity", options.TextVerbosity.Value.ToApiString());
        if (textOptions.Count > 0) body.Add("text", textOptions);
        if (options.Temperature.HasValue) body.Add("temperature", options.Temperature.Value);
        if (options.ReasoningEffort.HasValue || options.ReasoningSummary.HasValue) {
            var reasoning = new JsonObject();
            if (options.ReasoningEffort.HasValue) reasoning.Add("effort", options.ReasoningEffort.Value.ToApiString());
            if (options.ReasoningSummary.HasValue) reasoning.Add("summary", options.ReasoningSummary.Value.ToApiString());
            body.Add("reasoning", reasoning);
        }
        if (options.Tools is { Count: > 0 }) {
            var tools = new JsonArray();
            foreach (var definition in options.Tools) {
                if (definition is null) continue;
                var function = new JsonObject().Add("type", "function").Add("name", definition.Name)
                    .Add("description", definition.GetDescriptionWithTags() ?? string.Empty);
                if (definition.Parameters is not null) function.Add("parameters", definition.Parameters);
                tools.Add(function);
            }
            body.Add("tools", tools);
            if (options.ToolChoice is not null) {
                var choice = BuildToolChoice(options.ToolChoice);
                body.Add("tool_choice", choice.AsObject()?.GetObject("function") is { } function
                    ? JsonValue.From(new JsonObject().Add("type", "function").Add("name", function.GetString("name"))) : choice);
            }
        }
        if (options.ParallelToolCalls.HasValue) body.Add("parallel_tool_calls", options.ParallelToolCalls.Value);
        return body;
    }

    private async Task<ChatCompletionResponse> SendResponsesAsync(JsonObject body, long? maximum, CancellationToken cancellationToken) {
        if (maximum.HasValue && maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_apiBase, "responses")) {
            Content = new StringContent(JsonLite.Serialize(body), Encoding.UTF8, "application/json")
        };
        await PrepareRequestAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = await TaskCancellation.WaitAsync(_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken), cancellationToken, abandoned => abandoned.Dispose()).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        JsonObject? result;
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)) {
            using var stream = new ResponseBudgetStream(await ReadAsStreamAsync(response.Content, cancellationToken).ConfigureAwait(false), maximum);
            using var registration = cancellationToken.Register(stream.Dispose);
            result = null;
            try {
                await OpenAINativeSseParser.ParseAsync(stream, evt => {
                    string? type = evt.GetString("type");
                    if (type is ("response.output_text.delta" or "response.refusal.delta") && evt.GetString("delta") is { } delta)
                        ObserverDispatcher.Raise(DeltaReceived, this, delta);
                    if (type == "response.completed" || type == "response.incomplete") result = evt.GetObject("response");
                    if (type == "response.failed" || type == "error") throw new InvalidOperationException("The provider failed the Responses request.");
                    return Task.CompletedTask;
                }, cancellationToken, allowSensitiveDiagnostics: false, stopOnTerminalResponse: true).ConfigureAwait(false);
            } catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            if (result is null) throw new InvalidDataException("The Responses stream ended without a terminal response.");
        } else {
            var payload = await ResponseBudgetStream.ReadTextAsync(response.Content, maximum, cancellationToken).ConfigureAwait(false);
            result = JsonLite.Parse(payload)?.AsObject() ?? throw new InvalidDataException("Expected a Responses JSON object.");
        }
        if (result.GetString("status") is not ("completed" or "incomplete"))
            throw new InvalidDataException("The provider did not return a completed or incomplete response.");
        var output = result.GetArray("output") ?? throw new InvalidDataException("The response did not contain output items.");
        var history = new JsonObject().Add("role", "assistant").Add(ResponseReplayKey, output);
        var text = new StringBuilder();
        var calls = new JsonArray();
        foreach (var value in output) {
            var item = value.AsObject();
            if (item?.GetString("type") == "message" && item.GetArray("content") is { } parts) {
                foreach (var part in parts) {
                    var obj = part.AsObject();
                    if (obj?.GetString("type") == "output_text") text.Append(obj.GetString("text"));
                    else if (obj?.GetString("type") == "refusal") text.Append(obj.GetString("refusal"));
                }
            } else if (item?.GetString("type") == "function_call") {
                calls.Add(new JsonObject().Add("id", item.GetString("call_id")).Add("type", "function")
                    .Add("function", new JsonObject().Add("name", item.GetString("name")).Add("arguments", item.GetString("arguments") ?? "{}")));
            }
        }
        if (text.Length > 0) history.Add("content", text.ToString());
        if (calls.Count > 0) history.Add("tool_calls", calls);
        var converted = BuildTurnFromAssistantMessage(history, result.GetObject("usage"), result.GetString("status") == "completed" ? "stop" : "length");
        var turn = new TurnInfo(converted.Id, result.GetString("id"), converted.Status, converted.Outputs, converted.ImageOutputs, result, null, converted.Usage);
        return new ChatCompletionResponse(turn, history);
    }
}
