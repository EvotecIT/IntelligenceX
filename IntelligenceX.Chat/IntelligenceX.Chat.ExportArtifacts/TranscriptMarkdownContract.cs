using System;
using System.Collections.Generic;
using System.IO;
using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Shared transcript markdown preparation contract used by export and DOCX flows
/// against the published OfficeIMO markdown transcript-preparation APIs.
/// </summary>
public static class TranscriptMarkdownContract {
    /// <summary>
    /// Applies the shared transcript markdown body preparation contract.
    /// </summary>
    public static string PrepareMessageBody(string? markdown) {
        var value = markdown ?? string.Empty;
        if (value.Length == 0) {
            return string.Empty;
        }

        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptBody(value);
    }

    /// <summary>
    /// Prepares transcript markdown for markdown export by applying shared normalization, removing transport markers,
    /// and collapsing duplicate blank lines.
    /// </summary>
    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        var withoutMarkers = StripCachedEvidenceTransportMarkers(markdown);
        if (string.IsNullOrEmpty(withoutMarkers)) {
            return string.Empty;
        }

        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(withoutMarkers);
    }

    /// <summary>
    /// Prepares transcript markdown for portable markdown export by applying shared normalization,
    /// removing transport markers, and preferring generic semantic visual fence languages.
    /// </summary>
    public static string PrepareTranscriptMarkdownForPortableExport(string? markdown) {
        var withoutMarkers = StripCachedEvidenceTransportMarkers(markdown);
        if (string.IsNullOrEmpty(withoutMarkers)) {
            return string.Empty;
        }

        var prepared = MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(
            withoutMarkers,
            MarkdownVisualFenceLanguageMode.GenericSemanticFence);

        return NormalizeAliasVisualFenceOpenersToGeneric(prepared);
    }

    /// <summary>
    /// Applies the DOCX-specific normalization contract after the transcript markdown has already been prepared for export.
    /// </summary>
    public static string PrepareTranscriptMarkdownForDocx(string markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForDocx(markdown, preservesGroupedDefinitionLikeParagraphs);
    }

    private static string StripCachedEvidenceTransportMarkers(string? markdown) {
        var original = markdown ?? string.Empty;
        if (original.Length == 0) {
            return string.Empty;
        }

        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = original.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            if (line.Trim().Equals("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            output.Add(line);
        }

        return string.Join(newline, output);
    }

    private static string NormalizeAliasVisualFenceOpenersToGeneric(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return string.Empty;
        }

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');

        for (var i = 0; i < lines.Length; i++) {
            lines[i] = RewriteVisualFenceLanguage(lines[i] ?? string.Empty);
        }

        return string.Join(newline, lines);
    }

    private static string RewriteVisualFenceLanguage(string line) {
        if (string.IsNullOrEmpty(line)) {
            return line;
        }

        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        var fenceStart = index;
        while (index < line.Length && line[index] == '`') {
            index++;
        }

        if (index - fenceStart < 3) {
            return line;
        }

        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        if (index >= line.Length) {
            return line;
        }

        var languageStart = index;
        while (index < line.Length && !char.IsWhiteSpace(line[index])) {
            index++;
        }

        if (index == languageStart) {
            return line;
        }

        var language = line.Substring(languageStart, index - languageStart);
        var replacement = language switch {
            "ix-chart" => "chart",
            "ix-network" => "network",
            "visnetwork" => "network",
            "ix-dataview" => "dataview",
            _ => string.Empty
        };
        if (replacement.Length == 0) {
            return line;
        }

        return line.Substring(0, languageStart)
               + replacement
               + line.Substring(index);
    }

}
