using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IntelligenceX.Reviewer;

internal static class Redaction {
    public static string Apply(string input, IReadOnlyList<string> patterns, string replacement) {
        if (patterns.Count == 0) {
            return input;
        }
        var output = input;
        foreach (var pattern in patterns) {
            if (string.IsNullOrWhiteSpace(pattern)) {
                continue;
            }
            try {
                output = Regex.Replace(output, pattern, replacement, RegexOptions.IgnoreCase);
            } catch {
                // Ignore invalid patterns.
            }
        }
        return output;
    }
}
