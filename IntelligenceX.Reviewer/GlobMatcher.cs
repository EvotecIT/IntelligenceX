using System;
using System.Text.RegularExpressions;

namespace IntelligenceX.Reviewer;

internal static class GlobMatcher {
    public static bool IsMatch(string pattern, string value) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            return false;
        }
        var regex = ConvertGlobToRegex(pattern.Trim());
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
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
