namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static IReadOnlyList<(string RuleId, string Path)> ReadFindingsRulePathPairs(string findingsPath) {
        var content = File.ReadAllText(findingsPath);
        using var document = System.Text.Json.JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != System.Text.Json.JsonValueKind.Array) {
            return Array.Empty<(string RuleId, string Path)>();
        }

        var list = new List<(string RuleId, string Path)>();
        foreach (var item in items.EnumerateArray()) {
            var ruleId = item.TryGetProperty("ruleId", out var ruleIdValue) ? ruleIdValue.GetString() ?? string.Empty : string.Empty;
            var path = item.TryGetProperty("path", out var pathValue) ? pathValue.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(ruleId) && !string.IsNullOrWhiteSpace(path)) {
                list.Add((ruleId, path));
            }
        }

        return list;
    }

    private static int CountFindings(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string? path = null) {
        ArgumentNullException.ThrowIfNull(findings);
        var requiredRuleId = RequireNonEmpty(ruleId, nameof(ruleId));
        var hasPath = path is not null;
        if (hasPath) {
            path = RequireNonEmpty(path, nameof(path));
        }

        if (findings.Count == 0) {
            return 0;
        }

        return findings.Count(item =>
            item.RuleId.Equals(requiredRuleId, StringComparison.OrdinalIgnoreCase) &&
            (!hasPath || item.Path.Equals(path!, StringComparison.OrdinalIgnoreCase)));
    }

    private static int CountFindingsByPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix) {
        ArgumentNullException.ThrowIfNull(findings);
        var requiredRuleId = RequireNonEmpty(ruleId, nameof(ruleId));
        var requiredSuffix = RequireNonEmpty(pathSuffix, nameof(pathSuffix));
        if (findings.Count == 0) {
            return 0;
        }

        return findings.Count(item =>
            item.RuleId.Equals(requiredRuleId, StringComparison.OrdinalIgnoreCase) &&
            item.Path.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertHasFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindings(findings, ruleId);
        AssertEqual(true, matchCount > 0, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertHasFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId, string path,
        string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindings(findings, ruleId, path);
        AssertEqual(true, matchCount > 0, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertHasExactlyOneFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string path, string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindings(findings, ruleId, path);
        AssertEqual(1, matchCount, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertNoFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindings(findings, ruleId);
        AssertEqual(0, matchCount, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertNoFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId, string path,
        string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindings(findings, ruleId, path);
        AssertEqual(0, matchCount, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertHasFindingWithPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix, string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindingsByPathSuffix(findings, ruleId, pathSuffix);
        AssertEqual(true, matchCount > 0, $"{requiredMessage} (matches={matchCount})");
    }

    private static void AssertNoFindingWithPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix, string assertionMessage) {
        var requiredMessage = RequireNonEmpty(assertionMessage, nameof(assertionMessage));
        var matchCount = CountFindingsByPathSuffix(findings, ruleId, pathSuffix);
        AssertEqual(0, matchCount, $"{requiredMessage} (matches={matchCount})");
    }

    private static string RequireNonEmpty(string? value, string parameterName) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }

        return value;
    }
}
#endif
