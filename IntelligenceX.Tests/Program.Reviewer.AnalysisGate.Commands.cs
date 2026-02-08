namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeGateHelpToken() {
        var (exit, output) = RunAnalyzeAndCaptureOutput(new[] { "gate", "help" });
        AssertEqual(0, exit, "analyze gate help exit");
        AssertContainsText(output, "intelligencex analyze gate", "analyze gate help usage");
    }

    private static void TestAnalyzeGateRejectsConfigOutsideWorkspace() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-config-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var outsideRoot = temp + "2";
        Directory.CreateDirectory(outsideRoot);
        try {
            var outsideConfig = Path.Combine(outsideRoot, "reviewer.json");
            File.WriteAllText(outsideConfig, "{ }\n");

            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                outsideConfig
            }).GetAwaiter().GetResult();
            AssertEqual(1, exit, "analyze gate exit (config outside workspace rejected)");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
            if (Directory.Exists(outsideRoot)) {
                Directory.Delete(outsideRoot, true);
            }
        }
    }

    private static void TestAnalysisFindingsLoaderRejectsRelativeWorkspaceOverride() {
        AssertThrows<ArgumentException>(() =>
            IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(new ReviewSettings(),
                Array.Empty<PullRequestFile>(),
                "relative/path"),
            "analysis loader rejects relative workspace override");
    }

    private static void TestAnalysisFindingsLoaderIgnoresInputsOutsideWorkspace() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-input-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var outsideRoot = temp + "2";
        Directory.CreateDirectory(outsideRoot);
        try {
            var outsideInput = Path.Combine(outsideRoot, "intelligencex.findings.json");
            File.WriteAllText(outsideInput, """
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

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.MinSeverity = "info";
            settings.Analysis.Results.Inputs = new[] { outsideInput };

            var load = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(
                settings,
                Array.Empty<PullRequestFile>(),
                temp);
            AssertEqual(0, load.Report.ResolvedInputFiles, "analysis loader resolves 0 inputs (outside ignored)");
            AssertEqual(0, load.Findings.Count, "analysis loader findings empty (outside ignored)");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
            if (Directory.Exists(outsideRoot)) {
                Directory.Delete(outsideRoot, true);
            }
        }
    }

    private static void TestAnalysisFindingsLoaderWorkspaceBoundRejectsSiblingPrefix() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-prefix-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var sibling = temp + "2";
        Directory.CreateDirectory(sibling);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Reviewer.AnalysisFindingsLoader).GetMethod("ResolveWorkspaceBoundAbsolutePath", flags);
            AssertNotNull(method, "analysis loader resolve bound method exists");

            var candidate = Path.Combine(sibling, "artifact.sarif");
            var result = (string?)method!.Invoke(null, new object[] { temp, candidate });
            AssertEqual(null, result, "analysis loader rejects sibling prefix path");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
            if (Directory.Exists(sibling)) {
                Directory.Delete(sibling, true);
            }
        }
    }

    private static void TestAnalyzeGateResolveWorkspaceBoundPathAcceptsWorkspaceRoot() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-workspace-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Cli.Analysis.AnalyzeGateCommand).GetMethod("ResolveWorkspaceBoundPath", flags);
            AssertNotNull(method, "resolve workspace bound path method exists");

            var result = (string?)method!.Invoke(null, new object[] { temp, temp });
            AssertEqual(Path.GetFullPath(temp), result, "resolve workspace bound path accepts workspace root");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateDisabledSkips() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{ "items": [] }
""");
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": { "enabled": false },
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
            AssertEqual(0, exit, "analyze gate exit (disabled)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateFailsOnViolations() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
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
            File.WriteAllText(Path.Combine(artifactsDir, "changed-files.txt"), "src/test.cs\n");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
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
      "failOnUnavailable": true,
      "failOnNoEnabledRules": true
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
                configPath,
                "--changed-files",
                Path.Combine(artifactsDir, "changed-files.txt")
            }).GetAwaiter().GetResult();
            AssertEqual(2, exit, "analyze gate exit (violations)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGatePassesOnClean() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-pass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{ "items": [] }
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": { "enabled": true, "minSeverity": "warning" },
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
            AssertEqual(0, exit, "analyze gate exit (clean)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateFailsOnNoEnabledRules() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-no-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(artifactsDir);

            // Catalog exists, but packs are not configured -> no enabled rules.
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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{ "items": [] }
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": [],
    "gate": { "enabled": true, "failOnNoEnabledRules": true },
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
            AssertEqual(2, exit, "analyze gate exit (no enabled rules)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateFailOnUnavailableHandlesLoaderException() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-unavailable-throws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

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

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");

            var tempNormalized = temp.Replace("\\", "/");
            var badInputPattern = tempNormalized + "/artifacts/**/\\u0000*.sarif";
            File.WriteAllText(configPath, $$"""
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": {
      "enabled": true,
      "failOnUnavailable": true,
      "failOnNoEnabledRules": true
    },
    "results": { "inputs": ["{{badInputPattern}}"] }
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
            AssertEqual(2, exit, "analyze gate exit (loader exception -> unavailable)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeGateMinSeverityFilters() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-min-sev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "info",
      "message": "Low severity.",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "gate": { "enabled": true, "minSeverity": "warning", "types": ["bug"] },
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
            AssertEqual(0, exit, "analyze gate exit (minSeverity filters info)");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

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
}
#endif
