using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Native.Rendering;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Product-neutral column measurement used by both inline and expanded native tables.
/// </summary>
internal static class NativeTableColumnLayout {
    public const double MinimumWidth = 112;
    public const double MaximumPreferredWidth = 420;
    public const double MaximumExpandedWidth = 560;
    private const int MaximumMeasuredRows = 250;
    private const double CellChromeWidth = 34;
    private const double AverageCharacterWidth = 7.2;

    public static IReadOnlyList<double> MeasurePreferredWidths(NativeTranscriptTable table) {
        if (table == null) throw new ArgumentNullException(nameof(table));
        var widths = new double[table.Headers.Count];
        for (var column = 0; column < widths.Length; column++) {
            var longest = WeightedLength(table.Headers[column]);
            for (var row = 0; row < table.Rows.Count && row < MaximumMeasuredRows; row++) {
                if (column < table.Rows[row].Count) {
                    longest = Math.Max(longest, WeightedLength(table.Rows[row][column]));
                }
            }

            widths[column] = Math.Clamp(
                CellChromeWidth + longest * AverageCharacterWidth,
                MinimumWidth,
                MaximumPreferredWidth);
        }

        return widths;
    }

    public static IReadOnlyList<double> FitToViewport(
        IReadOnlyList<double> preferredWidths,
        double viewportWidth,
        double leadingWidth = 0) {
        if (preferredWidths == null) throw new ArgumentNullException(nameof(preferredWidths));
        var result = preferredWidths.Select(width => Math.Clamp(width, MinimumWidth, MaximumExpandedWidth)).ToArray();
        var available = Math.Max(0, viewportWidth - Math.Max(0, leadingWidth));
        var remaining = available - result.Sum();
        while (remaining > 0.5) {
            var expandable = Enumerable.Range(0, result.Length)
                .Where(index => result[index] < MaximumExpandedWidth - 0.5)
                .ToArray();
            if (expandable.Length == 0) break;
            var share = remaining / expandable.Length;
            var consumed = 0d;
            foreach (var index in expandable) {
                var growth = Math.Min(share, MaximumExpandedWidth - result[index]);
                result[index] += growth;
                consumed += growth;
            }

            if (consumed < 0.5) break;
            remaining -= consumed;
        }

        return result;
    }

    public static double Resize(double currentWidth, double horizontalDelta) =>
        Math.Clamp(currentWidth + horizontalDelta, MinimumWidth, MaximumExpandedWidth);

    private static double WeightedLength(string? value) {
        if (string.IsNullOrEmpty(value)) return 0;
        var longest = 0d;
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')) {
            var length = 0d;
            foreach (var character in line) {
                length += character > 0x2ff ? 1.75 : character is 'W' or 'M' or 'w' or 'm' ? 1.25 : 1;
            }

            longest = Math.Max(longest, length);
        }

        return longest;
    }
}
