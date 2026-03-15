using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    private static readonly Regex InlineCodeSpanRegex = new(
        @"`[^`\r\n]*`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnmatchedInlineCodeTailRegex = new(
        @"`[^\r\n]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex InlineCodePlaceholderRegex = new(
        "\u001FIXCODE_(?<prefix>[0-9a-f]{8})_(?<index>\\d+)\u001E",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ZeroWidthWhitespaceRegex = new(
        @"[\u200B\u2060\uFEFF]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static int InlineCodePlaceholderCounter;

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(normalized);

        return ApplyTransformOutsideFencedCodeBlocks(
            normalized,
            static segment => ApplyTransformPreservingInlineCodeSpans(
                segment,
                OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup));
    }

    /// <summary>
    /// Lightweight sanitizer for partial streaming text before render.
    /// </summary>
    public static string NormalizeForStreamingPreview(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(normalized);

        var officeImoPreview = TryNormalizeWithOfficeImoStreamingPreview(normalized);
        if (officeImoPreview != null) {
            return officeImoPreview;
        }

        return ApplyTransformOutsideFencedCodeBlocks(
            normalized,
            static segment => ApplyTransformPreservingInlineCodeSpans(
                ZeroWidthWhitespaceRegex.Replace(segment, string.Empty),
                OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup));
    }

    public static bool TryRepairLegacyTranscript(string? text, out string normalized) {
        normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return false;
        }

        var repaired = NormalizeForRendering(normalized);
        if (repaired == normalized) {
            return false;
        }

        normalized = repaired;
        return true;
    }

    private static string? TryNormalizeWithOfficeImoStreamingPreview(string text) {
        try {
            var previewType = Type.GetType(
                "OfficeIMO.Markdown.MarkdownStreamingPreviewNormalizer, OfficeIMO.Markdown",
                throwOnError: false);
            var method = previewType?.GetMethod(
                "NormalizeIntelligenceXTranscript",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (method?.ReturnType != typeof(string)) {
                return null;
            }

            return method.Invoke(null, [text]) as string;
        } catch {
            return null;
        }
    }

    private static string ApplyTransformOutsideFencedCodeBlocks(string text, Func<string, string> transform) {
        if (text.Length == 0) {
            return string.Empty;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new StringBuilder(normalized.Length);
        var proseBuffer = new StringBuilder();
        var insideFence = false;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var isFenceLine = line.TrimStart().StartsWith("```", StringComparison.Ordinal);
            if (isFenceLine) {
                FlushProseBuffer(proseBuffer, output, transform);
                AppendLine(output, line, i < lines.Length - 1);
                insideFence = !insideFence;
                continue;
            }

            if (insideFence) {
                AppendLine(output, line, i < lines.Length - 1);
                continue;
            }

            proseBuffer.Append(line);
            if (i < lines.Length - 1) {
                proseBuffer.Append('\n');
            }
        }

        FlushProseBuffer(proseBuffer, output, transform);

        var result = output.ToString();
        return string.Equals(newline, "\n", StringComparison.Ordinal)
            ? result
            : result.Replace("\n", newline, StringComparison.Ordinal);
    }

    private static string ApplyTransformPreservingInlineCodeSpans(string text, Func<string, string> transform) {
        if (text.Length == 0) {
            return string.Empty;
        }

        var prefixId = unchecked((uint)Interlocked.Increment(ref InlineCodePlaceholderCounter))
            .ToString("x8", CultureInfo.InvariantCulture);
        var tokenPrefix = "\u001FIXCODE_" + prefixId + "_";
        var codeSpans = new List<string>();
        var tokenized = InlineCodeSpanRegex.Replace(text, match => {
            var index = codeSpans.Count;
            codeSpans.Add(match.Value);
            return tokenPrefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });
        tokenized = UnmatchedInlineCodeTailRegex.Replace(tokenized, match => {
            var index = codeSpans.Count;
            codeSpans.Add(match.Value);
            return tokenPrefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });

        var transformed = transform(tokenized);
        if (codeSpans.Count == 0 || transformed.Length == 0) {
            return transformed;
        }

        return InlineCodePlaceholderRegex.Replace(transformed, match => {
            if (!match.Value.StartsWith(tokenPrefix, StringComparison.Ordinal)) {
                return match.Value;
            }

            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) {
                return match.Value;
            }

            return index >= 0 && index < codeSpans.Count
                ? codeSpans[index]
                : match.Value;
        });
    }

    private static void FlushProseBuffer(StringBuilder proseBuffer, StringBuilder output, Func<string, string> transform) {
        if (proseBuffer.Length == 0) {
            return;
        }

        output.Append(transform(proseBuffer.ToString()));
        proseBuffer.Clear();
    }

    private static void AppendLine(StringBuilder builder, string line, bool appendNewLine) {
        builder.Append(line);
        if (appendNewLine) {
            builder.Append('\n');
        }
    }
}
