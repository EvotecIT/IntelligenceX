using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Result returned by typed tool-request binders.
/// </summary>
/// <typeparam name="TRequest">Typed request model.</typeparam>
public sealed class ToolRequestBindingResult<TRequest> where TRequest : notnull {
    private IReadOnlyList<string> _hints = Array.Empty<string>();

    private ToolRequestBindingResult() { }

    /// <summary>
    /// True when binding succeeded.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Bound request model when <see cref="IsValid"/> is true.
    /// </summary>
    public TRequest? Request { get; init; }

    /// <summary>
    /// Error code when binding fails.
    /// </summary>
    public string ErrorCode { get; init; } = "invalid_argument";

    /// <summary>
    /// Error message when binding fails.
    /// </summary>
    public string Error { get; init; } = "Invalid arguments.";

    /// <summary>
    /// Optional guidance for callers.
    /// </summary>
    public IReadOnlyList<string> Hints {
        get => _hints;
        init => _hints = NormalizeHints(value);
    }

    /// <summary>
    /// Optional transient marker for bind-time failures.
    /// </summary>
    public bool IsTransient { get; init; }

    /// <summary>
    /// Creates a successful binding result.
    /// </summary>
    public static ToolRequestBindingResult<TRequest> Success(TRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return new ToolRequestBindingResult<TRequest> {
            IsValid = true,
            Request = request
        };
    }

    /// <summary>
    /// Creates a failed binding result.
    /// </summary>
    public static ToolRequestBindingResult<TRequest> Failure(
        string error,
        string errorCode = "invalid_argument",
        IReadOnlyList<string>? hints = null,
        bool isTransient = false) {
        return new ToolRequestBindingResult<TRequest> {
            IsValid = false,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "invalid_argument" : errorCode.Trim(),
            Error = string.IsNullOrWhiteSpace(error) ? "Invalid arguments." : error.Trim(),
            Hints = hints ?? Array.Empty<string>(),
            IsTransient = isTransient
        };
    }

    private static IReadOnlyList<string> NormalizeHints(IReadOnlyList<string>? hints) {
        if (hints is null || hints.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(hints.Count);
        for (var i = 0; i < hints.Count; i++) {
            var hint = hints[i];
            if (!string.IsNullOrWhiteSpace(hint)) {
                normalized.Add(hint.Trim());
            }
        }

        if (normalized.Count == 0) {
            return Array.Empty<string>();
        }

        return new ReadOnlyCollection<string>(normalized);
    }
}

/// <summary>
/// Typed argument reader used by request binders.
/// </summary>
public sealed class ToolArgumentReader {
    private readonly JsonObject? _arguments;

    /// <summary>
    /// Initializes a new argument reader.
    /// </summary>
    public ToolArgumentReader(JsonObject? arguments) {
        _arguments = arguments;
    }

    /// <summary>
    /// Reads an optional trimmed string value.
    /// </summary>
    public string? OptionalString(string key) {
        return ToolArgs.GetOptionalTrimmed(_arguments, key);
    }

    /// <summary>
    /// Reads a required trimmed string value.
    /// </summary>
    public bool TryReadRequiredString(string key, out string value, out string error) {
        value = OptionalString(key) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value)) {
            error = string.Empty;
            return true;
        }

        var normalizedKey = string.IsNullOrWhiteSpace(key) ? "value" : key.Trim();
        error = $"{normalizedKey} is required.";
        return false;
    }

    /// <summary>
    /// Reads a boolean value with default.
    /// </summary>
    public bool Boolean(string key, bool defaultValue = false) {
        return ToolArgs.GetBoolean(_arguments, key, defaultValue);
    }

    /// <summary>
    /// Reads an optional boolean value only when the source JSON token is a boolean.
    /// </summary>
    public bool? OptionalBoolean(string key) {
        return _arguments?.TryGetValue(key, out var value) == true && value is not null && value.Kind == JsonValueKind.Boolean
            ? value.AsBoolean()
            : null;
    }

    /// <summary>
    /// Reads an integer value with bounds.
    /// </summary>
    public int CappedInt32(string key, int defaultValue, int minInclusive, int maxInclusive) {
        return ToolArgs.GetCappedInt32(_arguments, key, defaultValue, minInclusive, maxInclusive);
    }

    /// <summary>
    /// Reads a 64-bit integer value with bounds.
    /// </summary>
    public long CappedInt64(string key, long defaultValue, long minInclusive, long maxInclusive) {
        return ToolArgs.GetCappedInt64(_arguments, key, defaultValue, minInclusive, maxInclusive);
    }

    /// <summary>
    /// Reads an optional 64-bit integer value.
    /// </summary>
    public long? OptionalInt64(string key) {
        return _arguments?.GetInt64(key);
    }

    /// <summary>
    /// Reads a trimmed string array.
    /// </summary>
    public IReadOnlyList<string> StringArray(string key) {
        return ToolArgs.ReadStringArray(_arguments?.GetArray(key));
    }

    /// <summary>
    /// Reads a distinct trimmed string array.
    /// </summary>
    public IReadOnlyList<string> DistinctStringArray(string key) {
        return ToolArgs.ReadDistinctStringArray(_arguments?.GetArray(key));
    }

    /// <summary>
    /// Reads a positive integer array and caps values by an inclusive upper bound.
    /// </summary>
    public IReadOnlyList<int> PositiveInt32ArrayCapped(string key, int maxInclusive) {
        return ToolArgs.ReadPositiveInt32ArrayCapped(_arguments?.GetArray(key), maxInclusive);
    }

    /// <summary>
    /// Reads an optional JSON array value.
    /// </summary>
    public JsonArray? Array(string key) {
        return _arguments?.GetArray(key);
    }
}

/// <summary>
/// Convenience entry point for typed tool-request binders.
/// </summary>
public static class ToolRequestBinder {
    /// <summary>
    /// Executes a binder callback with a typed argument reader.
    /// </summary>
    public static ToolRequestBindingResult<TRequest> Bind<TRequest>(
        JsonObject? arguments,
        Func<ToolArgumentReader, ToolRequestBindingResult<TRequest>> binder)
        where TRequest : notnull {
        ArgumentNullException.ThrowIfNull(binder);
        return binder(new ToolArgumentReader(arguments));
    }
}
