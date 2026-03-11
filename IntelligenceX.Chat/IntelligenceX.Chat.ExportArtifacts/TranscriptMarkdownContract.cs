using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Shared transcript markdown preparation contract used by render, export, and DOCX flows.
/// </summary>
public static class TranscriptMarkdownContract {
    /// <summary>
    /// Applies the shared transcript markdown normalization expected by render and export flows.
    /// </summary>
    public static string PrepareMessageBody(string? markdown) {
        var normalized = TranscriptTypographyNormalizer.NormalizeMarkdownOutsideFencedCodeBlocks(markdown ?? string.Empty);
        return ExpandAdjacentOrderedListItems(normalized);
    }

    /// <summary>
    /// Prepares transcript markdown for markdown export by applying shared normalization, removing transport markers,
    /// and collapsing duplicate blank lines.
    /// </summary>
    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        var prepared = PrepareMessageBody(markdown);
        if (string.IsNullOrEmpty(prepared)) {
            return string.Empty;
        }

        var original = markdown ?? string.Empty;
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = prepared.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        var previousWasBlank = false;

        foreach (var rawLine in lines) {
            var line = rawLine ?? string.Empty;
            if (line.Trim().Equals("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousWasBlank) {
                continue;
            }

            output.Add(line);
            previousWasBlank = isBlank;
        }

        return string.Join(newline, output);
    }

    /// <summary>
    /// Applies the DOCX-specific normalization contract after the transcript markdown has already been prepared for export.
    /// </summary>
    public static string PrepareTranscriptMarkdownForDocx(string markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        var normalized = TranscriptTypographyNormalizer.NormalizeMarkdownOutsideFencedCodeBlocks(markdown ?? string.Empty);
        if (!preservesGroupedDefinitionLikeParagraphs) {
            normalized = NormalizeLegacyGroupedDefinitionLikeParagraphsForDocx(normalized);
        }

        return normalized;
    }

    /// <summary>
    /// Inserts paragraph boundaries between adjacent definition-like lines for legacy DOCX compatibility.
    /// </summary>
    public static string NormalizeLegacyGroupedDefinitionLikeParagraphsForDocx(string markdown) {
        return TranscriptTypographyNormalizer.SeparateAdjacentDefinitionLikeLinesOutsideFencedCodeBlocks(markdown ?? string.Empty);
    }

    private static string ExpandAdjacentOrderedListItems(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0) {
            return text;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length < 2) {
            return normalized;
        }

        var sb = new StringBuilder(normalized.Length + 32);
        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            sb.Append(current);
            if (i >= lines.Length - 1) {
                continue;
            }

            sb.Append('\n');
            var next = lines[i + 1] ?? string.Empty;
            if (IsOrderedListLine(current) && IsOrderedListLine(next)) {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static bool IsOrderedListLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i])) {
            i++;
        }

        var numberStart = i;
        while (i < line.Length && char.IsDigit(line[i])) {
            i++;
        }

        if (i == numberStart || i >= line.Length || line[i] != '.') {
            return false;
        }

        i++;
        return i < line.Length && char.IsWhiteSpace(line[i]);
    }
}
