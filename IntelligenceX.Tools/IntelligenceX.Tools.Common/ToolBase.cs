using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Common base class for tool implementations to avoid repeating invocation boilerplate.
/// </summary>
/// <remarks>
/// Derived tools implement <see cref="InvokeCoreAsync"/> and return an already-shaped tool output envelope
/// (typically via <see cref="ToolResponse.Ok"/>/<see cref="ToolResponse.Error"/>). Unhandled exceptions are
/// converted to a standard <c>error_code=exception</c> envelope by <see cref="ToolInvoker"/>.
/// </remarks>
public abstract class ToolBase : ITool {
    /// <inheritdoc />
    public abstract ToolDefinition Definition { get; }

    /// <inheritdoc />
    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ToolInvoker.RunAsync(cancellationToken, () => InvokeCoreAsync(arguments, cancellationToken));
    }

    /// <summary>
    /// Tool-specific implementation.
    /// </summary>
    /// <remarks>
    /// Do not wrap this in a generic try/catch. Let the base class normalize unhandled exceptions.
    /// Catch only when mapping to a specific <c>error_code</c> is required.
    /// </remarks>
    protected abstract Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken);
}

