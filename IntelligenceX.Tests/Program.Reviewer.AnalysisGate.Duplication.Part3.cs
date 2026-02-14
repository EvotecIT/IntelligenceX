namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateDuplicationOverallBaselineSkipsNullItems() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-overall-null-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var baselinePath = Path.Combine(temp, "analysis-baseline.json");
            File.WriteAllText(baselinePath, """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    null,
    {
      "path": ".intelligencex/duplication-overall",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:overall:10:100:8"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationOverallBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication overall baseline skips null items ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication overall baseline skips null items error empty");
            AssertEqual(true, baselines.ContainsKey("IXDUP001|all"), "duplication overall baseline skips null items key exists");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationOverallBaselineRejectsMalformedFingerprints() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-overall-malformed-fp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var baselinePath = Path.Combine(temp, "analysis-baseline.json");
            File.WriteAllText(baselinePath, """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": ".intelligencex/duplication-overall",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:overall:10:100:oops"
    },
    {
      "path": ".intelligencex/duplication-overall",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:overall:10:100:scope:oops"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationOverallBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication overall baseline rejects malformed fp ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication overall baseline rejects malformed fp error empty");
            AssertEqual(0, baselines.Count, "duplication overall baseline rejects malformed fp no entries");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif

