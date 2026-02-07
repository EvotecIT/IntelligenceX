using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Analysis;

/// <summary>
/// Validation outcome for analysis rule and pack catalogs.
/// </summary>
public sealed class AnalysisCatalogValidationResult {
    /// <summary>
    /// Creates a validation result.
    /// </summary>
    public AnalysisCatalogValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings) {
        Errors = errors ?? new List<string>();
        Warnings = warnings ?? new List<string>();
    }

    /// <summary>
    /// True when no validation errors were found.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Validation errors that should block catalog usage.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Validation warnings that should be reviewed but do not block by themselves.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Total number of discovered issues (errors + warnings).
    /// </summary>
    public int IssueCount => Errors.Count + Warnings.Count;

    /// <summary>
    /// Builds a short one-line summary string.
    /// </summary>
    public string BuildSummary() {
        var status = IsValid ? "pass" : "fail";
        return $"Catalog validation: {status} ({Errors.Count} error(s), {Warnings.Count} warning(s))";
    }

    /// <summary>
    /// Creates a normalized result with deterministic ordering.
    /// </summary>
    public AnalysisCatalogValidationResult Normalize() {
        var errors = Errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(error => error, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        var warnings = Warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(warning => warning, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AnalysisCatalogValidationResult(errors, warnings);
    }
}
