namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateNewIssuesOnlySuppressesBaselineFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(0, exit, "analyze gate baseline suppresses existing findings");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlyFailsForNewFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken (new).",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken (old).",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(2, exit, "analyze gate baseline fails on new findings");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlyMissingSchemaLogsInference() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-schema-infer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(0, exit, "analyze gate baseline missing schema suppresses finding");
            AssertContainsText(output, "schema inferred as 'intelligencex.findings.v1'", "analyze gate baseline schema inference note");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlyMissingBaselineIsUnavailable() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(2, exit, "analyze gate baseline missing returns unavailable");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlyMissingBaselineCanPassWhenUnavailableAllowed() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-missing-soft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": false
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
            AssertEqual(0, exit, "analyze gate baseline missing can pass when unavailable allowed");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlySuppressesLegacyBaselineKeyPathNormalization() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-legacy-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "key": "IX001|SRC\\TEST.CS|10|IntelligenceX|msg:Broken."
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(0, exit, "analyze gate suppresses legacy baseline key with normalized path");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateNewIssuesOnlySuppressesLegacyBaselineKeyDotRelativePrefix() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-legacy-key-dotrel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "analysis-baseline.json"), """
{
  "schema": "intelligencex.analysis-baseline.v1",
  "items": [
    {
      "key": "IX001|.//SRC\\TEST.CS|10|IntelligenceX|msg:Broken."
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
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
            AssertEqual(0, exit, "analyze gate suppresses dot-relative legacy baseline key with normalized path");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateWriteBaselineCreatesContractSchema() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-baseline-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            SeedMinimalGateCatalog(temp);
            Directory.CreateDirectory(Path.Combine(temp, "artifacts"));
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, "artifacts", "intelligencex.findings.json"), """
{
  "schema": "intelligencex.findings.v1",
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Broken.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
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
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnUnavailable": true
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var baselinePath = Path.Combine(temp, ".intelligencex", "analysis-baseline.json");
            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace", temp,
                "--config", configPath,
                "--write-baseline", baselinePath
            });
            AssertEqual(0, exit, "analyze gate write baseline exit");
            AssertContainsText(output, "baseline updated", "analyze gate write baseline output");
            AssertEqual(true, File.Exists(baselinePath), "baseline file exists");

            var root = JsonLite.Parse(File.ReadAllText(baselinePath))?.AsObject();
            AssertNotNull(root, "baseline root json");
            AssertEqual("intelligencex.analysis-baseline.v1", root!.GetString("schema") ?? string.Empty, "baseline schema");
            var items = root.GetArray("items");
            AssertNotNull(items, "baseline items");
            AssertEqual(true, items!.Count >= 1, "baseline items count");
            var first = items[0]?.AsObject();
            AssertNotNull(first, "baseline first item");
            AssertEqual(true, !string.IsNullOrWhiteSpace(first!.GetString("key")), "baseline item key");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestReviewerSchemaIncludesAnalysisGateBaselineProperties() {
        var schemaPath = Path.Combine(ResolveWorkspaceRoot(), "Schemas", "reviewer.schema.json");
        AssertEqual(true, File.Exists(schemaPath), "reviewer schema exists");
        var root = JsonLite.Parse(File.ReadAllText(schemaPath))?.AsObject();
        AssertNotNull(root, "reviewer schema root");

        var analysis = root!.GetObject("properties")?.GetObject("analysis");
        AssertNotNull(analysis, "reviewer schema analysis node");
        var gate = analysis!.GetObject("properties")?.GetObject("gate");
        AssertNotNull(gate, "reviewer schema analysis.gate node");
        var gateProps = gate!.GetObject("properties");
        AssertNotNull(gateProps, "reviewer schema analysis.gate.properties");

        var newIssuesOnly = gateProps!.GetObject("newIssuesOnly");
        AssertNotNull(newIssuesOnly, "reviewer schema newIssuesOnly property");
        AssertEqual("boolean", newIssuesOnly!.GetString("type") ?? string.Empty, "reviewer schema newIssuesOnly type");

        var baselinePath = gateProps.GetObject("baselinePath");
        AssertNotNull(baselinePath, "reviewer schema baselinePath property");
        AssertEqual("string", baselinePath!.GetString("type") ?? string.Empty, "reviewer schema baselinePath type");
    }

    private static void SeedMinimalGateCatalog(string temp) {
        var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
        var packsDir = Path.Combine(temp, "Analysis", "Packs");
        Directory.CreateDirectory(rulesDir);
        Directory.CreateDirectory(packsDir);

        File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IX001",
  "type": "bug",
  "title": "Test rule",
  "description": "Test rule.",
  "category": "Reliability",
  "defaultSeverity": "warning"
}
""");
        File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IX001"]
}
""");
    }
}
#endif
