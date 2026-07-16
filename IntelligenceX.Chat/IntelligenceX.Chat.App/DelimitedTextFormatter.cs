using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Owns CSV and TSV cell escaping for desktop export surfaces.
/// </summary>
internal static class DelimitedTextFormatter {
    public static string FormatCsv(IEnumerable<IReadOnlyList<string>> rows) =>
        Format(rows, ',', EscapeCsvCell);

    public static string FormatTsv(IEnumerable<IReadOnlyList<string>> rows) =>
        Format(rows, '\t', SanitizeTsvCell);

    public static void WriteCsv(
        TextWriter writer,
        IEnumerable<IReadOnlyList<string>> rows,
        bool terminateLastRow = false) =>
        Write(writer, rows, ',', EscapeCsvCell, terminateLastRow);

    private static string Format(
        IEnumerable<IReadOnlyList<string>> rows,
        char delimiter,
        Func<string, string> formatCell) {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Write(writer, rows, delimiter, formatCell, terminateLastRow: false);
        return writer.ToString();
    }

    private static void Write(
        TextWriter writer,
        IEnumerable<IReadOnlyList<string>> rows,
        char delimiter,
        Func<string, string> formatCell,
        bool terminateLastRow) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(formatCell);

        var wroteRow = false;
        foreach (var row in rows) {
            if (wroteRow) {
                writer.WriteLine();
            }

            var cells = row ?? Array.Empty<string>();
            for (var index = 0; index < cells.Count; index++) {
                if (index > 0) {
                    writer.Write(delimiter);
                }

                writer.Write(formatCell(cells[index] ?? string.Empty));
            }

            wroteRow = true;
        }

        if (wroteRow && terminateLastRow) {
            writer.WriteLine();
        }
    }

    private static string SanitizeTsvCell(string value) =>
        (value ?? string.Empty)
            .Replace('\t', ' ')
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string EscapeCsvCell(string value) {
        var text = value ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? text
            : "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
