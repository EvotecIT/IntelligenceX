using System;
using System.Collections.Generic;
using System.Text;
using OfficeIMO.Excel;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// OfficeIMO-backed document writers used by chat export flows.
/// </summary>
public static partial class OfficeImoArtifactWriter {
    private const int MinDocxVisualMaxWidthPx = 320;
    private const int MaxDocxVisualMaxWidthPx = 2000;
    private const int DefaultDocxVisualMaxWidthPx = 760;

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
        WriteDocxTranscript(title, markdown, outputPath, additionalAllowedImageDirectories: null, docxVisualMaxWidthPx: null);
    }

    /// <summary>
    /// Writes transcript markdown to a Word document using OfficeIMO markdown-to-word conversion.
    /// </summary>
    /// <param name="title">Optional fallback heading when markdown has no heading.</param>
    /// <param name="markdown">Transcript markdown source.</param>
    /// <param name="outputPath">Destination .docx file path.</param>
    /// <param name="additionalAllowedImageDirectories">Additional local image directories to allow during markdown conversion.</param>
    /// <param name="docxVisualMaxWidthPx">Optional max-width hint in pixels for materialized visual images.</param>
    public static void WriteDocxTranscript(
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories,
        int? docxVisualMaxWidthPx = null) {
        var sourceMarkdown = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedMarkdown = NormalizeTranscriptMarkdownForDocx(sourceMarkdown);
        var transcriptMarkdown = BuildTranscriptMarkdown(title, normalizedMarkdown);
        // Runtime export materializes visual fences before invoking this writer.
        // This path intentionally handles markdown normalization and image allow-listing.
        var allowedImageDirectories = BuildAllowedImageDirectories(additionalAllowedImageDirectories);
        WriteDocxFromMarkdown(transcriptMarkdown, outputPath, allowedImageDirectories, docxVisualMaxWidthPx);
    }

    private static void WriteDocxFromMarkdown(
        string markdown,
        string outputPath,
        IReadOnlyList<string>? allowedImageDirectories = null,
        int? docxVisualMaxWidthPx = null) {
        var safeMarkdown = string.IsNullOrWhiteSpace(markdown) ? "# Transcript\n" : markdown;
        var options = new MarkdownToWordOptions {
            FontFamily = "Calibri",
            AllowLocalImages = allowedImageDirectories is { Count: > 0 },
            PreferNarrativeSingleLineDefinitions = true,
            FitImagesToContextWidth = true,
            MaxImageWidthPercentOfContent = 100d
        };
        ApplyMarkdownImageSizingOptions(options, docxVisualMaxWidthPx);
        if (allowedImageDirectories is { Count: > 0 }) {
            for (var i = 0; i < allowedImageDirectories.Count; i++) {
                var directory = allowedImageDirectories[i];
                if (string.IsNullOrWhiteSpace(directory)) {
                    continue;
                }

                if (!options.AllowedImageDirectories.Contains(directory)) {
                    options.AllowedImageDirectories.Add(directory);
                }
            }
        }

        using var document = safeMarkdown.LoadFromMarkdown(options);
        document.Save(outputPath);
    }

    private static void ApplyMarkdownImageSizingOptions(MarkdownToWordOptions options, int? docxVisualMaxWidthPx) {
        var normalizedWidth = NormalizeDocxVisualMaxWidthPx(docxVisualMaxWidthPx);
        options.FitImagesToPageContentWidth = true;
        options.MaxImageWidthPixels = normalizedWidth;
    }

    private static int NormalizeDocxVisualMaxWidthPx(int? value) {
        if (!value.HasValue) {
            return DefaultDocxVisualMaxWidthPx;
        }

        var normalized = value.Value;
        if (normalized < MinDocxVisualMaxWidthPx) {
            return MinDocxVisualMaxWidthPx;
        }

        if (normalized > MaxDocxVisualMaxWidthPx) {
            return MaxDocxVisualMaxWidthPx;
        }

        return normalized;
    }

    private static IReadOnlyList<string> BuildAllowedImageDirectories(IReadOnlyList<string>? additionalDirectories) {
        var list = new List<string>();

        if (additionalDirectories is { Count: > 0 }) {
            for (var i = 0; i < additionalDirectories.Count; i++) {
                var directory = (additionalDirectories[i] ?? string.Empty).Trim();
                if (directory.Length == 0 || ContainsDirectory(list, directory)) {
                    continue;
                }

                list.Add(directory);
            }
        }

        return list;
    }

    private static bool ContainsDirectory(List<string> directories, string candidate) {
        for (var i = 0; i < directories.Count; i++) {
            if (string.Equals(directories[i], candidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
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

    internal static string NormalizeTranscriptMarkdownForDocx(string markdown) {
        return TranscriptTypographyNormalizer.NormalizeMarkdownOutsideFencedCodeBlocks(markdown);
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

}
