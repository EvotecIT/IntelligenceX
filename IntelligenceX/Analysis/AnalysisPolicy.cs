using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Analysis;

/// <summary>
/// Selected analysis rule and effective severity.
/// </summary>
public sealed class AnalysisPolicyRule {
    /// <summary>
    /// Creates a selected policy rule.
    /// </summary>
    public AnalysisPolicyRule(AnalysisRule rule, string severity) {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Severity = string.IsNullOrWhiteSpace(severity) ? rule.DefaultSeverity : severity;
    }

    /// <summary>
    /// Rule metadata.
    /// </summary>
    public AnalysisRule Rule { get; }
    /// <summary>
    /// Effective severity after pack and user overrides.
    /// </summary>
    public string Severity { get; }
}

/// <summary>
/// Fully resolved policy from configured packs and rule overrides.
/// </summary>
public sealed class AnalysisPolicy {
    /// <summary>
    /// Creates a policy snapshot.
    /// </summary>
    public AnalysisPolicy(IReadOnlyDictionary<string, AnalysisPolicyRule> rules, IReadOnlyList<string> warnings) {
        Rules = rules ?? new Dictionary<string, AnalysisPolicyRule>(StringComparer.OrdinalIgnoreCase);
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>
    /// Selected rules keyed by catalog rule id.
    /// </summary>
    public IReadOnlyDictionary<string, AnalysisPolicyRule> Rules { get; }
    /// <summary>
    /// Non-fatal warnings while resolving packs/rules.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Returns selected rules that match any of the provided languages.
    /// </summary>
    public IReadOnlyList<AnalysisPolicyRule> SelectByLanguage(params string[] languages) {
        if (Rules.Count == 0 || languages is null || languages.Length == 0) {
            return Array.Empty<AnalysisPolicyRule>();
        }
        var set = new HashSet<string>(languages
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()), StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) {
            return Array.Empty<AnalysisPolicyRule>();
        }
        return Rules.Values
            .Where(value => set.Contains(value.Rule.Language.Trim()))
            .ToList();
    }
}
