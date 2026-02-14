namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateDuplicationFailsOnPerFileThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp");

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
        "maxFilePercent": 30,
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
            AssertEqual(2, exit, "analyze gate duplication per-file threshold blocks");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationPassesWhenWithinThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-pass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 20,
                duplicatedLines: 10,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-pass");

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
        "maxFilePercent": 30,
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
            AssertEqual(0, exit, "analyze gate duplication per-file threshold passes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationUsesPerFileConfiguredThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-file-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-file-threshold",
                findingPath: "src/test.js",
                configuredMaxPercent: 100,
                fileConfiguredMaxPercent: 30);

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

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate duplication uses per-file configured threshold");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationUnavailableCanPassWhenAllowed() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-unavailable-soft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
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
        "metricsPath": "artifacts/missing-duplication.json",
        "ruleIds": ["IXDUP001"],
        "failOnUnavailable": false
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            });
            AssertEqual(0, exit, "analyze gate duplication unavailable can pass when allowed");
            AssertContainsText(output, "duplication gate: unavailable", "analyze gate duplication unavailable output");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationUnavailableFailsWhenConfigured() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-unavailable-hard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "failOnUnavailable": false,
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/missing-duplication.json",
        "ruleIds": ["IXDUP001"],
        "failOnUnavailable": true
      }
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath
            });
            AssertEqual(2, exit, "analyze gate duplication unavailable fails when configured");
            AssertContainsText(output, "duplication gate: unavailable", "analyze gate duplication unavailable output");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationFailsOnOverallThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-overall-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-overall");

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
        "maxFilePercent": 100,
        "maxOverallPercent": 30,
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
            AssertEqual(2, exit, "analyze gate duplication overall threshold blocks");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationOverallNewOnlySuppressesBaselineFinding() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-overall-new-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-overall-new-only");

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": ".intelligencex/duplication-overall",
      "line": 0,
      "severity": "warning",
      "message": "legacy",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "IXDUP001:overall:20:50:8"
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
        "maxFilePercent": 100,
        "maxOverallPercent": 30,
        "newIssuesOnly": true,
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
            AssertEqual(0, exit, "analyze gate duplication overall new-only suppresses baseline finding");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationScopeChangedFilesIgnoresUnchangedFiles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-scope-ignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-scope-ignore",
                findingPath: "src/legacy.cs");
            File.WriteAllText(Path.Combine(temp, "artifacts", "changed-files.txt"), "src/changed.cs\n");

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
        "scope": "changed-files",
        "ruleIds": ["IXDUP001"],
        "maxFilePercent": 30,
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
                "--config", configPath,
                "--changed-files", Path.Combine(temp, "artifacts", "changed-files.txt")
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze gate duplication changed-files scope ignores untouched file");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationScopeChangedFilesBlocksChangedFiles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-scope-block-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-scope-block",
                findingPath: "src/changed.cs");
            File.WriteAllText(Path.Combine(temp, "artifacts", "changed-files.txt"), "src/changed.cs\n");

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
        "scope": "changed-files",
        "ruleIds": ["IXDUP001"],
        "maxFilePercent": 30,
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
                "--config", configPath,
                "--changed-files", Path.Combine(temp, "artifacts", "changed-files.txt")
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate duplication changed-files scope blocks changed file");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationNewOnlySuppressesBaselineFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-new-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            WriteEmptyFindings(Path.Combine(temp, "artifacts", "intelligencex.findings.json"));
            WriteDuplicationMetrics(
                Path.Combine(temp, "artifacts", "intelligencex.duplication.json"),
                duplicatedPercent: 40,
                duplicatedLines: 20,
                significantLines: 50,
                firstLine: 12,
                fingerprint: "ixdup-fp-baseline");

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 12,
      "severity": "warning",
      "message": "legacy",
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "fingerprint": "ixdup-fp-baseline"
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
        "maxFilePercent": 30,
        "newIssuesOnly": true,
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
            AssertEqual(0, exit, "analyze gate duplication new-only suppresses baseline finding");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDuplicationOverallDeltaBlocksWhenIncreaseExceedsAllowed() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-dup-delta-block-" + Guid.NewGuid().ToString("N"));
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
                fingerprint: "ixdup-fp-delta");

            // Baseline snapshot at 10% overall duplication.
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
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
        "maxOverallPercentIncrease": 2,
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
            AssertEqual(2, exit, "analyze gate duplication overall delta blocks");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

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

    private static void WriteEmptyFindings(string path) {
        File.WriteAllText(path, """
{
  "schema": "intelligencex.findings.v1",
  "items": []
}
""");
    }

    private static void WriteDuplicationMetrics(string metricsPath, double duplicatedPercent, int duplicatedLines,
        int significantLines, int firstLine, string fingerprint, string findingPath = "src/test.cs",
        double configuredMaxPercent = 25, double? fileConfiguredMaxPercent = null) {
        File.WriteAllText(metricsPath, $$"""
{
  "schema": "intelligencex.duplication.v1",
  "rules": [
    {
      "ruleId": "IXDUP001",
      "tool": "IntelligenceX.Maintainability",
      "windowLines": 8,
      "configuredMaxPercent": {{configuredMaxPercent}},
      "totalSignificantLines": {{significantLines}},
      "duplicatedSignificantLines": {{duplicatedLines}},
      "overallDuplicatedPercent": {{duplicatedPercent}},
      "files": [
        {
          "path": "{{findingPath}}",
          "configuredMaxPercent": {{(fileConfiguredMaxPercent.HasValue ? fileConfiguredMaxPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},
          "firstDuplicatedLine": {{firstLine}},
          "significantLines": {{significantLines}},
          "duplicatedLines": {{duplicatedLines}},
          "duplicatedPercent": {{duplicatedPercent}},
          "fingerprint": "{{fingerprint}}"
        }
      ]
    }
  ]
}
""");
    }
}
#endif
