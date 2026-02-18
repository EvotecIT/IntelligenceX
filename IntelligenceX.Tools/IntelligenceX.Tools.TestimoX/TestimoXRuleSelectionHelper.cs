using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestimoX.Definitions;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXRuleSelectionHelper {
    internal const string RuleOriginAny = "any";
    internal const string RuleOriginBuiltin = "builtin";
    internal const string RuleOriginExternal = "external";
    internal const int MaxRuleNamePatterns = 64;

    internal static readonly string[] SourceTypeNames = {
        "powershell",
        "csharp"
    };

    internal static readonly string[] RuleOriginNames = {
        RuleOriginAny,
        RuleOriginBuiltin,
        RuleOriginExternal
    };

    internal static bool TryParseSourceTypes(
        IReadOnlyList<string> requestedValues,
        out HashSet<string>? parsedSourceTypes,
        out string? error) {
        parsedSourceTypes = null;
        error = null;

        if (requestedValues is null || requestedValues.Count == 0) {
            return true;
        }

        var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < requestedValues.Count; i++) {
            var raw = requestedValues[i];
            var normalized = NormalizeSourceType(raw);
            if (normalized.Length == 0) {
                error = $"source_types[{i}] ('{raw}') is invalid. Allowed values: powershell, csharp.";
                return false;
            }
            parsed.Add(normalized);
        }

        parsedSourceTypes = parsed.Count == 0 ? null : parsed;
        return true;
    }

    internal static bool TryParseRuleOrigin(string? rawValue, out string ruleOrigin, out string? error) {
        error = null;
        var normalized = NormalizeRuleOrigin(rawValue);
        if (normalized.Length == 0) {
            error = $"rule_origin must be one of: {string.Join(", ", RuleOriginNames)}.";
            ruleOrigin = RuleOriginAny;
            return false;
        }

        ruleOrigin = normalized;
        return true;
    }

    internal static string GetSourceType(Rule? rule) {
        if (rule?.Source is null) {
            return "powershell";
        }

        return NormalizeSourceType(rule.Source.SourceType.ToString()) switch {
            "csharp" => "csharp",
            _ => "powershell"
        };
    }

    internal static bool MatchesSourceType(Rule rule, HashSet<string>? sourceTypeFilter) {
        if (sourceTypeFilter is null || sourceTypeFilter.Count == 0) {
            return true;
        }

        return sourceTypeFilter.Contains(GetSourceType(rule));
    }

    internal static string ResolveRuleOrigin(Rule rule, bool usingExternalDirectory, HashSet<string>? builtinRuleNames) {
        if (!usingExternalDirectory) {
            return RuleOriginBuiltin;
        }

        var name = rule.Name ?? string.Empty;
        if (name.Length == 0) {
            return RuleOriginExternal;
        }

        if (builtinRuleNames is not null && builtinRuleNames.Contains(name)) {
            return RuleOriginBuiltin;
        }

        return RuleOriginExternal;
    }

    internal static async Task<HashSet<string>> DiscoverBuiltinRuleNamesAsync(CancellationToken cancellationToken) {
        var runner = new TestimoRunner();
        var baseline = await runner.DiscoverRulesAsync(
            includeDisabled: true,
            ct: cancellationToken,
            powerShellRulesDirectory: null).ConfigureAwait(false);

        return new HashSet<string>(
            baseline
                .Select(static x => x.Name)
                .Where(static x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static bool MatchesAnyPattern(Rule rule, IReadOnlyList<string> patterns) {
        if (patterns is null || patterns.Count == 0) {
            return true;
        }

        var name = rule.Name ?? string.Empty;
        var displayName = rule.DisplayName ?? string.Empty;
        for (var i = 0; i < patterns.Count; i++) {
            var pattern = patterns[i];
            if (WildcardMatch(name, pattern) || WildcardMatch(displayName, pattern)) {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsIgnoreCase(string? value, string term) {
        return !string.IsNullOrWhiteSpace(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static IEnumerable<Rule> ApplyVisibilityFilters(
        IEnumerable<Rule> rules,
        bool includeDisabled,
        bool includeHidden,
        bool includeDeprecated) {
        var filtered = rules;
        if (!includeDisabled) {
            filtered = filtered.Where(static x => x.Enable);
        }
        if (!includeHidden) {
            filtered = filtered.Where(static x => x.Visibility != RuleVisibility.Hidden);
        }
        if (!includeDeprecated) {
            filtered = filtered.Where(static x => !x.IsDeprecated);
        }

        return filtered;
    }

    internal static IEnumerable<Rule> ApplySharedFilters(
        IEnumerable<Rule> rules,
        string? searchText,
        IReadOnlyList<string> requestedCategories,
        IReadOnlyList<string> requestedTags,
        HashSet<string>? sourceTypeFilter,
        string ruleOrigin,
        bool usingExternalDirectory,
        HashSet<string>? builtinRuleNames) {
        var filtered = rules;
        if (!string.IsNullOrWhiteSpace(searchText)) {
            var term = searchText.Trim();
            filtered = filtered.Where(rule =>
                ContainsIgnoreCase(rule.Name, term) ||
                ContainsIgnoreCase(rule.DisplayName, term) ||
                ContainsIgnoreCase(rule.Description, term));
        }

        if (requestedCategories.Count > 0) {
            var requested = new HashSet<string>(requestedCategories, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Category.Any(cat => requested.Contains(cat.ToString())));
        }

        if (requestedTags.Count > 0) {
            var requested = new HashSet<string>(requestedTags, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(rule => rule.Tags.Any(tag => requested.Contains(tag)));
        }

        if (sourceTypeFilter is { Count: > 0 }) {
            filtered = filtered.Where(rule => MatchesSourceType(rule, sourceTypeFilter));
        }

        if (!string.Equals(ruleOrigin, RuleOriginAny, StringComparison.OrdinalIgnoreCase)) {
            filtered = filtered.Where(rule =>
                string.Equals(
                    ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
                    ruleOrigin,
                    StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    private static string NormalizeSourceType(string? value) {
        var canonical = Canonicalize(value);
        return canonical switch {
            "powershell" => "powershell",
            "ps" => "powershell",
            "csharp" => "csharp",
            "cs" => "csharp",
            "dotnet" => "csharp",
            _ => string.Empty
        };
    }

    private static string NormalizeRuleOrigin(string? value) {
        var canonical = Canonicalize(value);
        return canonical switch {
            "" => RuleOriginAny,
            "any" => RuleOriginAny,
            "all" => RuleOriginAny,
            "*" => RuleOriginAny,
            "builtin" => RuleOriginBuiltin,
            "bundled" => RuleOriginBuiltin,
            "default" => RuleOriginBuiltin,
            "external" => RuleOriginExternal,
            "custom" => RuleOriginExternal,
            "user" => RuleOriginExternal,
            _ => string.Empty
        };
    }

    private static string Canonicalize(string? value) {
        return (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static bool WildcardMatch(string? value, string pattern) {
        var candidate = value ?? string.Empty;
        if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0) {
            return string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = "^" +
                           Regex.Escape(pattern)
                               .Replace("\\*", ".*", StringComparison.Ordinal)
                               .Replace("\\?", ".", StringComparison.Ordinal) +
                           "$";
        return Regex.IsMatch(candidate, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
