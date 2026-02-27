using System;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static bool ContainsFenceLanguage(string text, string language) {
        var expectedLanguage = (language ?? string.Empty).Trim();
        if (expectedLanguage.Length == 0 || string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var value = text.AsSpan();
        var lineStart = 0;
        while (lineStart < value.Length) {
            var lineEnd = value.Slice(lineStart).IndexOf('\n');
            if (lineEnd < 0) {
                lineEnd = value.Length - lineStart;
            }

            var line = value.Slice(lineStart, lineEnd).TrimStart();
            if (TryGetFenceLanguage(line, out var fenceLanguage)
                && fenceLanguage.Equals(expectedLanguage.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            lineStart += lineEnd + 1;
        }

        return false;
    }

    private static bool ContainsToken(string text, string token) {
        var expectedToken = (token ?? string.Empty).Trim();
        if (expectedToken.Length == 0 || string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        // Treat token as a structured signal only when used as an explicit inline-code token.
        // Accept single or repeated backtick delimiters and optional surrounding whitespace.
        var value = text.AsSpan();
        var index = 0;
        while (index < value.Length) {
            var start = value.Slice(index).IndexOf('`');
            if (start < 0) {
                break;
            }

            var fenceStart = index + start;
            var fenceLength = CountRepeated(value, fenceStart, '`');
            if (fenceLength <= 0) {
                index = fenceStart + 1;
                continue;
            }

            var contentStart = fenceStart + fenceLength;
            if (contentStart >= value.Length) {
                index = fenceStart + 1;
                continue;
            }

            var contentEnd = FindClosingRepeated(value, contentStart, '`', fenceLength);
            if (contentEnd < 0) {
                index = fenceStart + 1;
                continue;
            }

            var content = value.Slice(contentStart, contentEnd - contentStart).Trim();
            if (content.Equals(expectedToken.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            index = contentEnd + fenceLength;
        }

        return false;
    }

    private static bool TryGetFenceLanguage(ReadOnlySpan<char> line, out ReadOnlySpan<char> language) {
        language = default;
        if (line.Length < 4) {
            return false;
        }

        var fenceChar = line[0];
        if ((fenceChar != '`' && fenceChar != '~') || line[1] != fenceChar || line[2] != fenceChar) {
            return false;
        }

        var index = 3;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        if (index >= line.Length) {
            return false;
        }

        var start = index;
        while (index < line.Length && IsFenceLanguageChar(line[index])) {
            index++;
        }

        if (index <= start) {
            return false;
        }

        language = line.Slice(start, index - start);
        return true;
    }

    private static bool IsFenceLanguageChar(char ch) {
        return char.IsLetterOrDigit(ch) || ch is '-' or '_';
    }

    private static int CountRepeated(ReadOnlySpan<char> value, int start, char expected) {
        var index = start;
        while (index < value.Length && value[index] == expected) {
            index++;
        }

        return index - start;
    }

    private static int FindClosingRepeated(ReadOnlySpan<char> value, int start, char expected, int length) {
        if (length <= 0 || start >= value.Length) {
            return -1;
        }

        var index = start;
        while (index < value.Length) {
            var next = value.Slice(index).IndexOf(expected);
            if (next < 0) {
                return -1;
            }

            var candidate = index + next;
            if (CountRepeated(value, candidate, expected) == length) {
                return candidate;
            }

            index = candidate + 1;
        }

        return -1;
    }
}
