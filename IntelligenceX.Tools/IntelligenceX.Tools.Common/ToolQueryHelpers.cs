using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared row/query helper primitives used by multiple tool packs.
/// </summary>
public static class ToolQueryHelpers {
    /// <summary>
    /// Caps a row collection by max-results and returns standard scanned/truncated counters.
    /// </summary>
    public static IReadOnlyList<TRow> CapRows<TRow>(
        IReadOnlyList<TRow> allRows,
        int maxResults,
        out int scanned,
        out bool truncated) {
        scanned = allRows.Count;
        if (scanned <= maxResults) {
            truncated = false;
            return allRows;
        }

        truncated = true;
        return allRows.Take(maxResults).ToArray();
    }

    /// <summary>
    /// Builds the standard auto-column table envelope.
    /// </summary>
    public static string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int scanned,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                metaMutate?.Invoke(meta);
            });
        return response;
    }

    /// <summary>
    /// Builds a projected key set from row values using case-insensitive matching.
    /// </summary>
    public static HashSet<string> BuildProjectedSet<TRow>(
        IReadOnlyList<TRow> rows,
        Func<TRow, string?> keySelector) {
        if (rows is null) {
            throw new ArgumentNullException(nameof(rows));
        }
        if (keySelector is null) {
            throw new ArgumentNullException(nameof(keySelector));
        }

        var projected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) {
            var key = keySelector(row);
            if (!string.IsNullOrWhiteSpace(key)) {
                projected.Add(key);
            }
        }

        return projected;
    }

    /// <summary>
    /// Filters details by a projected key set.
    /// </summary>
    public static IReadOnlyList<TDetail> FilterByProjectedSet<TDetail>(
        IReadOnlyList<TDetail> details,
        IReadOnlySet<string> projectedKeys,
        Func<TDetail, string?> keySelector) {
        if (details is null) {
            throw new ArgumentNullException(nameof(details));
        }
        if (projectedKeys is null) {
            throw new ArgumentNullException(nameof(projectedKeys));
        }
        if (keySelector is null) {
            throw new ArgumentNullException(nameof(keySelector));
        }
        if (details.Count == 0 || projectedKeys.Count == 0) {
            return Array.Empty<TDetail>();
        }

        return details
            .Where(detail => {
                var key = keySelector(detail);
                return !string.IsNullOrWhiteSpace(key) && projectedKeys.Contains(key);
            })
            .ToArray();
    }
}
