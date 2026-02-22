using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Terminal pipeline delegate.
/// </summary>
public delegate Task<string> ToolPipelineNext<TRequest>(
    ToolPipelineContext<TRequest> context,
    CancellationToken cancellationToken)
    where TRequest : notnull;

/// <summary>
/// Middleware delegate for typed tool execution pipelines.
/// </summary>
public delegate Task<string> ToolPipelineMiddleware<TRequest>(
    ToolPipelineContext<TRequest> context,
    CancellationToken cancellationToken,
    ToolPipelineNext<TRequest> next)
    where TRequest : notnull;

/// <summary>
/// Context available to typed tool pipelines.
/// </summary>
public sealed class ToolPipelineContext<TRequest> where TRequest : notnull {
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new pipeline context.
    /// </summary>
    public ToolPipelineContext(ToolDefinition definition, JsonObject? arguments, TRequest request) {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Arguments = arguments;
    }

    /// <summary>
    /// Tool definition being executed.
    /// </summary>
    public ToolDefinition Definition { get; }

    /// <summary>
    /// Raw tool arguments.
    /// </summary>
    public JsonObject? Arguments { get; }

    /// <summary>
    /// Typed request produced by the binder stage.
    /// </summary>
    public TRequest Request { get; }

    /// <summary>
    /// Stores middleware state.
    /// </summary>
    public void SetItem<TValue>(string key, TValue value) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        _items[key.Trim()] = value;
    }

    /// <summary>
    /// Resolves middleware state.
    /// </summary>
    public bool TryGetItem<TValue>(string key, out TValue? value) {
        value = default;
        if (string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        if (!_items.TryGetValue(key.Trim(), out var boxed) || boxed is null) {
            return false;
        }

        if (boxed is not TValue typed) {
            return false;
        }

        value = typed;
        return true;
    }
}

/// <summary>
/// Shared typed pipeline runner for tool execution.
/// </summary>
public static partial class ToolPipeline {
    /// <summary>
    /// Runs binder, middleware, and terminal execution for a tool call.
    /// </summary>
    /// <remarks>
    /// Execution order is binder -> reliability (optional) -> middleware chain (optional) -> terminal.
    /// When reliability is enabled, retries wrap the full middleware+terminal chain and re-run it per attempt.
    /// Caller-triggered cancellation is propagated and not retried.
    /// </remarks>
    public static Task<string> RunAsync<TRequest>(
        ToolDefinition definition,
        JsonObject? arguments,
        CancellationToken cancellationToken,
        Func<JsonObject?, ToolRequestBindingResult<TRequest>> binder,
        ToolPipelineNext<TRequest> terminal,
        ToolPipelineReliabilityOptions? reliability = null,
        IReadOnlyList<ToolPipelineMiddleware<TRequest>>? middleware = null)
        where TRequest : notnull {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(terminal);
        cancellationToken.ThrowIfCancellationRequested();

        var binding = binder(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return Task.FromResult(ToolResponse.Error(
                binding.ErrorCode,
                binding.Error,
                hints: binding.Hints,
                isTransient: binding.IsTransient));
        }

        var context = new ToolPipelineContext<TRequest>(definition, arguments, binding.Request);
        ToolPipelineNext<TRequest> chain = terminal;

        if (middleware is not null && middleware.Count > 0) {
            for (var i = middleware.Count - 1; i >= 0; i--) {
                var current = middleware[i] ?? throw new ArgumentNullException(nameof(middleware));
                var next = chain;
                chain = (ctx, token) => current(ctx, token, next);
            }
        }

        if (reliability is not null) {
            var reliabilityMiddleware = Reliability<TRequest>(reliability);
            var next = chain;
            chain = (ctx, token) => reliabilityMiddleware(ctx, token, next);
        }

        return chain(context, cancellationToken);
    }
}
