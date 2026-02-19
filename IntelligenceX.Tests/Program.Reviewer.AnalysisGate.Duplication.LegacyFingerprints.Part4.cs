namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateDuplicationFileBaselineLoadsLegacyFileFingerprintWithoutWindowLines() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-baseline-legacy-file-nowin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var baselinePath = Path.Combine(temp, "analysis-baseline.json");
            File.WriteAllText(baselinePath, """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": ".intelligencex/duplication-file",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:file:src/test.cs:20:100"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication file baseline loads legacy file fingerprint ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication file baseline loads legacy file fingerprint error empty");
            AssertEqual(true, baselines.ContainsKey("IXDUP001|all|src/test.cs"), "duplication file baseline loads legacy file fingerprint key");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateDuplicationFileBaselineLoadsLegacyFileUriFingerprintWithoutWindowLines() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-baseline-legacy-file-uri-nowin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var baselinePath = Path.Combine(temp, "analysis-baseline.json");
            File.WriteAllText(baselinePath, """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": ".intelligencex/duplication-file",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:file-uri:src%2Ftest.cs:20:100"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication file baseline loads legacy file-uri fingerprint ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication file baseline loads legacy file-uri fingerprint error empty");
            AssertEqual(true, baselines.ContainsKey("IXDUP001|all|src/test.cs"), "duplication file baseline loads legacy file-uri fingerprint key");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif
