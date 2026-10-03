using System.Net;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolCallCompletionTests {
    [Theory]
    [InlineData("length", false)]
    [InlineData("tool_calls", true)]
    public async Task RunnerDoesNotExecuteCallsFromATruncatedCompletion(string finishReason, bool execute) {
        int requests = 0;
        using var http = new HttpClient(new Handler(_ => {
            requests++;
            string body = requests == 1
                ? JsonSerializer.Serialize(new { choices = new[] { new {
                    finish_reason = finishReason,
                    message = new { role = "assistant", tool_calls = new[] { new {
                        id = "call-id", type = "function", function = new { name = "fixture_echo", arguments = "{\"text\":\"hello\"}" }
                    } } }
                } } })
                : "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"done\"}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }));
        using var client = new IntelligenceXClient(new OpenAICompatibleHttpTransport(new() {
            BaseUrl = "http://127.0.0.1:11434/v1", AllowInsecureHttp = true, Streaming = false
        }, http), "model", null, null, null);
        var tool = new EchoTool();
        var registry = new ToolRegistry();
        registry.Register(tool);

        var result = await ToolRunner.RunAsync(client, ChatInput.FromText("echo"), new() { Model = "model" }, registry);

        Assert.Equal(execute ? 1 : 0, tool.Invocations);
        Assert.Equal(execute ? 2 : 1, requests);
        Assert.Equal(execute ? "completed" : "incomplete", result.FinalTurn.Status);
        Assert.Equal(execute ? 1 : 0, result.ToolCalls.Count);
        Assert.Equal(execute ? 1 : 0, result.ToolOutputs.Count);
    }

    [Theory]
    [InlineData("incomplete", "completed", false)]
    [InlineData("completed", "incomplete", false)]
    [InlineData("completed", "in_progress", false)]
    [InlineData("completed", "completed", true)]
    [InlineData(null, null, true)]
    public void ExtractionRespectsExplicitTurnAndCallCompletion(string? turnStatus, string? callStatus, bool executable) {
        var item = new JsonObject().Add("type", "function_call").Add("call_id", "call-id")
            .Add("name", "fixture_echo").Add("arguments", "{}").Add("status", callStatus);
        var turn = TurnInfo.FromJson(new JsonObject().Add("id", "turn-id").Add("status", turnStatus)
            .Add("output", new JsonArray().Add(item)));

        Assert.Equal(executable ? 1 : 0, ToolCallParser.Extract(turn).Count);
        Assert.Single(turn.Outputs);
    }

    [Theory]
    [InlineData("incomplete", false)]
    [InlineData("in_progress", false)]
    [InlineData("completed", true)]
    [InlineData(null, true)]
    public async Task ResponsesAdapterPreservesCallCompletionForExecution(string? callStatus, bool execute) {
        int requests = 0;
        using var client = await IntelligenceXClient.ConnectAsync(new() {
            TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = "model",
            CopilotOptions = new() {
                GitHubToken = "fixture-token", Streaming = false,
                HttpMessageHandler = new Handler(request => {
                    if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                        Content = new StringContent("{\"data\":[{\"id\":\"model\",\"supported_endpoints\":[\"/responses\"]}]}")
                    });
                    Assert.Equal("/responses", request.RequestUri!.AbsolutePath);
                    string body = ++requests == 1
                        ? JsonSerializer.Serialize(new { id = "response-id", status = "completed", output = new[] { new {
                            type = "function_call", call_id = "call-id", name = "fixture_echo",
                            arguments = "{\"text\":\"hello\"}", status = callStatus
                        } } })
                        : "{\"id\":\"next-id\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"done\"}]}]}";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
                })
            }
        });
        var tool = new EchoTool();
        var registry = new ToolRegistry();
        registry.Register(tool);

        var result = await ToolRunner.RunAsync(client, ChatInput.FromText("echo"), new() { Model = "model" }, registry);

        Assert.Equal(execute ? 1 : 0, tool.Invocations);
        Assert.Equal(execute ? 2 : 1, requests);
        Assert.Equal(execute ? 1 : 0, result.ToolCalls.Count);
        if (!execute) Assert.Equal(callStatus, Assert.Single(result.FinalTurn.Outputs).Raw.GetString("status"));
    }

    [Fact]
    public async Task ContinuationAfterAnUnfinishedCallRetainsAssistantText() {
        int requests = 0;
        using var http = new HttpClient(new Handler(async request => {
            requests++;
            if (requests == 3) {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToArray();
                Assert.Contains(messages, item => item.GetProperty("role").GetString() == "assistant"
                    && item.GetProperty("content").GetString() == "earlier answer");
                Assert.Contains(messages, item => item.GetProperty("role").GetString() == "assistant"
                    && item.GetProperty("content").GetString() == "partial answer");
                Assert.DoesNotContain(messages, item => item.TryGetProperty("tool_calls", out _) || item.GetProperty("role").GetString() == "tool");
            }
            string body = requests == 2
                ? "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"partial answer\",\"tool_calls\":[{\"id\":\"call-id\",\"type\":\"function\",\"function\":{\"name\":\"fixture_echo\",\"arguments\":\"{\\\"text\\\":\\\"hello\\\"}\"}}]}}]}"
                : JsonSerializer.Serialize(new { choices = new[] { new {
                    finish_reason = "stop", message = new { role = "assistant", content = requests == 1 ? "earlier answer" : "done" }
                } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        using var client = new IntelligenceXClient(new OpenAICompatibleHttpTransport(new() {
            BaseUrl = "http://127.0.0.1:11434/v1", AllowInsecureHttp = true, Streaming = false
        }, http), "model", null, null, null);
        var tool = new EchoTool();
        var registry = new ToolRegistry();
        registry.Register(tool);

        await client.ChatAsync("first question");
        var partial = await ToolRunner.RunAsync(client, ChatInput.FromText("echo"), new() { Model = "model" }, registry);
        Assert.Equal("incomplete", partial.FinalTurn.Status);
        Assert.Equal(0, tool.Invocations);
        Assert.Equal("done", EasyChatResult.FromTurn(await client.ChatAsync("continue")).Text);
        Assert.Equal(3, requests);
    }

    private sealed class EchoTool : ITool {
        internal int Invocations { get; private set; }
        public ToolDefinition Definition { get; } = new("fixture_echo", tags: new[] { "pack:fixture" });
        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            Invocations++;
            Assert.Equal("hello", arguments?.GetString("text"));
            return Task.FromResult("hello");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
