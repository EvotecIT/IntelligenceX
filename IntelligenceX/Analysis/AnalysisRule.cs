using System.Collections.Generic;

namespace IntelligenceX.Analysis;

/// <summary>
/// Describes a single analyzer rule and its metadata.
/// </summary>
public sealed class AnalysisRule {
    /// <summary>
    /// Creates a new rule description.
    /// </summary>
    public AnalysisRule(string id, string language, string tool, string toolRuleId, string title, string description,
        string category, string defaultSeverity, IReadOnlyList<string> tags, string? docs, string? sourcePath) {
        Id = id;
        Language = language;
        Tool = tool;
        ToolRuleId = toolRuleId;
        Title = title;
        Description = description;
        Category = category;
        DefaultSeverity = defaultSeverity;
        Tags = tags;
        Docs = docs;
        SourcePath = sourcePath;
    }

    /// <summary>
    /// Stable rule identifier.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Language identifier for the rule.
    /// </summary>
    public string Language { get; }
    /// <summary>
    /// Analyzer tool that owns the rule.
    /// </summary>
    public string Tool { get; }
    /// <summary>
    /// Tool-specific rule identifier.
    /// </summary>
    public string ToolRuleId { get; }
    /// <summary>
    /// Short rule title.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Rule description shown in UI.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Rule category label.
    /// </summary>
    public string Category { get; }
    /// <summary>
    /// Default severity assigned by the pack.
    /// </summary>
    public string DefaultSeverity { get; }
    /// <summary>
    /// Optional rule tags for grouping.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }
    /// <summary>
    /// Optional documentation link.
    /// </summary>
    public string? Docs { get; }
    /// <summary>
    /// Source file path for the rule definition.
    /// </summary>
    public string? SourcePath { get; }
}
