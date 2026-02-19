using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Canonical response factory for tool output envelopes (v2).
/// </summary>
public static class ToolResultV2 {
    /// <summary>
    /// Creates an error envelope.
    /// </summary>
    public static string Error(string errorCode, string error, IReadOnlyList<string>? hints = null, bool isTransient = false) {
        return ToolResponse.Error(errorCode, error, hints, isTransient);
    }

    /// <summary>
    /// Creates a success envelope from a typed model.
    /// </summary>
    public static string OkModel<T>(T model, JsonObject? meta = null, string? summaryMarkdown = null, JsonObject? render = null) {
        return ToolResponse.OkModel(
            model: model,
            meta: CloneObject(meta),
            summaryMarkdown: summaryMarkdown,
            render: CloneObject(render));
    }

    /// <summary>
    /// Creates a facts-table success envelope from a typed model.
    /// </summary>
    public static string OkFactsModel<T>(
        T model,
        string title,
        IReadOnlyList<(string Key, string Value)> facts,
        JsonObject? meta = null,
        string keyHeader = "Field",
        string valueHeader = "Value",
        bool truncated = false,
        JsonObject? render = null) {
        return ToolResponse.OkFactsModel(
            model: model,
            title: title,
            facts: facts,
            meta: CloneObject(meta),
            keyHeader: keyHeader,
            valueHeader: valueHeader,
            truncated: truncated,
            render: CloneObject(render));
    }

    /// <summary>
    /// Creates a standardized mutating-tool success envelope.
    /// </summary>
    public static string OkWriteActionModel<T>(
        T model,
        string action,
        bool writeApplied,
        IReadOnlyList<(string Key, string Value)>? facts = null,
        JsonObject? meta = null,
        JsonObject? render = null,
        string? summaryTitle = null) {
        return ToolResponse.OkWriteActionModel(
            model: model,
            action: action,
            writeApplied: writeApplied,
            facts: facts,
            meta: CloneObject(meta),
            render: CloneObject(render),
            summaryTitle: summaryTitle);
    }

    private static JsonObject? CloneObject(JsonObject? source) {
        if (source is null) {
            return null;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        foreach (var item in source) {
            clone.Add(item.Key, CloneValue(item.Value));
        }

        return clone;
    }

    private static JsonArray? CloneArray(JsonArray? source) {
        if (source is null) {
            return null;
        }

        var clone = new JsonArray();
        foreach (var item in source) {
            clone.Add(CloneValue(item));
        }

        return clone;
    }

    private static JsonValue CloneValue(JsonValue? source) {
        if (source is null) {
            return JsonValue.Null;
        }

        return source.Kind switch {
            JsonValueKind.Null => JsonValue.Null,
            JsonValueKind.Boolean => JsonValue.From(source.AsBoolean()),
            JsonValueKind.String => JsonValue.From(source.AsString()),
            JsonValueKind.Number => CloneNumber(source),
            JsonValueKind.Object => JsonValue.From(CloneObject(source.AsObject()) ?? new JsonObject(StringComparer.Ordinal)),
            JsonValueKind.Array => JsonValue.From(CloneArray(source.AsArray()) ?? new JsonArray()),
            _ => JsonValue.Null
        };
    }

    private static JsonValue CloneNumber(JsonValue source) {
        return source.Value switch {
            long l => JsonValue.From(l),
            int i => JsonValue.From((long)i),
            double d => JsonValue.From(d),
            _ => JsonValue.From(source.AsDouble() ?? 0d)
        };
    }
}
