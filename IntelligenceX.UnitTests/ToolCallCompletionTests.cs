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
