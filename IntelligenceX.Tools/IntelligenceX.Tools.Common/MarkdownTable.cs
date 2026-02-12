using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Small markdown helpers intended for UI tool traces (headings, tables).
/// </summary>
/// <remarks>
/// This does not attempt to be a full markdown library. It only produces stable CommonMark-friendly
/// fragments that render well in typical markdown renderers.
/// </remarks>
public static class MarkdownTable {
    /// <summary>
    /// Builds a markdown table with a <c>###</c> title and a trailing count line.
    /// </summary>
    /// <param name="title">Optional title (rendered as <c>### {title}</c>).</param>
    /// <param name="headers">Table headers (one per column).</param>
    /// <param name="rows">Table rows (each row is a list of cell strings).</param>
    /// <param name="totalCount">Total number of items represented.</param>
    /// <param name="truncated">Whether the result set was truncated by tool limits.</param>
    /// <returns>Markdown table string.</returns>
    public static string Table(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, int totalCount, bool truncated) {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title)) {
            sb.Append("### ").AppendLine(title.Trim());
        }

        sb.AppendLine();

        sb.Append('|');
        for (var i = 0; i < headers.Count; i++) {
            sb.Append(' ').Append(EscapeCell(headers[i])).Append(" |");
        }
        sb.AppendLine();

        sb.Append('|');
        for (var i = 0; i < headers.Count; i++) {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        for (var r = 0; r < rows.Count; r++) {
            var row = rows[r];
            sb.Append('|');
            for (var c = 0; c < headers.Count; c++) {
                var value = c < row.Count ? row[c] : string.Empty;
                sb.Append(' ').Append(EscapeCell(value)).Append(" |");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.Append("Count: ").Append(totalCount);
        if (truncated) {
            sb.Append(" (truncated)");
        }

        return sb.ToString().TrimEnd();
    }

    private static string EscapeCell(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        // Minimal escaping to keep tables stable.
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}

