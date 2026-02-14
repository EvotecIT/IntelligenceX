namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateDuplicationOverallDeltaMissingBaselineIsUnavailable() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-delta-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 15,
                duplicatedLines: 15,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-delta-missing");

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": []
}
""");

            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true,
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/intelligencex.duplication.json",
        "ruleIds": ["IXDUP001"],
        "maxOverallPercentIncrease": 1,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate duplication overall delta missing baseline unavailable");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileDeltaBlocksWhenIncreaseExceedsAllowed() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 35,
                duplicatedLines: 35,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-delta",
                findingPath: "src/test.cs");

            // Baseline snapshot at 20% per-file duplication.
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": ".intelligencex/duplication-file",
      "line": 0,
      "severity": "info",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:file:src/test.cs:20:100:8"
    }
  ]
}
""");

            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true,
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/intelligencex.duplication.json",
        "ruleIds": ["IXDUP001"],
        "maxFilePercentIncrease": 5,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate duplication per-file delta blocks");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileBaselineLoadsPathsWithColon() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-baseline-colon-" + Guid.NewGuid().ToString("N"));
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
      "fingerprint": "IXDUP001:file:C:\\src\\test.cs:20:100:8:scope:changed-files"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication file baseline loads colon path ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication file baseline loads colon path error empty");
            AssertEqual(true, baselines.ContainsKey("IXDUP001|changed-files|C:/src/test.cs"),
                "duplication file baseline loads colon path key");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateWriteBaselineIncludesDuplicationFileSnapshotsWhenConfigured() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-baseline-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-baseline-files");

            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "failOnUnavailable": true,
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/intelligencex.duplication.json",
        "ruleIds": ["IXDUP001"],
        "maxFilePercentIncrease": 1,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath,
                "--write-baseline", baselinePath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze gate write baseline includes duplication file snapshots exit");
            AssertEqual(true, File.Exists(baselinePath), "analyze gate write baseline file snapshots baseline exists");
            var content = File.ReadAllText(baselinePath);
            AssertContainsText(content, ".intelligencex/duplication-file", "analyze gate write baseline includes duplication file snapshots");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "analyze gate write baseline file snapshots baseline loads");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "analyze gate write baseline file snapshots baseline load error empty");
            AssertEqual(true, baselines.Count > 0, "analyze gate write baseline file snapshots baseline has entries");

            var firstFingerprint = baselines.Values.First().Fingerprint ?? string.Empty;
            AssertEqual(true, firstFingerprint.Contains(":file-uri:", StringComparison.OrdinalIgnoreCase),
                "analyze gate write baseline file snapshots fingerprint uses encoded file-uri format");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateWriteBaselineIncludesDuplicationOverallSnapshot() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-baseline-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-baseline-snapshot");

            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "failOnUnavailable": true,
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/intelligencex.duplication.json",
        "ruleIds": ["IXDUP001"],
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath,
                "--write-baseline", baselinePath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze gate write baseline includes duplication snapshot exit");
            AssertEqual(true, File.Exists(baselinePath), "analyze gate write baseline snapshot baseline exists");
            var content = File.ReadAllText(baselinePath);
            AssertContainsText(content, ".intelligencex/duplication-overall", "analyze gate write baseline includes duplication overall snapshot");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileBaselineLoadsPathsContainingScopeSuffixTokens() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-baseline-scope-in-path-" + Guid.NewGuid().ToString("N"));
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
      "fingerprint": "IXDUP001:file-uri:%2Ftmp%2Fweird%3Ascope%3Achanged-files%2Ftest.cs:20:100:8:scope:changed-files"
    }
  ]
}
""");

            var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePath,
                out var baselines,
                out var error);
            AssertEqual(true, ok, "duplication file baseline loads scope tokens in path ok");
            AssertEqual(true, string.IsNullOrWhiteSpace(error), "duplication file baseline loads scope tokens in path error empty");
            AssertEqual(true, baselines.ContainsKey("IXDUP001|changed-files|/tmp/weird:scope:changed-files/test.cs"),
                "duplication file baseline loads scope tokens in path key");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileBaselineMissingPathReturnsNotProvided() {
        var ok = IntelligenceX.Cli.Analysis.AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
            path: "",
            baselines: out _,
            error: out var error);
        AssertEqual(false, ok, "duplication file baseline missing path fails");
        AssertContainsText(error ?? string.Empty, "baseline path not provided", "duplication file baseline missing path error");
    }
}
#endif
