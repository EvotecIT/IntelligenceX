using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Tools;

/// <summary>
/// Represents a tool that can be called by the model.
/// </summary>
public interface ITool {
    /// <summary>
    /// Tool definition metadata.
    /// </summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Executes the tool with the provided arguments.
    /// </summary>
    /// <param name="arguments">Parsed tool arguments (may be null).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool output text.</returns>
    Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken);
}
