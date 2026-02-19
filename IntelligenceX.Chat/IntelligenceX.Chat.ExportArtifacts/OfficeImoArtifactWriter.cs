using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using OfficeIMO.Excel;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// OfficeIMO-backed document writers used by chat export flows.
/// </summary>
public static class OfficeImoArtifactWriter {
    private static readonly Regex OrderedListLineRegex = new(
        @"^\s*\d+[.)]\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Writes tabular rows to an Excel workbook using OfficeIMO.Excel.
    /// </summary>
    /// <param name="title">Sheet and table naming hint.</param>
    /// <param name="rows">Rectangular row matrix where row zero is treated as header.</param>
    /// <param name="outputPath">Destination .xlsx file path.</param>
    public static void WriteXlsx(string title, IReadOnlyList<string[]> rows, string outputPath) {
        using var document = ExcelDocument.Create(outputPath);
        var sheet = document.AddWorkSheet(SanitizeSheetName(title), SheetNameValidationMode.Sanitize);

        for (int r = 0; r < rows.Count; r++) {
            var values = rows[r];
            for (int c = 0; c < values.Length; c++) {
                sheet.CellValue(r + 1, c + 1, values[c] ?? string.Empty);
            }
        }

        if (rows.Count > 1 && rows[0].Length > 0) {
            var range = "A1:" + BuildSpreadsheetColumnName(rows[0].Length) + rows.Count;
            sheet.AddTable(range, hasHeader: true, name: SanitizeTableName(title), style: TableStyle.TableStyleMedium2);
        }

        sheet.AutoFitColumns();
        document.Save(openExcel: false);
    }

    /// <summary>
    /// Writes tabular rows to a Word document by converting a generated markdown table.
    /// </summary>
    /// <param name="title">Optional document heading.</param>
    /// <param name="rows">Rectangular row matrix where row zero is treated as header.</param>
    /// <param name="outputPath">Destination .docx file path.</param>
    public static void WriteDocxTable(string title, IReadOnlyList<string[]> rows, string outputPath) {
        var markdown = BuildMarkdownTable(title, rows);
        WriteDocxFromMarkdown(markdown, outputPath);
    }

    /// <summary>
    /// Writes transcript markdown to a Word document using OfficeIMO markdown-to-word conversion.
    /// </summary>
    /// <param name="title">Optional fallback heading when markdown has no heading.</param>
    /// <param name="markdown">Transcript markdown source.</param>
    /// <param name="outputPath">Destination .docx file path.</param>
    public static void WriteDocxTranscript(string title, string markdown, string outputPath) {
        var sourceMarkdown = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var transcriptMarkdown = BuildTranscriptMarkdown(title, sourceMarkdown);
        var wordSafeMarkdown = NeutralizeSingleLineDefinitionLists(transcriptMarkdown);
        WriteDocxFromMarkdown(wordSafeMarkdown, outputPath);
    }

    private static void WriteDocxFromMarkdown(string markdown, string outputPath) {
        var safeMarkdown = string.IsNullOrWhiteSpace(markdown) ? "# Transcript\n" : markdown;
        using var document = safeMarkdown.LoadFromMarkdown(new MarkdownToWordOptions { FontFamily = "Calibri" });
        document.Save(outputPath);
    }

    private static string BuildMarkdownTable(string title, IReadOnlyList<string[]> rows) {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) {
            builder.Append("# ").AppendLine(title.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("_Generated from Assistant Data View_");
        builder.AppendLine();

        AppendMarkdownTableRow(builder, rows[0]);
        builder.Append('|');
        for (int i = 0; i < rows[0].Length; i++) {
            builder.Append(" --- |");
        }
        builder.AppendLine();

        for (int r = 1; r < rows.Count; r++) {
            AppendMarkdownTableRow(builder, rows[r]);
        }

        return builder.ToString();
    }

    private static string BuildTranscriptMarkdown(string title, string markdown) {
        if (markdown.Length == 0) {
            return string.IsNullOrWhiteSpace(title) ? "# Transcript\n" : "# " + title.Trim() + "\n";
        }

        var trimmed = markdown.TrimStart();
        if (trimmed.StartsWith("#", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(title)) {
            return markdown;
        }

        return "# " + title.Trim() + "\n\n" + markdown;
    }

    private static string NeutralizeSingleLineDefinitionLists(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return string.Empty;
        }

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        bool insideFence = false;

        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) {
                insideFence = !insideFence;
                continue;
            }

            if (insideFence || !LooksLikeSingleLineDefinition(trimmed)) {
                continue;
            }

            if (!TryGetDefinitionSeparatorIndex(line, out var separatorIndex)) {
                continue;
            }

            if (separatorIndex > 0 && line[separatorIndex - 1] == '\\') {
                continue;
            }

            lines[i] = line.Insert(separatorIndex, "\\");
        }

        return string.Join("\n", lines);
    }

    private static bool LooksLikeSingleLineDefinition(string trimmedLine) {
        if (string.IsNullOrWhiteSpace(trimmedLine)) {
            return false;
        }

        if (trimmedLine.StartsWith("#", StringComparison.Ordinal) ||
            trimmedLine.StartsWith(">", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("- ", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("* ", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("+ ", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("|", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("```", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("~~~", StringComparison.Ordinal) ||
            OrderedListLineRegex.IsMatch(trimmedLine)) {
            return false;
        }

        return TryGetDefinitionSeparatorIndex(trimmedLine, out _);
    }

    private static bool TryGetDefinitionSeparatorIndex(string line, out int index) {
        index = -1;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var scanFrom = 0;
        while (scanFrom < line.Length) {
            var separator = line.IndexOf(':', scanFrom);
            if (separator < 1) {
                return false;
            }

            if (separator + 1 < line.Length && line[separator + 1] == ' ') {
                index = separator;
                return true;
            }

            scanFrom = separator + 1;
        }

        return false;
    }

    private static void AppendMarkdownTableRow(StringBuilder builder, IReadOnlyList<string> cells) {
        builder.Append('|');
        for (int i = 0; i < cells.Count; i++) {
            builder.Append(' ').Append(EscapeMarkdownTableCell(cells[i])).Append(" |");
        }
        builder.AppendLine();
    }

    private static string EscapeMarkdownTableCell(string? value) {
        var safe = value ?? string.Empty;
        return safe
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\r", "<br>", StringComparison.Ordinal);
    }

    private static string SanitizeSheetName(string title) {
        var raw = (title ?? string.Empty).Trim();
        if (raw.Length == 0) {
            raw = "Data";
        }

        raw = raw
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        if (raw.Length > 31) {
            raw = raw[..31];
        }

        return raw.Length == 0 ? "Data" : raw;
    }

    private static string SanitizeTableName(string title) {
        var raw = (title ?? string.Empty).Trim();
        if (raw.Length == 0) {
            raw = "Data";
        }

        var builder = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++) {
            var ch = raw[i];
            if (char.IsLetterOrDigit(ch) || ch == '_') {
                builder.Append(ch);
            } else if (!char.IsWhiteSpace(ch)) {
                builder.Append('_');
            } else {
                builder.Append('_');
            }
        }

        var value = builder.ToString().Trim('_');
        if (value.Length == 0) {
            value = "Data";
        }

        if (char.IsDigit(value[0])) {
            value = "T_" + value;
        }

        if (value.Length > 64) {
            value = value[..64];
        }

        return value;
    }

    private static string BuildSpreadsheetColumnName(int columnIndexOneBased) {
        if (columnIndexOneBased < 1) {
            throw new ArgumentOutOfRangeException(nameof(columnIndexOneBased));
        }

        var columnName = string.Empty;
        var column = columnIndexOneBased;
        while (column > 0) {
            var remainder = (column - 1) % 26;
            columnName = (char)('A' + remainder) + columnName;
            column = (column - 1) / 26;
        }

        return columnName;
    }
}
