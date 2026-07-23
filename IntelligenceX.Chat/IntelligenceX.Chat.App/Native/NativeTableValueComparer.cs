using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Compares table values as numbers, timestamps, versions, and finally natural text.
/// </summary>
internal sealed class NativeTableValueComparer : IComparer<string> {
    public static NativeTableValueComparer Instance { get; } = new();

    public int Compare(string? left, string? right) {
        left ??= string.Empty;
        right ??= string.Empty;
        if (decimal.TryParse(left, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var rightNumber)) {
            return leftNumber.CompareTo(rightNumber);
        }

        if (DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var leftDate)
            && DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var rightDate)) {
            return leftDate.CompareTo(rightDate);
        }

        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion)) {
            return leftVersion.CompareTo(rightVersion);
        }

        return CompareNatural(left, right);
    }

    private static int CompareNatural(string left, string right) {
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length) {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex])) {
                var leftStart = leftIndex;
                var rightStart = rightIndex;
                while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
                while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;
                var leftDigits = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
                var rightDigits = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');
                var length = leftDigits.Length.CompareTo(rightDigits.Length);
                if (length != 0) return length;
                var digits = leftDigits.CompareTo(rightDigits, StringComparison.Ordinal);
                if (digits != 0) return digits;
                continue;
            }

            var characters = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characters != 0) return characters;
            leftIndex++;
            rightIndex++;
        }

        return left.Length.CompareTo(right.Length);
    }
}
