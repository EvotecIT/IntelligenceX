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
            DeleteDirectoryIfExistsWithRetries(temp);
            DeleteDirectoryIfExistsWithRetries(outsideRoot);
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
            DeleteDirectoryIfExistsWithRetries(temp);
            DeleteDirectoryIfExistsWithRetries(outsideRoot);
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
            DeleteDirectoryIfExistsWithRetries(temp);
            DeleteDirectoryIfExistsWithRetries(sibling);
        }
    }

    private static void TestAnalysisFindingsLoaderDoesNotRelativizeSiblingPrefixAbsoluteFindingPath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-path-normalization-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var sibling = temp + "2";
        Directory.CreateDirectory(sibling);
        try {
            var siblingFile = Path.Combine(sibling, "src", "test.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(siblingFile)!);
            File.WriteAllText(siblingFile, "// outside workspace");

            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            var escapedSiblingPath = siblingFile.Replace("\\", "\\\\");
            var findingsPath = Path.Combine(artifactsDir, "intelligencex.findings.json");
            File.WriteAllText(findingsPath, $$"""
{
  "items": [
    {
      "path": "{{escapedSiblingPath}}",
      "line": 5,
      "severity": "warning",
      "message": "Outside path finding",
      "ruleId": "IX001",
      "tool": "IntelligenceX"
    }
  ]
}
""");

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.MinSeverity = "info";
            settings.Analysis.Results.Inputs = new[] { "artifacts/intelligencex.findings.json" };

            var load = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(
                settings,
                Array.Empty<PullRequestFile>(),
                temp);

            AssertEqual(1, load.Findings.Count, "analysis loader finds outside absolute path item");
            var expected = Path.GetFullPath(siblingFile).Replace('\\', '/');
            AssertEqual(expected, load.Findings[0].Path, "analysis loader keeps outside path absolute");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
            DeleteDirectoryIfExistsWithRetries(sibling);
        }
    }

    private static void TestAnalysisFindingsLoaderNormalizePathCaseSensitivityByPlatform() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-normalize-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Reviewer.AnalysisFindingsLoader).GetMethod("NormalizePath", flags);
            AssertNotNull(method, "analysis loader normalize path method exists");

            var workspace = Path.GetFullPath(temp);
            var pathInWorkspace = Path.Combine(workspace, "src", "test.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(pathInWorkspace)!);
            File.WriteAllText(pathInWorkspace, "// test");

            var workspaceCaseVariant = TogglePathCase(workspace);
            if (string.Equals(workspaceCaseVariant, workspace, StringComparison.Ordinal)) {
                AssertEqual(true, true, "analysis loader normalize path case setup");
                return;
            }

            var normalized = (string)method!.Invoke(null, new object[] { pathInWorkspace, workspaceCaseVariant })!;
            var expectedCaseInsensitive = Path.DirectorySeparatorChar == '\\';
            var expectedRelative = Path.Combine("src", "test.cs").Replace('\\', '/');
            var expectedAbsolute = Path.GetFullPath(pathInWorkspace).Replace('\\', '/');

            AssertEqual(expectedCaseInsensitive ? expectedRelative : expectedAbsolute,
                normalized,
                "analysis loader normalize path follows platform case semantics");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisFindingsLoaderNormalizePathAcceptsMixedSeparatorsWithinWorkspace() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-normalize-separators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            if (Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar) {
                AssertEqual(true, true, "analysis loader normalize mixed separators setup");
                return;
            }

            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Reviewer.AnalysisFindingsLoader).GetMethod("NormalizePath", flags);
            AssertNotNull(method, "analysis loader normalize path method exists (mixed separators)");

            var workspace = Path.GetFullPath(temp);
            var pathInWorkspace = Path.Combine(workspace, "src", "test.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(pathInWorkspace)!);
            File.WriteAllText(pathInWorkspace, "// test");

            var mixedWorkspace = workspace.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var mixedPath = pathInWorkspace.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var normalizedMixedWorkspace = (string)method!.Invoke(null, new object[] { pathInWorkspace, mixedWorkspace })!;
            var normalizedMixedPath = (string)method.Invoke(null, new object[] { mixedPath, workspace })!;
            var expectedRelative = Path.Combine("src", "test.cs").Replace('\\', '/');

            AssertEqual(expectedRelative,
                normalizedMixedWorkspace,
                "analysis loader normalize path accepts mixed-separator workspace");
            AssertEqual(expectedRelative,
                normalizedMixedPath,
                "analysis loader normalize path accepts mixed-separator candidate");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateRuleIdsFilterCanNarrowScopeWithoutTypes() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-ruleids-only-" + Guid.NewGuid().ToString("N"));
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
  "title": "Rule one",
  "description": "Rule one.",
  "category": "Reliability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX002.json"), """
{
  "id": "IX002",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IX002",
  "type": "code-smell",
  "title": "Rule two",
  "description": "Rule two.",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IX001", "IX002"]
}
""");
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Rule one finding.",
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
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "ruleIds": ["IX002"]
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                configPath
            });
            AssertEqual(0, exit, "analyze gate exit (ruleIds-only filter narrows scope)");
            AssertContainsText(output, "Gate ruleIds filter: IX002", "analyze gate ruleIds-only filter summary");
            AssertContainsText(output, "- Violations: 0", "analyze gate ruleIds-only filter violations");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateRuleIdsFilterAddsToTypeFiltering() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-ruleids-additive-" + Guid.NewGuid().ToString("N"));
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
  "title": "Rule one",
  "description": "Rule one.",
  "category": "Reliability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX002.json"), """
{
  "id": "IX002",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IX002",
  "type": "code-smell",
  "title": "Rule two",
  "description": "Rule two.",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IX001", "IX002"]
}
""");
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Rule two finding.",
      "ruleId": "IX002",
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
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "types": ["bug"],
      "ruleIds": ["IX002"]
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                configPath
            });
            AssertEqual(2, exit, "analyze gate exit (ruleIds add to type filter)");
            AssertContainsText(output, "Gate type filter: bug", "analyze gate additive type filter summary");
            AssertContainsText(output, "Gate ruleIds filter: IX002", "analyze gate additive ruleIds filter summary");
            AssertContainsText(output, "IX002", "analyze gate additive ruleIds violation output");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateFiltersNormalizeWhitespaceAndCase() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-gate-filters-normalization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(artifactsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX002.json"), """
{
  "id": "IX002",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IX002",
  "type": "  Code-Smell  ",
  "title": "Rule two",
  "description": "Rule two.",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IX002"]
}
""");
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "warning",
      "message": "Rule two finding.",
      "ruleId": "IX002",
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
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "types": ["  CODE-SMELL  "],
      "ruleIds": ["  ix002  "]
    },
    "results": { "inputs": ["artifacts/intelligencex.findings.json"] }
  }
}
""");

            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "gate",
                "--workspace",
                temp,
                "--config",
                configPath
            });
            AssertEqual(2, exit, "analyze gate exit (normalized filters still match)");
            AssertContainsText(output, "Gate type filter: code-smell", "analyze gate normalized type filter summary");
            AssertContainsText(output, "Gate ruleIds filter: ix002", "analyze gate normalized ruleIds filter summary");
            AssertContainsText(output, "IX002", "analyze gate normalized filters violation output");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeGateFiltersMissingTypeDoesNotMatchTypeOnlyFilter() {
        var nestedFlags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
        var gateType = typeof(IntelligenceX.Cli.Analysis.AnalyzeGateCommand);
        var filterType = gateType.GetNestedType("GateFindingFilters", nestedFlags);
        AssertEqual(true, filterType is not null, "gate matcher nested type exists");

        var constructorFlags = global::System.Reflection.BindingFlags.Public |
                               global::System.Reflection.BindingFlags.NonPublic |
                               global::System.Reflection.BindingFlags.Instance;
        global::System.Reflection.ConstructorInfo? constructor = null;
        foreach (var candidate in filterType!.GetConstructors(constructorFlags)) {
            if (candidate.GetParameters().Length == 5) {
                constructor = candidate;
                break;
            }
        }
        AssertEqual(true, constructor is not null, "gate matcher constructor exists");

        var matchesMethod =
            filterType.GetMethod("Matches", global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance);
        AssertEqual(true, matchesMethod is not null, "gate matcher method exists");

        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bug" };
        var noRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onlyTypeFilter = constructor!.Invoke(new object[] {
            allowedTypes,
            noRuleIds,
            true,
            false,
            false
        });

        var missingTypeWithoutRuleIdFilter = (bool)matchesMethod!.Invoke(onlyTypeFilter, new object?[] { "IX001", null })!;
        AssertEqual(false, missingTypeWithoutRuleIdFilter,
            "gate matcher does not include missing type when only type filter is configured");

        var unknownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "unknown" };
        var unknownTypeFilter = constructor.Invoke(new object[] {
            unknownTypes,
            noRuleIds,
            true,
            false,
            false
        });
        var missingTypeMatchesUnknownTypeFilter = (bool)matchesMethod.Invoke(unknownTypeFilter, new object?[] { "IX001", null })!;
        AssertEqual(true, missingTypeMatchesUnknownTypeFilter,
            "gate matcher evaluates missing type as unknown when type filter includes unknown");

        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IXMATCH" };
        var typeAndRuleFilter = constructor.Invoke(new object[] {
            allowedTypes,
            ruleIds,
            true,
            true,
            false
        });
        var missingTypeWithNonMatchingRuleId = (bool)matchesMethod.Invoke(typeAndRuleFilter, new object?[] { "IXOTHER", null })!;
        var missingTypeWithMatchingRuleId = (bool)matchesMethod.Invoke(typeAndRuleFilter, new object?[] { "IXMATCH", null })!;

        AssertEqual(false, missingTypeWithNonMatchingRuleId,
            "gate matcher does not include missing type when ruleId filter exists and ruleId does not match");
        AssertEqual(true, missingTypeWithMatchingRuleId,
            "gate matcher includes missing type when ruleId filter exists and ruleId matches");
    }

}
#endif
