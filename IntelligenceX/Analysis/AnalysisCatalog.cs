using System;
using System.Collections.Generic;

namespace IntelligenceX.Analysis;

/// <summary>
/// In-memory view of rule and pack definitions.
/// </summary>
public sealed class AnalysisCatalog {
    /// <summary>
    /// Creates a catalog from rule and pack dictionaries.
    /// </summary>
    public AnalysisCatalog(IReadOnlyDictionary<string, AnalysisRule> rules,
        IReadOnlyDictionary<string, AnalysisPack> packs) {
        Rules = rules ?? new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase);
        Packs = packs ?? new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rule definitions keyed by rule ID.
    /// </summary>
    public IReadOnlyDictionary<string, AnalysisRule> Rules { get; }
    /// <summary>
    /// Pack definitions keyed by pack ID.
    /// </summary>
    public IReadOnlyDictionary<string, AnalysisPack> Packs { get; }

    /// <summary>
    /// Try to get a rule by ID.
    /// </summary>
    public bool TryGetRule(string id, out AnalysisRule rule) {
        if (string.IsNullOrWhiteSpace(id)) {
            rule = null!;
            return false;
        }
        return Rules.TryGetValue(id.Trim(), out rule!);
    }

    /// <summary>
    /// Try to get a pack by ID.
    /// </summary>
    public bool TryGetPack(string id, out AnalysisPack pack) {
        if (string.IsNullOrWhiteSpace(id)) {
            pack = null!;
            return false;
        }
        return Packs.TryGetValue(id.Trim(), out pack!);
    }
}
