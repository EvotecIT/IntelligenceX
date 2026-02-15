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

    private static void TestAnalyzeGateDuplicationFileDeltaNormalizesParentRelativePaths() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-delta-parentpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            var findingsPath = Path.Combine(temp, "artifacts", "intelligencex.findings.json");
            var metricsPath = Path.Combine(temp, "artifacts", "intelligencex.duplication.json");
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");

            WriteEmptyFindings(findingsPath);

            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-delta-parentpath-baseline",
                findingPath: "src/test.cs");

            var configWritePath = Path.Combine(temp, ".intelligencex", "reviewer-write.json");
            File.WriteAllText(configWritePath, """
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
        "maxFilePercent": 100,
        "maxOverallPercent": 100,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var writeExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configWritePath,
                "--write-baseline", baselinePath
            }).GetAwaiter().GetResult();
            AssertEqual(0, writeExit, "analyze gate write baseline for file delta parentpath test exits");

            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 12,
                duplicatedLines: 12,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-delta-parentpath-current",
                findingPath: "src/../src/test.cs");

            var configDeltaPath = Path.Combine(temp, ".intelligencex", "reviewer-delta.json");
            File.WriteAllText(configDeltaPath, """
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
        "maxFilePercentIncrease": 1,
        "maxFilePercent": 100,
        "maxOverallPercent": 100,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var deltaExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configDeltaPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, deltaExit, "analyze gate file delta normalizes ../ paths");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileDeltaNormalizesDoubleSlashes() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-delta-doubleslash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            var findingsPath = Path.Combine(temp, "artifacts", "intelligencex.findings.json");
            var metricsPath = Path.Combine(temp, "artifacts", "intelligencex.duplication.json");
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");

            WriteEmptyFindings(findingsPath);

            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-delta-doubleslash-baseline",
                findingPath: "src/test.cs");

            var configWritePath = Path.Combine(temp, ".intelligencex", "reviewer-write.json");
            File.WriteAllText(configWritePath, """
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
        "maxFilePercent": 100,
        "maxOverallPercent": 100,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var writeExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configWritePath,
                "--write-baseline", baselinePath
            }).GetAwaiter().GetResult();
            AssertEqual(0, writeExit, "analyze gate write baseline for file delta doubleslash test exits");

            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 12,
                duplicatedLines: 12,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-delta-doubleslash-current",
                findingPath: "src//test.cs");

            var configDeltaPath = Path.Combine(temp, ".intelligencex", "reviewer-delta.json");
            File.WriteAllText(configDeltaPath, """
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
        "maxFilePercentIncrease": 1,
        "maxFilePercent": 100,
        "maxOverallPercent": 100,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var deltaExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configDeltaPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, deltaExit, "analyze gate file delta normalizes // paths");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileDeltaWindowMismatchIsUnavailable() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-window-mismatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            var findingsPath = Path.Combine(temp, "artifacts", "intelligencex.findings.json");
            var metricsPath = Path.Combine(temp, "artifacts", "intelligencex.duplication.json");
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");

            WriteEmptyFindings(findingsPath);

            // Baseline is written with windowLines=8.
            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-window-baseline",
                findingPath: "src/test.cs",
                windowLines: 8);

            var configWritePath = Path.Combine(temp, ".intelligencex", "reviewer-write.json");
            File.WriteAllText(configWritePath, """
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
        "maxFilePercent": 100,
        "maxOverallPercent": 100,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var writeExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configWritePath,
                "--write-baseline", baselinePath
            }).GetAwaiter().GetResult();
            AssertEqual(0, writeExit, "analyze gate write baseline for file window mismatch test exits");

            // Current metrics use a different window size.
            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 10,
                duplicatedLines: 10,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-window-current",
                findingPath: "src/test.cs",
                windowLines: 9);

            var configDeltaPath = Path.Combine(temp, ".intelligencex", "reviewer-delta.json");
            File.WriteAllText(configDeltaPath, """
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
        "maxFilePercentIncrease": 1,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var deltaExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configDeltaPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, deltaExit, "analyze gate file delta window mismatch unavailable");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFileDeltaBaselineWindowMissingIsUnavailable() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-window-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            var findingsPath = Path.Combine(temp, "artifacts", "intelligencex.findings.json");
            var metricsPath = Path.Combine(temp, "artifacts", "intelligencex.duplication.json");
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");

            WriteEmptyFindings(findingsPath);

            // Legacy baseline fingerprint without windowLines token (WindowLines=0).
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
      "fingerprint": "IXDUP001:file:src/test.cs:10:100"
    }
  ]
}
""");

            // Current metrics include an explicit window size.
            WriteDuplicationMetrics(
                metricsPath,
                duplicatedPercent: 12,
                duplicatedLines: 12,
                significantLines: 100,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-window-current",
                findingPath: "src/test.cs",
                windowLines: 9);

            var configDeltaPath = Path.Combine(temp, ".intelligencex", "reviewer-delta.json");
            File.WriteAllText(configDeltaPath, """
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
        "maxFilePercentIncrease": 1,
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var deltaExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configDeltaPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, deltaExit, "analyze gate file delta baseline window missing unavailable");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif
