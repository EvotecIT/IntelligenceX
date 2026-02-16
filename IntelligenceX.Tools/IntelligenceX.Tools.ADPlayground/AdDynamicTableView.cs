using System;
using System.Collections.Generic;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Thin AD adapter for dynamic LDAP attribute-bag projection.
/// </summary>
internal static class AdDynamicTableView {
    private const int DefaultMaxViewTop = 5000;

    internal static bool TryBuildResponseFromOutputRows<TModel>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<LdapToolOutputRow> rows,
        string title,
        string rowsPath,
        bool baseTruncated,
        out string response,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null,
        int maxTop = DefaultMaxViewTop) {
        var bags = ToBags(rows);
        return ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(
            arguments: arguments,
            model: model,
            rows: bags,
            title: title,
            rowsPath: rowsPath,
            baseTruncated: baseTruncated,
            response: out response,
            scanned: scanned,
            metaMutate: metaMutate,
            maxTop: maxTop);
    }

    internal static bool TryBuildResponseFromQueryRows<TModel>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<LdapToolQueryRow> rows,
        string title,
        string rowsPath,
        bool baseTruncated,
        out string response,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null,
        int maxTop = DefaultMaxViewTop) {
        var bags = ToBags(rows);
        return ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(
            arguments: arguments,
            model: model,
            rows: bags,
            title: title,
            rowsPath: rowsPath,
            baseTruncated: baseTruncated,
            response: out response,
            scanned: scanned,
            metaMutate: metaMutate,
            maxTop: maxTop);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToBags(IReadOnlyList<LdapToolOutputRow> rows) {
        var bags = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            bags.Add(ToDictionary(rows[i]));
        }

        return bags;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToBags(IReadOnlyList<LdapToolQueryRow> rows) {
        var bags = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            bags.Add(ToDictionary(rows[i]));
        }

        return bags;
    }

    private static Dictionary<string, object?> ToDictionary(LdapToolOutputRow row) {
        var bag = row?.Attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(row.Attributes, StringComparer.Ordinal);

        if (row?.TruncatedAttributes is { Count: > 0 }) {
            bag["_truncated_attributes"] = row.TruncatedAttributes;
        }

        return bag;
    }

    private static Dictionary<string, object?> ToDictionary(LdapToolQueryRow row) {
        var bag = row?.Attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(row.Attributes, StringComparer.Ordinal);

        if (row?.TruncatedAttributes is { Count: > 0 }) {
            bag["_truncated_attributes"] = row.TruncatedAttributes;
        }

        return bag;
    }
}
