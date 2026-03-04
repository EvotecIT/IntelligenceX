using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Adapter abstraction for typed tool wrappers that want to package bind/execute/reliability concerns together.
/// </summary>
/// <typeparam name="TRequest">Typed request model.</typeparam>
public abstract class ToolRequestAdapter<TRequest> where TRequest : notnull {
    /// <summary>
    /// Optional reliability profile for the wrapped pipeline execution.
    /// </summary>
    public virtual ToolPipelineReliabilityOptions? Reliability => null;

    /// <summary>
    /// Optional middleware chain for the wrapped pipeline execution.
    /// </summary>
    public virtual IReadOnlyList<ToolPipelineMiddleware<TRequest>>? Middleware => null;

    /// <summary>
    /// Binds raw tool arguments into a typed request model.
    /// </summary>
    public abstract ToolRequestBindingResult<TRequest> Bind(JsonObject? arguments);

    /// <summary>
    /// Executes the typed request and returns a serialized tool envelope.
    /// </summary>
    public abstract Task<string> ExecuteAsync(
        ToolPipelineContext<TRequest> context,
        CancellationToken cancellationToken);
}

