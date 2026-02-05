using System.Collections.Generic;

namespace IntelligenceX.Analysis;

/// <summary>
/// Defines a curated set of rules with optional severity overrides.
/// </summary>
public sealed class AnalysisPack {
    /// <summary>
    /// Creates a new pack definition.
    /// </summary>
    public AnalysisPack(string id, string label, string? description, IReadOnlyList<string> rules,
        IReadOnlyDictionary<string, string> severityOverrides, string? sourcePath) {
        Id = id;
        Label = label;
        Description = description;
        Rules = rules;
        SeverityOverrides = severityOverrides;
        SourcePath = sourcePath;
    }

    /// <summary>
    /// Pack identifier.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Human-friendly label.
    /// </summary>
    public string Label { get; }
    /// <summary>
    /// Optional pack description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Rule IDs included in the pack.
    /// </summary>
    public IReadOnlyList<string> Rules { get; }
    /// <summary>
    /// Optional severity overrides applied by the pack.
    /// </summary>
    public IReadOnlyDictionary<string, string> SeverityOverrides { get; }
    /// <summary>
    /// Source file path for the pack definition.
    /// </summary>
    public string? SourcePath { get; }
}
