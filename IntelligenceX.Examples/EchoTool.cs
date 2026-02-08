using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Examples;

internal sealed class EchoTool : ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "echo",
        "Echo back the provided text.",
        new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("text", new JsonObject()
                    .Add("type", "string")
                    .Add("description", "Text to echo back.")))
            .Add("required", new JsonArray().Add("text"))
            .Add("additionalProperties", false));

    public ToolDefinition Definition => DefinitionValue;

    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var text = arguments?.GetString("text") ?? string.Empty;
        return Task.FromResult(text);
    }
}

