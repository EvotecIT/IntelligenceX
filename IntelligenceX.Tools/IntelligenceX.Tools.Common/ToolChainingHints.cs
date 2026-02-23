using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared contract helpers for model-driven continuation/chaining hints in tool responses.
/// </summary>
public static class ToolChainingHints {
    private static readonly IReadOnlyDictionary<string, string> EmptyStringMap =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Shared immutable empty string map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EmptyMap => EmptyStringMap;

    /// <summary>
    /// Creates a normalized continuation contract payload.
    /// </summary>
    public static ToolChainContractModel Create(
        IEnumerable<ToolNextActionModel>? nextActions = null,
        string? cursor = null,
        string? resumeToken = null,
        IReadOnlyDictionary<string, string>? handoff = null,
        double confidence = 0.5d,
        string? flowId = null,
        string? stepId = null,
        IReadOnlyDictionary<string, string>? checkpoint = null) {
        return new ToolChainContractModel {
            NextActions = NormalizeActionsForContract(nextActions),
            Cursor = NormalizeTokenForContract(cursor),
            ResumeToken = NormalizeTokenForContract(resumeToken),
            Handoff = NormalizeMapForContract(handoff),
            Confidence = NormalizeConfidenceForContract(confidence),
            FlowId = NormalizeTokenForContract(flowId),
            StepId = NormalizeTokenForContract(stepId),
            Checkpoint = NormalizeMapForContract(checkpoint)
        };
    }

    /// <summary>
    /// Creates a single next-action hint.
    /// </summary>
    public static ToolNextActionModel NextAction(
        string tool,
        string reason,
        IReadOnlyDictionary<string, string>? suggestedArguments = null,
        bool optional = true,
        bool? mutating = null,
        IReadOnlyDictionary<string, object?>? arguments = null) {
        if (string.IsNullOrWhiteSpace(tool)) {
            throw new ArgumentException("Tool name is required.", nameof(tool));
        }
        if (string.IsNullOrWhiteSpace(reason)) {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        return new ToolNextActionModel {
            Tool = tool.Trim(),
            Reason = reason.Trim(),
            SuggestedArguments = NormalizeMapForContract(suggestedArguments),
            Arguments = NormalizeObjectMapForContract(arguments),
            Optional = optional,
            Mutating = mutating
        };
    }

    /// <summary>
    /// Builds a simple dictionary payload from key/value entries.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Map(params (string Key, object? Value)[] entries) {
        if (entries is null || entries.Length == 0) {
            return EmptyStringMap;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++) {
            var key = entries[i].Key;
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            map[key.Trim()] = Convert.ToString(entries[i].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return map.Count == 0
            ? EmptyStringMap
            : new ReadOnlyDictionary<string, string>(map);
    }

    /// <summary>
    /// Builds a typed dictionary payload for structured next-action arguments.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> MapObject(params (string Key, object? Value)[] entries) {
        if (entries is null || entries.Length == 0) {
            return EmptyObjectMap;
        }

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++) {
            var key = entries[i].Key;
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            map[key.Trim()] = NormalizeArgumentValue(entries[i].Value);
        }

        return map.Count == 0
            ? EmptyObjectMap
            : new ReadOnlyDictionary<string, object?>(map);
    }

    /// <summary>
    /// Builds a compact opaque token from stable key/value parts.
    /// </summary>
    public static string BuildToken(string scope, params (string Key, string? Value)[] parts) {
        if (string.IsNullOrWhiteSpace(scope)) {
            return string.Empty;
        }

        var normalizedScope = scope.Trim();
        var normalizedParts = new List<string>();
        for (var i = 0; i < parts.Length; i++) {
            var key = parts[i].Key;
            var value = parts[i].Value;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            normalizedParts.Add(
                $"{Uri.EscapeDataString(key.Trim())}={Uri.EscapeDataString(value.Trim())}");
        }

        return normalizedParts.Count == 0
            ? normalizedScope
            : $"{normalizedScope}:{string.Join("|", normalizedParts)}";
    }

    private static IReadOnlyList<ToolNextActionModel> NormalizeActions(IEnumerable<ToolNextActionModel>? actions) {
        if (actions is null) {
            return Array.Empty<ToolNextActionModel>();
        }

        var normalized = new List<ToolNextActionModel>();
        foreach (var action in actions) {
            if (action is null || string.IsNullOrWhiteSpace(action.Tool) || string.IsNullOrWhiteSpace(action.Reason)) {
                continue;
            }

            normalized.Add(new ToolNextActionModel {
                Tool = action.Tool.Trim(),
                Reason = action.Reason.Trim(),
                SuggestedArguments = NormalizeMapForContract(action.SuggestedArguments),
                Arguments = NormalizeObjectMapForContract(action.Arguments),
                Optional = action.Optional,
                Mutating = NormalizeMutabilityHint(action.Mutating)
            });
        }

        var deduplicated = normalized
            .DistinctBy(static action => (action.Tool, action.Reason))
            .ToList();
        if (deduplicated.Count == 0) {
            return Array.Empty<ToolNextActionModel>();
        }

        return new ReadOnlyCollection<ToolNextActionModel>(deduplicated);
    }

    private static string NormalizeToken(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }

        return token.Trim();
    }

    private static IReadOnlyDictionary<string, string> NormalizeMap(IReadOnlyDictionary<string, string>? source) {
        if (source is null || source.Count == 0) {
            return EmptyStringMap;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in source) {
            if (!string.IsNullOrWhiteSpace(item.Key)) {
                map[item.Key.Trim()] = item.Value ?? string.Empty;
            }
        }

        return map.Count == 0
            ? EmptyStringMap
            : new ReadOnlyDictionary<string, string>(map);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyObjectMap =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    internal static IReadOnlyDictionary<string, object?>? NormalizeObjectMapForContract(IReadOnlyDictionary<string, object?>? source) {
        if (source is null || source.Count == 0) {
            return null;
        }

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in source) {
            if (!string.IsNullOrWhiteSpace(item.Key)) {
                map[item.Key.Trim()] = NormalizeArgumentValue(item.Value);
            }
        }

        return map.Count == 0
            ? null
            : new ReadOnlyDictionary<string, object?>(map);
    }

    internal static IReadOnlyList<ToolNextActionModel> NormalizeActionsForContract(IEnumerable<ToolNextActionModel>? actions) {
        return NormalizeActions(actions);
    }

    internal static string NormalizeTokenForContract(string? token) {
        return NormalizeToken(token);
    }

    internal static IReadOnlyDictionary<string, string> NormalizeMapForContract(IReadOnlyDictionary<string, string>? source) {
        return NormalizeMap(source);
    }

    internal static double NormalizeConfidenceForContract(double confidence) {
        return Math.Clamp(confidence, 0d, 1d);
    }

    internal static bool? NormalizeMutabilityHintForContract(bool? mutating) {
        return NormalizeMutabilityHint(mutating);
    }

    private static bool? NormalizeMutabilityHint(bool? mutating) {
        return mutating;
    }

    private static object? NormalizeArgumentValue(object? value) {
        if (value is null) {
            return null;
        }

        if (value is string
            || value is bool
            || value is byte
            || value is sbyte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal) {
            return value;
        }

        if (value is char ch) {
            return ch.ToString();
        }

        if (value is DateTime dt) {
            return dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset dto) {
            return dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is Enum enumValue) {
            return enumValue.ToString();
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyObjectMap) {
            var nested = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in readOnlyObjectMap) {
                if (!string.IsNullOrWhiteSpace(pair.Key)) {
                    nested[pair.Key.Trim()] = NormalizeArgumentValue(pair.Value);
                }
            }
            return nested;
        }

        if (value is IDictionary dictionary) {
            var nested = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry pair in dictionary) {
                var key = Convert.ToString(pair.Key, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(key)) {
                    nested[key.Trim()] = NormalizeArgumentValue(pair.Value);
                }
            }
            return nested;
        }

        if (value is IEnumerable enumerable) {
            var list = new List<object?>();
            foreach (var item in enumerable) {
                list.Add(NormalizeArgumentValue(item));
            }
            return list;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

/// <summary>
/// Canonical continuation/chaining contract emitted by chat-oriented tools.
/// </summary>
public sealed class ToolChainContractModel {
    private IReadOnlyList<ToolNextActionModel> _nextActions = Array.Empty<ToolNextActionModel>();
    private string _cursor = string.Empty;
    private string _resumeToken = string.Empty;
    private IReadOnlyDictionary<string, string> _handoff = ToolChainingHints.EmptyMap;
    private double _confidence = 0.5d;
    private string _flowId = string.Empty;
    private string _stepId = string.Empty;
    private IReadOnlyDictionary<string, string> _checkpoint = ToolChainingHints.EmptyMap;

    /// <summary>
    /// Optional follow-up actions for the model (advisory only).
    /// </summary>
    public IReadOnlyList<ToolNextActionModel> NextActions {
        get => _nextActions;
        init => _nextActions = ToolChainingHints.NormalizeActionsForContract(value);
    }

    /// <summary>
    /// Opaque cursor representing current continuation position/state.
    /// </summary>
    public string Cursor {
        get => _cursor;
        init => _cursor = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Opaque token that can be echoed by orchestrators to resume a flow.
    /// </summary>
    public string ResumeToken {
        get => _resumeToken;
        init => _resumeToken = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Structured handoff payload for cross-tool chaining.
    /// </summary>
    public IReadOnlyDictionary<string, string> Handoff {
        get => _handoff;
        init => _handoff = ToolChainingHints.NormalizeMapForContract(value);
    }

    /// <summary>
    /// Best-effort confidence score (0..1) for the emitted result context.
    /// </summary>
    public double Confidence {
        get => _confidence;
        init => _confidence = ToolChainingHints.NormalizeConfidenceForContract(value);
    }

    /// <summary>
    /// Stable flow identifier for multi-step orchestration across tool calls.
    /// </summary>
    public string FlowId {
        get => _flowId;
        init => _flowId = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Current flow step identifier (for example phase/page marker).
    /// </summary>
    public string StepId {
        get => _stepId;
        init => _stepId = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Structured checkpoint map for resumable long-flow progression.
    /// </summary>
    public IReadOnlyDictionary<string, string> Checkpoint {
        get => _checkpoint;
        init => _checkpoint = ToolChainingHints.NormalizeMapForContract(value);
    }
}

/// <summary>
/// Advisory next-action descriptor for agent orchestration.
/// </summary>
public sealed class ToolNextActionModel {
    private string _tool = string.Empty;
    private string _reason = string.Empty;
    private IReadOnlyDictionary<string, string> _suggestedArguments = ToolChainingHints.EmptyMap;
    private IReadOnlyDictionary<string, object?>? _arguments;
    private bool? _mutating;

    /// <summary>
    /// Suggested tool name.
    /// </summary>
    public string Tool {
        get => _tool;
        init => _tool = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Why this action is suggested.
    /// </summary>
    public string Reason {
        get => _reason;
        init => _reason = ToolChainingHints.NormalizeTokenForContract(value);
    }

    /// <summary>
    /// Suggested argument skeleton for follow-up.
    /// </summary>
    public IReadOnlyDictionary<string, string> SuggestedArguments {
        get => _suggestedArguments;
        init => _suggestedArguments = ToolChainingHints.NormalizeMapForContract(value);
    }

    /// <summary>
    /// Typed argument payload for follow-up execution (supports arrays/objects).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Arguments {
        get => _arguments;
        init => _arguments = ToolChainingHints.NormalizeObjectMapForContract(value);
    }

    /// <summary>
    /// Indicates this action is optional/advisory and should not block autonomous planning.
    /// </summary>
    public bool Optional { get; init; } = true;

    /// <summary>
    /// Optional mutability hint for host-side execution guards.
    /// true = mutating/write-capable, false = read-only, null = unspecified.
    /// </summary>
    public bool? Mutating {
        get => _mutating;
        init => _mutating = ToolChainingHints.NormalizeMutabilityHintForContract(value);
    }
}
