using System;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static bool ContainsLikelyJsonEnvelope(ReadOnlySpan<char> text) {
        if (LooksLikeJsonContainer(text.Trim())) {
            return true;
        }

        var lineStart = 0;
        while (lineStart < text.Length) {
            ReadOnlySpan<char> line;
            lineStart = ReadNextLine(text, lineStart, out line);
            if (LooksLikeJsonContainer(line.Trim())) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeJsonContainer(ReadOnlySpan<char> value) {
        if (value.Length < 2) {
            return false;
        }

        var first = value[0];
        var last = value[^1];
        return (first == '{' && last == '}') || (first == '[' && last == ']');
    }

    private static bool ContainsMarkdownTableContractSignal(ReadOnlySpan<char> text) {
        var lineStart = 0;
        while (lineStart < text.Length) {
            ReadOnlySpan<char> headerLine;
            lineStart = ReadNextLine(text, lineStart, out headerLine);
            if (!LooksLikeMarkdownTableHeaderRow(headerLine)) {
                continue;
            }

            if (lineStart >= text.Length) {
                return false;
            }

            ReadOnlySpan<char> separatorLine;
            _ = ReadNextLine(text, lineStart, out separatorLine);
            if (LooksLikeMarkdownTableSeparatorRow(separatorLine)) {
                return true;
            }
        }

        return false;
    }

    private static int ReadNextLine(ReadOnlySpan<char> text, int startIndex, out ReadOnlySpan<char> line) {
        if (startIndex >= text.Length) {
            line = ReadOnlySpan<char>.Empty;
            return text.Length;
        }

        var remaining = text.Slice(startIndex);
        var lineBreakIndex = remaining.IndexOfAny('\r', '\n');
        if (lineBreakIndex < 0) {
            line = remaining;
            return text.Length;
        }

        line = remaining.Slice(0, lineBreakIndex);
        var nextIndex = startIndex + lineBreakIndex + 1;
        if (nextIndex < text.Length && text[startIndex + lineBreakIndex] == '\r' && text[nextIndex] == '\n') {
            nextIndex++;
        }

        return nextIndex;
    }

    private static bool LooksLikeMarkdownTableHeaderRow(ReadOnlySpan<char> line) {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || trimmed.IndexOf('|') < 0) {
            return false;
        }

        var cellCount = 0;
        var hasTextualCell = false;
        var parts = trimmed.ToString().Split('|', StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++) {
            var cell = parts[i].Trim();
            if (cell.Length == 0) {
                continue;
            }

            cellCount++;
            if (ContainsLetterOrDigit(cell.AsSpan())) {
                hasTextualCell = true;
            }
        }

        return cellCount >= 2 && hasTextualCell;
    }

    private static bool LooksLikeMarkdownTableSeparatorRow(ReadOnlySpan<char> line) {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || trimmed.IndexOf('|') < 0) {
            return false;
        }

        var separatorCellCount = 0;
        var parts = trimmed.ToString().Split('|', StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++) {
            var cell = parts[i].Trim();
            if (cell.Length == 0) {
                continue;
            }

            var dashCount = 0;
            for (var j = 0; j < cell.Length; j++) {
                var ch = cell[j];
                if (ch == '-') {
                    dashCount++;
                    continue;
                }

                if (ch == ':') {
                    continue;
                }

                return false;
            }

            if (dashCount < 3) {
                return false;
            }

            separatorCellCount++;
        }

        return separatorCellCount >= 2;
    }

    private static bool ContainsLetterOrDigit(ReadOnlySpan<char> value) {
        for (var i = 0; i < value.Length; i++) {
            if (char.IsLetterOrDigit(value[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsVisualContractFenceLanguage(ReadOnlySpan<char> language) {
        if (language.IsEmpty) {
            return false;
        }

        for (var i = 0; i < VisualContractFenceLanguages.Length; i++) {
            if (language.Equals(VisualContractFenceLanguages[i].AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string GetSupportedProactiveVisualBlockListText() {
        return string.Join("/", ProactiveVisualPromptFenceLanguages);
    }

    private static bool TryResolvePreferredVisualTypeToken(ReadOnlySpan<char> value, out string preferredVisualType) {
        preferredVisualType = string.Empty;
        var normalized = NormalizeCompactToken(value);
        if (normalized.Length == 0) {
            return false;
        }

        if (!PreferredVisualTypeByToken.TryGetValue(normalized, out var resolved) || string.IsNullOrWhiteSpace(resolved)) {
            return false;
        }

        preferredVisualType = resolved;
        return true;
    }
}
