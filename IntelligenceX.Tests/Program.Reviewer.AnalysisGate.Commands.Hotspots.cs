namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateHotspotsMinSeverityFilters() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-hotspots-min-sev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Security hotspot",
  "description": "Requires review.",
  "category": "Security",
  "defaultSeverity": "info"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");

            var finding = new AnalysisFinding("src/test.cs", 10, "Hotspot finding.", "info", "IXHOT001", "IntelligenceX", "fp-xyz");
            var key = AnalysisHotspots.ComputeHotspotKey(finding);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "hotspots.json"), $$"""
{
  "schema": "{{HotspotStateStore.SchemaValue}}",
  "items": [
    { "key": "{{key}}", "status": "to-review" }
  ]
}
""");

            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "info",
      "message": "Hotspot finding.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-xyz"
    }
  ]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "types": ["security-hotspot"],
      "failOnHotspotsToReview": true
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                configPath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze gate exit (hotspots minSeverity filters info)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateHotspotsHonorRuleIdFiltersWithBaselineSuppression() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-hotspots-ruleid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Security hotspot",
  "description": "Requires review.",
  "category": "Security",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");

            var finding = new AnalysisFinding("src/test.cs", 10, "Hotspot finding.", "warning", "IXHOT001", "IntelligenceX", "fp-xyz");
            var key = AnalysisHotspots.ComputeHotspotKey(finding);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            File.WriteAllText(Path.Combine(temp, ".intelligencex", "hotspots.json"), $$"""
{
  "schema": "{{HotspotStateStore.SchemaValue}}",
  "items": [
    { "key": "{{key}}", "status": "to-review" }
  ]
}
""");

            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Hotspot finding.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-xyz"
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
      "message": "Hotspot finding.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-xyz"
    }
  ]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "types": ["bug"],
      "ruleIds": ["IXHOT001"],
      "newIssuesOnly": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "failOnHotspotsToReview": true
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                configPath
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate exit (hotspots honor ruleIds even when type filter excludes)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif
