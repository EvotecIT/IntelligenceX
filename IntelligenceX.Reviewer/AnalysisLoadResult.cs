using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed record AnalysisLoadReport(
    int ConfiguredInputs,
    int ResolvedInputFiles,
    int ParsedInputFiles,
    int FailedInputFiles
);

internal sealed record AnalysisLoadResult(
    IReadOnlyList<AnalysisFinding> Findings,
    AnalysisLoadReport Report
);
