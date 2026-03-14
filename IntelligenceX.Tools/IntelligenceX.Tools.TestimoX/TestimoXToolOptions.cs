using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Runtime options for IX.TestimoX tools.
/// </summary>
public sealed class TestimoXToolOptions : IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "testimox"
    };

    /// <summary>
    /// Enables or disables the entire TestimoX pack.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum page size accepted by catalog queries when callers request paged output.
    /// </summary>
    public int MaxRulesInCatalog { get; set; } = 2000;

    /// <summary>
    /// Maximum number of rules accepted per run request.
    /// </summary>
    public int MaxRulesPerRun { get; set; } = 100;

    /// <summary>
    /// Maximum page size accepted by read-only history/report catalog queries.
    /// </summary>
    public int MaxHistoryRowsInCatalog { get; set; } = 500;

    /// <summary>
    /// Maximum number of characters returned for report snapshot content.
    /// </summary>
    public int MaxSnapshotContentChars { get; set; } = 16000;

    /// <summary>
    /// Default execution concurrency for testimox_rules_run.
    /// </summary>
    public int DefaultConcurrency { get; set; } = 4;

    /// <summary>
    /// Maximum execution concurrency accepted by testimox_rules_run.
    /// </summary>
    public int MaxConcurrency { get; set; } = 16;

    /// <summary>
    /// Default include-superseded behavior for testimox_rules_run.
    /// </summary>
    public bool DefaultIncludeSupersededRules { get; set; }

    /// <summary>
    /// Maximum number of raw result rows included per rule when include_rule_results=true.
    /// </summary>
    public int MaxResultRowsPerRule { get; set; } = 200;

    /// <summary>
    /// Allowed roots for read-only TestimoX result-store inspection tools.
    /// </summary>
    public List<string> AllowedStoreRoots { get; } = new();

    /// <summary>
    /// Allowed roots for read-only monitoring history/report inspection tools.
    /// </summary>
    public List<string> AllowedHistoryRoots { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        if (MaxRulesInCatalog <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxRulesInCatalog), "MaxRulesInCatalog must be greater than 0.");
        }
        if (MaxRulesPerRun <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxRulesPerRun), "MaxRulesPerRun must be greater than 0.");
        }
        if (MaxHistoryRowsInCatalog <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxHistoryRowsInCatalog), "MaxHistoryRowsInCatalog must be greater than 0.");
        }
        if (MaxSnapshotContentChars <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxSnapshotContentChars), "MaxSnapshotContentChars must be greater than 0.");
        }
        if (DefaultConcurrency <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultConcurrency), "DefaultConcurrency must be greater than 0.");
        }
        if (MaxConcurrency <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "MaxConcurrency must be greater than 0.");
        }
        if (DefaultConcurrency > MaxConcurrency) {
            throw new ArgumentOutOfRangeException(nameof(DefaultConcurrency), "DefaultConcurrency cannot exceed MaxConcurrency.");
        }
        if (MaxResultRowsPerRule <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResultRowsPerRule), "MaxResultRowsPerRule must be greater than 0.");
        }
    }
}
