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
    private const string SignalFlowLabelAlternation = "Why it matters|Action|Next action|Fix action";
    private static readonly string[] SignalFlowLabels = ["Why it matters", "Action", "Next action", "Fix action"];
    private static readonly char[] DefinitionListRiskyInlineMarkers = ['*', '_', '`', '['];
    private const int MinDocxVisualMaxWidthPx = 320;
    private const int MaxDocxVisualMaxWidthPx = 2000;
    private const int DefaultDocxVisualMaxWidthPx = 760;
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    // Detect ordered-list starters so definition-list escaping can skip true list items.
    private static readonly Regex OrderedListLineRegex = new(
        @"^\s*\d+[.)]\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    // Repair malformed strong spans like ****359**** into valid markdown emphasis.
    private static readonly Regex OverwrappedStrongSpanRegex = new(
        @"(?<!\*)\*{4}(?<inner>[^*\r\n]+)\*{4}(?!\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    // Detect single wrapped signal-flow bullets where one outer strong span swallowed inner labels.
    private static readonly Regex WrappedSignalFlowLineRegex = new(
        @"^(?<prefix>\s*-\s+[^\r\n]*?)\*\*(?<inner>[^\r\n]*->\s*\*\*[^\r\n]*?)\*\*(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    // Normalize tight arrow-label joins like "->**Why it matters:**" to "-> **Why it matters:**".
    private static readonly Regex SignalFlowArrowLabelTightRegex = new(
        @"->\s*\*\*(?=(?:" + SignalFlowLabelAlternation + @"):)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    // Ensure a space after bold labels such as "**Action:**next".
    private static readonly Regex SignalFlowBoldLabelMissingSpaceRegex = new(
        @"(?<label>\*\*(?:" + SignalFlowLabelAlternation + @"):\*\*)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    // Ensure a space after plain labels such as "Action:next", excluding markdown emphasis starts.
    private static readonly Regex SignalFlowPlainLabelMissingSpaceRegex = new(
        @"(?<label>(?<![\p{L}\p{N}_])(?:" + SignalFlowLabelAlternation + @"):)(?=\S)(?!\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    // Fast gate for deciding whether signal-label spacing repairs are needed at all.
    private static readonly Regex TightSignalLabelRegex = new(
        @"(?:" + SignalFlowLabelAlternation + @"):\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);

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
        var wordSafeMarkdown = NeutralizeSingleLineDefinitionLists(transcriptMarkdown);
        // Runtime export materializes visual fences before invoking this writer.
        // This path intentionally handles markdown normalization and image allow-listing.
        var allowedImageDirectories = BuildAllowedImageDirectories(additionalAllowedImageDirectories);
        WriteDocxFromMarkdown(wordSafeMarkdown, outputPath, allowedImageDirectories, docxVisualMaxWidthPx);
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
            MaxImageWidthPercentOfContent = 100
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
        TrySetMarkdownOption(options, "FitImagesToPageContentWidth", true);
        TrySetMarkdownOption(options, "MaxImageWidthPixels", (double)normalizedWidth);
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

    private static void TrySetMarkdownOption(MarkdownToWordOptions options, string propertyName, object value) {
        var property = options.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite) {
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (targetType.IsInstanceOfType(value)) {
            property.SetValue(options, value);
            return;
        }

        try {
            var converted = Convert.ChangeType(value, targetType);
            property.SetValue(options, converted);
        } catch {
            // Option type mismatch should not block DOCX export.
        }
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
        if (string.IsNullOrEmpty(markdown)) {
            return string.Empty;
        }

        if (!RequiresTranscriptTypographyNormalization(markdown)) {
            return markdown;
        }

        var newline = DetectLineEnding(markdown);
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        bool insideFence = false;
        char fenceMarker = '\0';
        int fenceLength = 0;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();
            if (TryGetFenceToken(trimmed, out var marker, out var markerRunLength)) {
                if (!insideFence) {
                    insideFence = true;
                    fenceMarker = marker;
                    fenceLength = markerRunLength;
                    continue;
                }

                if (marker == fenceMarker &&
                    markerRunLength >= fenceLength &&
                    IsClosingFenceLine(trimmed, markerRunLength)) {
                    insideFence = false;
                    fenceMarker = '\0';
                    fenceLength = 0;
                    continue;
                }

                continue;
            }

            if (insideFence) {
                continue;
            }

            lines[i] = NormalizeTranscriptTypographyLine(line);
        }

        return string.Join(newline, lines);
    }

    private static bool RequiresTranscriptTypographyNormalization(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return false;
        }

        return markdown.Contains("****", StringComparison.Ordinal)
               || markdown.Contains("->**", StringComparison.Ordinal)
               || ContainsAnySignalFlowBoldLabel(markdown)
               || TightSignalLabelRegex.IsMatch(markdown);
    }

    private static string NormalizeTranscriptTypographyLine(string line) {
        if (string.IsNullOrEmpty(line)) {
            return string.Empty;
        }

        var value = OverwrappedStrongSpanRegex.Replace(line, static match => {
            var inner = match.Groups["inner"].Value.Trim();
            return inner.Length == 0 ? match.Value : "**" + inner + "**";
        });
        value = RepairWrappedSignalFlowLine(value);
        value = NormalizeSignalFlowLabelSpacing(value);
        return value;
    }

    private static string RepairWrappedSignalFlowLine(string line) {
        if (string.IsNullOrEmpty(line)) {
            return string.Empty;
        }

        return WrappedSignalFlowLineRegex.Replace(line, static match => {
            var inner = match.Groups["inner"].Value;
            var markerIndex = inner.IndexOf("-> **", StringComparison.Ordinal);
            if (markerIndex < 0) {
                markerIndex = inner.IndexOf("->**", StringComparison.Ordinal);
            }
            if (markerIndex <= 0) {
                return match.Value;
            }

            var headline = inner[..markerIndex].TrimEnd();
            if (headline.Length == 0) {
                return match.Value;
            }

            var flow = inner[markerIndex..].TrimStart();
            if (flow.StartsWith("->**", StringComparison.Ordinal)) {
                flow = "-> **" + flow[4..];
            }

            if (!flow.StartsWith("-> **", StringComparison.Ordinal)) {
                return match.Value;
            }

            return match.Groups["prefix"].Value + "**" + headline + "** " + flow + match.Groups["tail"].Value;
        });
    }

    private static string NormalizeSignalFlowLabelSpacing(string line) {
        if (string.IsNullOrEmpty(line)) {
            return string.Empty;
        }

        if (!ContainsAnySignalFlowLabelPrefix(line)) {
            return line;
        }

        var value = SignalFlowArrowLabelTightRegex.Replace(line, "-> **");
        value = SignalFlowBoldLabelMissingSpaceRegex.Replace(value, "${label} ");
        value = SignalFlowPlainLabelMissingSpaceRegex.Replace(value, "${label} ");
        return value;
    }

    private static bool ContainsAnySignalFlowBoldLabel(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        for (var i = 0; i < SignalFlowLabels.Length; i++) {
            var marker = "-> **" + SignalFlowLabels[i] + ":**";
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnySignalFlowLabelPrefix(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        for (var i = 0; i < SignalFlowLabels.Length; i++) {
            var marker = SignalFlowLabels[i] + ":";
            if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string NeutralizeSingleLineDefinitionLists(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return string.Empty;
        }

        var newline = DetectLineEnding(markdown);
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        bool insideFence = false;
        char fenceMarker = '\0';
        int fenceLength = 0;

        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();

            if (TryGetFenceToken(trimmed, out var marker, out var markerRunLength)) {
                if (!insideFence) {
                    insideFence = true;
                    fenceMarker = marker;
                    fenceLength = markerRunLength;
                    continue;
                }

                if (marker == fenceMarker &&
                    markerRunLength >= fenceLength &&
                    IsClosingFenceLine(trimmed, markerRunLength)) {
                    insideFence = false;
                    fenceMarker = '\0';
                    fenceLength = 0;
                    continue;
                }

                // Fence-like content inside an active fence should never be rewritten.
                if (insideFence) {
                    continue;
                }

                continue;
            }

            if (insideFence || !LooksLikeSingleLineDefinition(trimmed) || !RequiresDefinitionListNeutralization(trimmed)) {
                continue;
            }

            if (!TryGetDefinitionSeparatorIndex(line, out var separatorIndex)) {
                continue;
            }

            if (separatorIndex > 0 && line[separatorIndex - 1] == '\\') {
                continue;
            }

            lines[i] = line[..separatorIndex] + "&#58;" + line[(separatorIndex + 1)..];
        }

        return string.Join(newline, lines);
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

    private static bool RequiresDefinitionListNeutralization(string trimmedLine) {
        if (string.IsNullOrWhiteSpace(trimmedLine)) {
            return false;
        }

        return trimmedLine.IndexOfAny(DefinitionListRiskyInlineMarkers) >= 0;
    }

    private static bool TryGetDefinitionSeparatorIndex(string line, out int index) {
        index = -1;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var inlineFenceLength = 0;
        var i = 0;
        while (i < line.Length) {
            var ch = line[i];

            // Respect escaped punctuation and escaped backticks.
            if (ch == '\\' && i + 1 < line.Length) {
                i += 2;
                continue;
            }

            if (ch == '`') {
                var runLength = CountRunLength(line, i, '`');
                if (inlineFenceLength == 0) {
                    inlineFenceLength = runLength;
                    i += runLength;
                    continue;
                }

                if (runLength >= inlineFenceLength) {
                    inlineFenceLength = 0;
                    i += runLength;
                    continue;
                }
            }

            if (inlineFenceLength == 0 &&
                ch == ':' &&
                i > 0 &&
                i + 1 < line.Length &&
                char.IsWhiteSpace(line[i + 1])) {
                index = i;
                return true;
            }

            i++;
        }

        return false;
    }

    private static string DetectLineEnding(string text) {
        if (text.Contains("\r\n", StringComparison.Ordinal)) {
            return "\r\n";
        }

        if (text.Contains('\r')) {
            return "\r";
        }

        return "\n";
    }

    private static bool TryGetFenceToken(string trimmedLine, out char marker, out int runLength) {
        marker = '\0';
        runLength = 0;
        if (string.IsNullOrEmpty(trimmedLine)) {
            return false;
        }

        var first = trimmedLine[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var length = 0;
        while (length < trimmedLine.Length && trimmedLine[length] == first) {
            length++;
        }

        if (length < 3) {
            return false;
        }

        marker = first;
        runLength = length;
        return true;
    }

    private static bool IsClosingFenceLine(string trimmedLine, int fenceLength) {
        if (fenceLength < 3 || trimmedLine.Length < fenceLength) {
            return false;
        }

        for (int i = fenceLength; i < trimmedLine.Length; i++) {
            if (!char.IsWhiteSpace(trimmedLine[i])) {
                return false;
            }
        }

        return true;
    }

    private static int CountRunLength(string text, int startIndex, char marker) {
        var length = 0;
        while (startIndex + length < text.Length && text[startIndex + length] == marker) {
            length++;
        }

        return length;
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
