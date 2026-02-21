using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.ExportArtifacts;

internal static partial class DocxVisualFenceMaterializer {
    private static int FindClosingFence(string[] lines, int startIndex, char marker, int markerRunLength) {
        for (var i = startIndex; i < lines.Length; i++) {
            if (TryReadFenceRun(lines[i], marker, out var runLength, out var remainder)
                && runLength >= markerRunLength
                && string.IsNullOrWhiteSpace(remainder)) {
                return i;
            }
        }

        return -1;
    }

    private static bool TryReadFenceStart(string line, out char marker, out int markerRunLength, out string language) {
        marker = '\0';
        markerRunLength = 0;
        language = string.Empty;

        var trimmed = (line ?? string.Empty).TrimStart();
        if (!TryReadFenceRun(trimmed, out marker, out markerRunLength, out var suffix)) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(suffix)) {
            return true;
        }

        var text = suffix.Trim();
        var firstSpace = text.IndexOfAny([' ', '\t']);
        language = firstSpace >= 0 ? text[..firstSpace] : text;
        language = language.Trim().ToLowerInvariant();
        return true;
    }

    private static bool TryReadFenceRun(string line, out char marker, out int runLength, out string remainder) {
        marker = '\0';
        runLength = 0;
        remainder = string.Empty;
        if (string.IsNullOrEmpty(line)) {
            return false;
        }

        var first = line[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var idx = 0;
        while (idx < line.Length && line[idx] == first) {
            idx++;
        }

        if (idx < 3) {
            return false;
        }

        marker = first;
        runLength = idx;
        remainder = line[idx..];
        return true;
    }

    private static bool TryReadFenceRun(string line, char expectedMarker, out int runLength, out string remainder) {
        runLength = 0;
        remainder = string.Empty;
        var trimmed = (line ?? string.Empty).TrimStart();
        if (!TryReadFenceRun(trimmed, out var marker, out runLength, out remainder)) {
            return false;
        }

        return marker == expectedMarker;
    }

}
