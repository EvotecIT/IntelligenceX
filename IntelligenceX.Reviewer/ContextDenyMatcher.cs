using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IntelligenceX.Reviewer;

internal static class ContextDenyMatcher {
    private static readonly TimeSpan DenyPatternTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly object DenyPatternLock = new();
    private static readonly HashSet<string> DenyPatternFailures = new(StringComparer.OrdinalIgnoreCase);

    public static bool Matches(string body, IReadOnlyList<string> patterns) {
        foreach (var pattern in patterns) {
            if (string.IsNullOrWhiteSpace(pattern)) {
                continue;
            }
            try {
                if (Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, DenyPatternTimeout)) {
                    return true;
                }
            } catch (RegexMatchTimeoutException ex) {
                LogDenyPatternOnce(pattern, $"Context deny regex timed out: '{pattern}'. {ex.Message}");
            } catch (ArgumentException ex) {
                LogDenyPatternOnce(pattern, $"Invalid context deny regex: '{pattern}'. {ex.Message}");
            }
        }
        return false;
    }

    private static void LogDenyPatternOnce(string pattern, string message) {
        lock (DenyPatternLock) {
            if (!DenyPatternFailures.Add(pattern)) {
                return;
            }
        }
        Console.Error.WriteLine(message);
    }
}
