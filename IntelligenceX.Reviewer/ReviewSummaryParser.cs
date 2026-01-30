using System;
using System.IO;

namespace IntelligenceX.Reviewer;

internal static class ReviewSummaryParser {
    public static bool TryGetReviewedCommit(string? body, out string? commit) {
        commit = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }

        var marker = ReviewFormatter.ReviewedCommitMarker;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                continue;
            }
            var slice = line.Substring(index + marker.Length);
            var start = slice.IndexOf('`');
            if (start < 0) {
                return false;
            }
            slice = slice.Substring(start + 1);
            var end = slice.IndexOf('`');
            if (end < 0) {
                return false;
            }
            var token = slice.Substring(0, end).Trim();
            if (token.Length == 0) {
                return false;
            }
            commit = token;
            return true;
        }

        return false;
    }
}
