namespace IntelligenceX.Tests;

internal static partial class Program {
    #if INTELLIGENCEX_REVIEWER
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
    #endif
}
