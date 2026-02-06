using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

/// <summary>
/// Aggregated load counters for configured analysis result inputs.
/// </summary>
/// <param name="ConfiguredInputs">Number of configured input patterns.</param>
/// <param name="ResolvedInputFiles">Number of unique files matched by configured input patterns.</param>
/// <param name="ParsedInputFiles">Number of non-empty matched files successfully deserialized as SARIF/findings payloads, including valid payloads that produce zero findings.</param>
/// <param name="FailedInputFiles">Number of matched files that failed load/parse.</param>
internal sealed record AnalysisLoadReport(
    int ConfiguredInputs,
    int ResolvedInputFiles,
    int ParsedInputFiles,
    int FailedInputFiles
);

/// <summary>
/// Loaded analysis findings and related load counters.
/// </summary>
/// <param name="Findings">Normalized analysis findings that passed filtering.</param>
/// <param name="Report">Load counters for configured input patterns and matched files.</param>
internal sealed record AnalysisLoadResult(
    IReadOnlyList<AnalysisFinding> Findings,
    AnalysisLoadReport Report
);
