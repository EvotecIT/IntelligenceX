using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.ReviewerSetup;

internal static class ReviewerSetupToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["reviewer_setup_contract_verify"] = new[] {
                "verify reviewer setup contract fingerprints and detect drift between autodetect output and canonical pack metadata"
            }
        };
}
