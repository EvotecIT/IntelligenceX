using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Shared transcript markdown preparation contract used by export and DOCX flows.
/// Prefers the explicit OfficeIMO transcript-preparation APIs when available and
/// falls back to the published package baseline while those APIs are still unreleased.
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

        return TryInvokeTranscriptPreparationMethod("PrepareIntelligenceXTranscriptBody", value)
               ?? ExpandAdjacentOrderedListItems(value);
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

        return TryInvokeTranscriptPreparationMethod("PrepareIntelligenceXTranscriptForExport", withoutMarkers)
               ?? CollapseDuplicateBlankLines(PrepareMessageBody(withoutMarkers));
    }

    /// <summary>
    /// Applies the DOCX-specific normalization contract after the transcript markdown has already been prepared for export.
    /// </summary>
    public static string PrepareTranscriptMarkdownForDocx(string markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        return TryInvokeTranscriptPreparationForDocx(markdown, preservesGroupedDefinitionLikeParagraphs)
               ?? PrepareTranscriptMarkdownForDocxFallback(markdown, preservesGroupedDefinitionLikeParagraphs);
    }

    private static string PrepareTranscriptMarkdownForDocxFallback(string? markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        var prepared = PrepareMessageBody(markdown);
        if (preservesGroupedDefinitionLikeParagraphs) {
            return prepared;
        }

        return SeparateAdjacentDefinitionLikeLinesOutsideFencedCodeBlocks(prepared);
    }

    private static string? TryInvokeTranscriptPreparationMethod(string methodName, string markdown) {
        try {
            var preparationType = Type.GetType(
                "OfficeIMO.Markdown.MarkdownTranscriptPreparation, OfficeIMO.Markdown",
                throwOnError: false);
            var method = preparationType?.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (method?.ReturnType != typeof(string)) {
                return null;
            }

            return method.Invoke(null, [markdown]) as string;
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            return null;
        }
    }

    private static string? TryInvokeTranscriptPreparationForDocx(string markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        try {
            var preparationType = Type.GetType(
                "OfficeIMO.Markdown.MarkdownTranscriptPreparation, OfficeIMO.Markdown",
                throwOnError: false);
            var method = preparationType?.GetMethod(
                "PrepareIntelligenceXTranscriptForDocx",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(bool)],
                modifiers: null);
            if (method?.ReturnType != typeof(string)) {
                return null;
            }

            return method.Invoke(null, [markdown, preservesGroupedDefinitionLikeParagraphs]) as string;
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            return null;
        }
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

    private static string CollapseDuplicateBlankLines(string markdown) {
        if (markdown.Length == 0) {
            return string.Empty;
        }

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        var previousWasBlank = false;

        foreach (var rawLine in lines) {
            var line = rawLine ?? string.Empty;
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousWasBlank) {
                continue;
            }

            output.Add(line);
            previousWasBlank = isBlank;
        }

        return string.Join(newline, output);
    }

    private static string ExpandAdjacentOrderedListItems(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0) {
            return text;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
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

        var prepared = sb.ToString();
        return string.Equals(newline, "\n", StringComparison.Ordinal)
            ? prepared
            : prepared.Replace("\n", newline, StringComparison.Ordinal);
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

    private static string SeparateAdjacentDefinitionLikeLinesOutsideFencedCodeBlocks(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return string.Empty;
        }

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length + 4);
        var insideFence = false;

        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            if (IsFenceLine(current)) {
                insideFence = !insideFence;
                output.Add(current);
                continue;
            }

            output.Add(current);
            if (insideFence || i >= lines.Length - 1) {
                continue;
            }

            var next = lines[i + 1] ?? string.Empty;
            if (IsDefinitionLikeLine(current) && IsDefinitionLikeLine(next) && !string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(next)) {
                output.Add(string.Empty);
            }
        }

        return string.Join(newline, output);
    }

    private static bool IsFenceLine(string line) {
        return line.TrimStart().StartsWith("```", StringComparison.Ordinal);
    }

    private static bool IsDefinitionLikeLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var trimmed = line.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= trimmed.Length - 1) {
            return false;
        }

        for (var i = 0; i < colonIndex; i++) {
            var ch = trimmed[i];
            if (char.IsControl(ch)) {
                return false;
            }
        }

        return !char.IsWhiteSpace(trimmed[colonIndex + 1]);
    }

    private static bool IsCompatibilityFallbackException(Exception exception) {
        var unwrapped = UnwrapInvocationException(exception);
        return unwrapped is TypeLoadException
            or FileNotFoundException
            or FileLoadException
            or BadImageFormatException
            or MissingMethodException
            or MissingMemberException
            or MemberAccessException
            or NotSupportedException
            or InvalidCastException;
    }

    private static Exception UnwrapInvocationException(Exception exception) {
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } invocationException) {
            current = invocationException.InnerException!;
        }

        return current;
    }
}
