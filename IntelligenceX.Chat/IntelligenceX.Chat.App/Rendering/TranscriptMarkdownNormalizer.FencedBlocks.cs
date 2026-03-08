using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static partial class TranscriptMarkdownNormalizer {
    private static string UpgradeLegacyVisualFences(string input) {
        if (string.IsNullOrEmpty(input) || input.IndexOf("```", StringComparison.Ordinal) < 0) {
            return input ?? string.Empty;
        }

        var output = new StringBuilder(input.Length);
        var index = 0;
        while (index < input.Length) {
            var lineStart = index;
            while (index < input.Length && input[index] != '\r' && input[index] != '\n') {
                index++;
            }

            var lineEnd = index;
            if (index < input.Length && input[index] == '\r') {
                index++;
                if (index < input.Length && input[index] == '\n') {
                    index++;
                }
            } else if (index < input.Length && input[index] == '\n') {
                index++;
            }

            var line = input.Substring(lineStart, lineEnd - lineStart);
            var lineWithNewline = input.Substring(lineStart, index - lineStart);
            if (!TryReadFenceRun(line, out var runMarker, out var runLength, out _)) {
                output.Append(lineWithNewline);
                continue;
            }

            var blockBuilder = new StringBuilder(lineWithNewline.Length + 256);
            blockBuilder.Append(lineWithNewline);
            var contentBuilder = new StringBuilder();
            var foundClosingFence = false;
            while (index < input.Length) {
                var innerLineStart = index;
                while (index < input.Length && input[index] != '\r' && input[index] != '\n') {
                    index++;
                }

                var innerLineEnd = index;
                if (index < input.Length && input[index] == '\r') {
                    index++;
                    if (index < input.Length && input[index] == '\n') {
                        index++;
                    }
                } else if (index < input.Length && input[index] == '\n') {
                    index++;
                }

                var innerLine = input.Substring(innerLineStart, innerLineEnd - innerLineStart);
                var innerLineWithNewline = input.Substring(innerLineStart, index - innerLineStart);
                if (TryReadFenceRun(innerLine, out var closingMarker, out var closingLength, out var closingSuffix)
                    && closingMarker == runMarker
                    && closingLength >= runLength
                    && string.IsNullOrWhiteSpace(closingSuffix)) {
                    foundClosingFence = true;
                    blockBuilder.Append(innerLineWithNewline);
                    break;
                }

                contentBuilder.Append(innerLineWithNewline);
                blockBuilder.Append(innerLineWithNewline);
            }

            if (!foundClosingFence || !ShouldUpgradeLegacyJsonFenceToIxNetwork(line, contentBuilder.ToString())) {
                output.Append(blockBuilder);
                continue;
            }

            output.Append(RewriteFenceOpeningLine(lineWithNewline, "ix-network"));
            output.Append(contentBuilder);
            var closingIndex = blockBuilder.Length - (index > 0 ? 0 : 0);
            var originalBlock = blockBuilder.ToString();
            var openingLength = lineWithNewline.Length;
            var contentLength = contentBuilder.Length;
            output.Append(originalBlock.Substring(openingLength + contentLength));
        }

        return output.ToString();
    }

    private static bool ContainsLegacyJsonVisualFenceCandidate(string input) {
        if (string.IsNullOrEmpty(input)) {
            return false;
        }

        return input.IndexOf("```json", StringComparison.OrdinalIgnoreCase) >= 0
               || input.IndexOf("```", StringComparison.Ordinal) >= 0 && input.IndexOf("\"nodes\"", StringComparison.OrdinalIgnoreCase) >= 0
               && input.IndexOf("\"edges\"", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ApplyTransformOutsideFencedCodeBlocks(string input, Func<string, string> transform) {
        if (string.IsNullOrEmpty(input)) {
            return input ?? string.Empty;
        }

        var output = new StringBuilder(input.Length);
        var outsideSegment = new StringBuilder();
        var inFence = false;
        var fenceMarker = '\0';
        var fenceRunLength = 0;

        var index = 0;
        while (index < input.Length) {
            var lineStart = index;
            while (index < input.Length && input[index] != '\r' && input[index] != '\n') {
                index++;
            }

            var lineEnd = index;
            if (index < input.Length && input[index] == '\r') {
                index++;
                if (index < input.Length && input[index] == '\n') {
                    index++;
                }
            } else if (index < input.Length && input[index] == '\n') {
                index++;
            }

            var line = input.Substring(lineStart, lineEnd - lineStart);
            var lineWithNewline = input.Substring(lineStart, index - lineStart);

            if (TryReadFenceRun(line, out var runMarker, out var runLength, out var runSuffix)) {
                if (!inFence) {
                    FlushOutsideSegment(output, outsideSegment, transform);
                    inFence = true;
                    fenceMarker = runMarker;
                    fenceRunLength = runLength;
                    output.Append(lineWithNewline);
                    continue;
                }

                if (runMarker == fenceMarker && runLength >= fenceRunLength && string.IsNullOrWhiteSpace(runSuffix)) {
                    inFence = false;
                    fenceMarker = '\0';
                    fenceRunLength = 0;
                    output.Append(lineWithNewline);
                    continue;
                }
            }

            if (inFence) {
                output.Append(lineWithNewline);
            } else {
                outsideSegment.Append(lineWithNewline);
            }
        }

        FlushOutsideSegment(output, outsideSegment, transform);
        return output.ToString();
    }

    private static void FlushOutsideSegment(StringBuilder output, StringBuilder outsideSegment, Func<string, string> transform) {
        if (outsideSegment.Length == 0) {
            return;
        }

        var transformed = transform(outsideSegment.ToString());
        output.Append(transformed);
        outsideSegment.Clear();
    }

    private static bool TryReadFenceRun(string line, out char marker, out int runLength, out string suffix) {
        marker = '\0';
        runLength = 0;
        suffix = string.Empty;
        if (line is null) {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) {
            return false;
        }

        var first = trimmed[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == first) {
            i++;
        }

        if (i < 3) {
            return false;
        }

        marker = first;
        runLength = i;
        suffix = trimmed.Substring(i);
        return true;
    }

    private static bool ShouldUpgradeLegacyJsonFenceToIxNetwork(string openingFenceLine, string fenceContent) {
        if (string.IsNullOrWhiteSpace(fenceContent)) {
            return false;
        }

        if (!TryReadFenceRun(openingFenceLine, out _, out _, out var suffix)) {
            return false;
        }

        var language = suffix.Trim();
        if (language.Length > 0 && !language.Equals("json", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(fenceContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            return document.RootElement.TryGetProperty("nodes", out var nodes)
                   && nodes.ValueKind == JsonValueKind.Array
                   && document.RootElement.TryGetProperty("edges", out var edges)
                   && edges.ValueKind == JsonValueKind.Array;
        } catch (JsonException) {
            return false;
        }
    }

    private static string RewriteFenceOpeningLine(string originalLineWithNewline, string language) {
        var newlineStart = originalLineWithNewline.Length;
        while (newlineStart > 0) {
            var ch = originalLineWithNewline[newlineStart - 1];
            if (ch != '\r' && ch != '\n') {
                break;
            }

            newlineStart--;
        }

        var newline = originalLineWithNewline[newlineStart..];
        var line = originalLineWithNewline[..newlineStart];
        var indentLength = 0;
        while (indentLength < line.Length && char.IsWhiteSpace(line[indentLength])) {
            indentLength++;
        }

        if (!TryReadFenceRun(line, out var marker, out var runLength, out _)) {
            return originalLineWithNewline;
        }

        return line[..indentLength]
               + new string(marker, runLength)
               + language
               + newline;
    }
}
