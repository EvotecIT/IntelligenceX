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
