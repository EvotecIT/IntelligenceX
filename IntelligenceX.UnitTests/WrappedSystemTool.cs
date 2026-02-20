using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.System;

internal sealed class WrappedSystemTool : ITool {
    public WrappedSystemTool(ToolDefinition definition) {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ToolDefinition Definition { get; }

    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        _ = arguments;
        _ = cancellationToken;
        return Task.FromResult("{}");
    }
}
