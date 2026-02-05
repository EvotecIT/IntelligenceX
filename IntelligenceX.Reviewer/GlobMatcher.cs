using System;
using System.Text.RegularExpressions;

namespace IntelligenceX.Reviewer;

internal static class GlobMatcher {
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static bool IsMatch(string pattern, string value) {
        if (string.IsNullOrWhiteSpace(pattern) || value is null) {
            return false;
        }
        var regex = ConvertGlobToRegex(pattern.Trim());
        try {
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase, RegexTimeout);
        } catch (RegexMatchTimeoutException) {
            return false;
        } catch (ArgumentException) {
            return false;
        }
    }

    private static string ConvertGlobToRegex(string pattern) {
        var escaped = Regex.Escape(pattern);
        escaped = escaped.Replace(@"\*\*", "___WILDCARD_MULTI___");
        escaped = escaped.Replace(@"\*", "___WILDCARD_SINGLE___");
        escaped = escaped.Replace(@"\?", "___WILDCARD_ONE___");

        escaped = escaped.Replace("___WILDCARD_MULTI___", ".*");
        escaped = escaped.Replace("___WILDCARD_SINGLE___", "[^/]*");
        escaped = escaped.Replace("___WILDCARD_ONE___", ".");

        return "^" + escaped + "$";
    }
}
