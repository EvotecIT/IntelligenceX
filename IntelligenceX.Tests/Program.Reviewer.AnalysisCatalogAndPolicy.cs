namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisSeverityCritical() {
        AssertEqual("error", AnalysisSeverity.Normalize("critical"), "severity critical normalize");
        AssertEqual(3, AnalysisSeverity.Rank("critical"), "severity critical rank");
        AssertEqual("warning", AnalysisSeverity.Normalize("medium"), "severity medium normalize");
    }

    private static void TestAnalysisConfigExportToolIds() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
                ["IX001"] = new AnalysisRule(
                    "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                    "Reliability", "warning", Array.Empty<string>(), null, null),
                ["IX002"] = new AnalysisRule(
                    "IX002", "cs", "roslyn", "CA1062", "Validate arguments", "Validate argument null checks",
                    "Reliability", "warning", Array.Empty<string>(), null, null),
                ["PS001"] = new AnalysisRule(
                    "PS001", "powershell", "psscriptanalyzer", "PSAvoidUsingWriteHost",
                    "Avoid Write-Host", "Use Write-Output instead", "BestPractices", "suggestion",
                    Array.Empty<string>(), null, null),
                ["PS002"] = new AnalysisRule(
                    "PS002", "ps", "psscriptanalyzer", "PSUseSupportsShouldProcess",
                    "Use SupportsShouldProcess", "Add SupportsShouldProcess when needed", "BestPractices", "warning",
                    Array.Empty<string>(), null, null)
            };
            var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
                ["pack"] = new AnalysisPack(
                    "pack", "Pack", "Test pack", new[] { "IX001", "IX002", "PS001", "PS002" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null)
            };
            var catalog = new AnalysisCatalog(rules, packs);
            var settings = new AnalysisSettings { Packs = new[] { "pack" } };

            AnalysisConfigExporter.Export(settings, catalog, temp);

            var editorConfig = File.ReadAllText(Path.Combine(temp, ".editorconfig"));
            AssertEqual(true, editorConfig.Contains("dotnet_diagnostic.CA2000.severity", StringComparison.Ordinal),
                "editorconfig CA2000");
            AssertEqual(true, editorConfig.Contains("dotnet_diagnostic.CA1062.severity", StringComparison.Ordinal),
                "editorconfig CA1062");

            var psConfig = File.ReadAllText(Path.Combine(temp, "PSScriptAnalyzerSettings.psd1"));
            AssertEqual(true, psConfig.Contains("PSAvoidUsingWriteHost", StringComparison.Ordinal),
                "psconfig PSAvoidUsingWriteHost");
            AssertEqual(true, psConfig.Contains("PSAvoidUsingWriteHost = @{ Severity = 'Information' }", StringComparison.Ordinal),
                "psconfig suggestion maps to information");
            AssertEqual(true, psConfig.Contains("PSUseSupportsShouldProcess", StringComparison.Ordinal),
                "psconfig PSUseSupportsShouldProcess");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyResolvesOverrides() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null),
            ["IX002"] = new AnalysisRule(
                "IX002", "powershell", "psscriptanalyzer", "PSAvoidUsingWriteHost", "Avoid Write-Host",
                "Use Write-Output", "BestPractices", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["default"] = new AnalysisPack(
                "default", "Default", "Test pack", new[] { "IX001", "IX002" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["IX001"] = "error"
                }, null)
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "default" },
            SeverityOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["CA2000"] = "warning",
                ["IX002"] = "error"
            }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(0, policy.Warnings.Count, "policy warnings");
        AssertEqual(2, policy.Rules.Count, "policy selected count");
        AssertEqual("warning", policy.Rules["IX001"].Severity, "policy override tool rule id");
        AssertEqual("error", policy.Rules["IX002"].Severity, "policy override catalog id");
    }

    private static void TestAnalysisPolicyResolvesIncludedPacks() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null),
            ["IX002"] = new AnalysisRule(
                "IX002", "csharp", "roslyn", "CA1062", "Validate arguments", "Validate public arguments",
                "Reliability", "warning", Array.Empty<string>(), null, null),
            ["IX003"] = new AnalysisRule(
                "IX003", "csharp", "roslyn", "SA1600", "Document elements", "Document public symbols",
                "Style", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["core"] = new AnalysisPack(
                "core", "Core", "Core rules", new[] { "IX001" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null),
            ["standard"] = new AnalysisPack(
                "standard", "Standard", "Standard rules", new[] { "IX002" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["IX001"] = "error"
                }, null, includes: new[] { "core" }),
            ["strict"] = new AnalysisPack(
                "strict", "Strict", "Strict rules", new[] { "IX003" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["IX002"] = "error"
                }, null, includes: new[] { "standard" })
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "strict" }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(0, policy.Warnings.Count, "policy include warnings");
        AssertEqual(3, policy.Rules.Count, "policy include selected count");
        AssertEqual("error", policy.Rules["IX001"].Severity, "policy include inherited override");
        AssertEqual("error", policy.Rules["IX002"].Severity, "policy include parent override");
        AssertEqual("warning", policy.Rules["IX003"].Severity, "policy include direct rule");
    }

    private static void TestAnalysisPolicyIncludedPackCycleWarning() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null),
            ["IX002"] = new AnalysisRule(
                "IX002", "csharp", "roslyn", "CA1062", "Validate arguments", "Validate public arguments",
                "Reliability", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["pack-a"] = new AnalysisPack(
                "pack-a", "Pack A", "A rules", new[] { "IX001" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, includes: new[] { "pack-b" }),
            ["pack-b"] = new AnalysisPack(
                "pack-b", "Pack B", "B rules", new[] { "IX002" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, includes: new[] { "pack-a" })
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "pack-a" }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(true, policy.Rules.ContainsKey("IX001"), "policy cycle includes pack-a rules");
        AssertEqual(true, policy.Rules.ContainsKey("IX002"), "policy cycle includes pack-b rules");
        AssertEqual(true, policy.Warnings.Any(warning => warning.Contains("Pack include cycle detected", StringComparison.OrdinalIgnoreCase)),
            "policy cycle warning");
    }

    private static void TestAnalysisCatalogValidatorPassesBuiltInCatalog() {
        var workspace = ResolveWorkspaceRoot();
        var validation = IntelligenceX.Analysis.AnalysisCatalogValidator.ValidateWorkspace(workspace);
        AssertEqual(true, validation.IsValid, "catalog validator built-in valid");
        AssertEqual(0, validation.Errors.Count, "catalog validator built-in errors");
    }

    private static void TestAnalysisCatalogValidatorDetectsInvalidCatalog() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "csharp",
  "tool": "roslyn",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX001.duplicate.json"), """
{
  "id": "IX001",
  "language": "csharp",
  "tool": "roslyn",
  "title": "Rule one duplicate",
  "description": "Rule one duplicate"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "pack-a.json"), """
{
  "id": "pack-a",
  "label": "Pack A",
  "includes": ["pack-b", "pack-missing"],
  "rules": ["IX001", "IX404"],
  "severityOverrides": {
    "IX001": "banana"
  }
}
""");
            File.WriteAllText(Path.Combine(packsDir, "pack-b.json"), """
{
  "id": "pack-b",
  "label": "Pack B",
  "includes": ["pack-a"],
  "rules": []
}
""");

            var validation = IntelligenceX.Analysis.AnalysisCatalogValidator.ValidateWorkspace(temp);
            AssertEqual(false, validation.IsValid, "catalog validator invalid catalog");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("Duplicate rule id 'IX001'", StringComparison.OrdinalIgnoreCase)),
                "catalog validator duplicate rule id");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("unknown rule 'IX404'", StringComparison.OrdinalIgnoreCase)),
                "catalog validator unknown rule");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("unknown pack 'pack-missing'", StringComparison.OrdinalIgnoreCase)),
                "catalog validator unknown include");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("unsupported severity 'banana'", StringComparison.OrdinalIgnoreCase)),
                "catalog validator unsupported severity");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("Pack include cycle detected", StringComparison.OrdinalIgnoreCase)),
                "catalog validator include cycle");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisCatalogValidatorDetectsMissingRuleMetadata() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-validate-missing-field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXMISSING.json"), """
{
  "id": "IXMISSING",
  "language": "csharp",
  "tool": "roslyn",
  "description": "Missing title"
}
""");

            var validation = IntelligenceX.Analysis.AnalysisCatalogValidator.ValidateWorkspace(temp);
            AssertEqual(false, validation.IsValid, "catalog validator missing required metadata invalid");
            AssertEqual(true,
                validation.Errors.Any(error => error.Contains("Rule 'IXMISSING' missing required field 'title'",
                    StringComparison.OrdinalIgnoreCase)),
                "catalog validator missing required metadata");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisCatalogRuleOverridesApply() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-overrides-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var overridesDir = Path.Combine(temp, "Analysis", "Catalog", "overrides", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(overridesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IX001",
  "title": "Base rule",
  "description": "Base description",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["base"]
}
""");
            File.WriteAllText(Path.Combine(overridesDir, "IX001.json"), """
{
  "id": "IX001",
  "type": "code-smell",
  "tags": ["override", "base"]
}
""");

            var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(temp);
            AssertEqual(true, catalog.Rules.TryGetValue("IX001", out var rule), "override rule exists");
            AssertEqual("code-smell", rule!.Type, "override type applied");
            AssertEqual(true, rule.Tags.Contains("base"), "override tags include base");
            AssertEqual(true, rule.Tags.Contains("override"), "override tags include override");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisCatalogValidatorRejectsDanglingOverride() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-overrides-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var overridesDir = Path.Combine(temp, "Analysis", "Catalog", "overrides", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(overridesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Base rule",
  "description": "Base description"
}
""");
            File.WriteAllText(Path.Combine(overridesDir, "IX404.json"), """
{
  "id": "IX404",
  "type": "bug",
  "tags": ["dangling"]
}
""");

            var validation = IntelligenceX.Analysis.AnalysisCatalogValidator.ValidateWorkspace(temp);
            AssertEqual(false, validation.IsValid, "dangling override invalid");
            AssertEqual(true, validation.Errors.Any(error => error.Contains("does not match any base rule", StringComparison.OrdinalIgnoreCase)),
                "dangling override error");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsRenderAndStateSnippet() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXHOT001.json"), """
{
  "id": "IXHOT001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXHOT001",
  "type": "security-hotspot",
  "title": "Potential hardcoded secret usage",
  "description": "Security-sensitive pattern that requires human review.",
  "category": "Security",
  "defaultSeverity": "info",
  "tags": ["security-hotspot"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), """
{
  "id": "all-50",
  "label": "All Essentials (50)",
  "rules": ["IXHOT001"]
}
""");

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/test.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-123")
            };

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "### Security Hotspots", "hotspots header");
            AssertContainsText(block, "Hotspots: 1", "hotspots count");
            AssertContainsText(block, "Missing state entries: 1", "hotspots missing state count");
            AssertContainsText(block, "\"key\": \"IXHOT001:fp-123\"", "hotspots suggested key uses fingerprint");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisLoaderIncludesHotspotsBelowMinSeverity() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-minseverity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var originalCwd = Environment.CurrentDirectory;
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
            File.WriteAllText(Path.Combine(artifactsDir, "intelligencex.findings.json"), """
{
  "items": [
    {
      "path": "src/test.cs",
      "line": 10,
      "severity": "info",
      "message": "This is a hotspot that should be shown even when minSeverity=warning.",
      "ruleId": "IXHOT001",
      "tool": "IntelligenceX",
      "fingerprint": "fp-1"
    }
  ]
}
""");

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] { "artifacts/intelligencex.findings.json" };
            settings.Analysis.Results.MinSeverity = "warning";

            var load = AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
            AssertEqual(1, load.Findings.Count, "hotspot finding not filtered by minSeverity");
            AssertEqual("IXHOT001", load.Findings[0].RuleId, "hotspot rule id");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeHotspotsSyncStateWritesStateFile() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-hotspots-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
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

            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            var configPath = Path.Combine(temp, ".intelligencex", "reviewer.json");
            File.WriteAllText(configPath, """
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "hotspots": {
      "show": true,
      "statePath": ".intelligencex/hotspots.json"
    },
    "results": {
      "inputs": ["artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "showPolicy": false,
      "summary": false
    }
  }
}
""");

            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "hotspots",
                "sync-state",
                "--workspace",
                temp,
                "--config",
                configPath
            }).GetAwaiter().GetResult();
            AssertEqual(0, exit, "analyze hotspots sync-state exit");

            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            AssertEqual(true, File.Exists(statePath), "hotspots state file created");
            var text = File.ReadAllText(statePath);
            AssertContainsText(text, "\"schema\": \"intelligencex.hotspots.v1\"", "hotspots state schema");
            AssertContainsText(text, "\"key\": \"IXHOT001:fp-xyz\"", "hotspots state key");
            AssertContainsText(text, "\"status\": \"to-review\"", "hotspots state default status");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeValidateCatalogCommand() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-validate-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "csharp",
  "tool": "roslyn",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "pack-a.json"), """
{
  "id": "pack-a",
  "label": "Pack A",
  "rules": ["IX001"]
}
""");

            var validExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "validate-catalog",
                "--workspace",
                temp
            }).GetAwaiter().GetResult();
            AssertEqual(0, validExit, "analyze validate-catalog valid exit");

            File.WriteAllText(Path.Combine(packsDir, "pack-a.json"), """
{
  "id": "pack-a",
  "label": "Pack A",
  "rules": ["IX404"]
}
""");

            var invalidExit = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(new[] {
                "validate-catalog",
                "--workspace",
                temp
            }).GetAwaiter().GetResult();
            AssertEqual(1, invalidExit, "analyze validate-catalog invalid exit");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesMarkdownFormat() {
        var workspace = ResolveWorkspaceRoot();
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--format",
            "markdown"
        });
        AssertEqual(0, exitCode, "analyze list-rules markdown exit");
        AssertContainsText(output, "| ID | Language | Type | Tool | Tool Rule ID | Default Severity | Category | Title | Docs |",
            "analyze list-rules markdown header");
        AssertContainsText(output, "CA2000", "analyze list-rules markdown includes CA2000");
        AssertContainsText(output, "PSAvoidUsingWriteHost", "analyze list-rules markdown includes powershell rule");
    }

    private static void TestAnalyzeListRulesJsonWithPackFilter() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX002.json"), """
{
  "id": "IX002",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule two",
  "description": "Rule two"
}
""");
            File.WriteAllText(Path.Combine(rulesDir, "IX003.json"), """
{
  "id": "IX003",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule three",
  "description": "Rule three"
}
""");

            File.WriteAllText(Path.Combine(packsDir, "core.json"), """
{
  "id": "core",
  "label": "Core",
  "rules": ["IX001"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "standard.json"), """
{
  "id": "standard",
  "label": "Standard",
  "includes": ["core"],
  "rules": ["IX002"]
}
""");
            File.WriteAllText(Path.Combine(packsDir, "strict.json"), """
{
  "id": "strict",
  "label": "Strict",
  "includes": ["standard"],
  "rules": ["IX003"]
}
""");

            var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--pack",
                "strict",
                "--format",
                "json"
            });

            AssertEqual(0, exitCode, "analyze list-rules json exit");
            var parsed = JsonLite.Parse(output.Trim())?.AsArray();
            AssertNotNull(parsed, "analyze list-rules json payload");
            var ids = new List<string>();
            foreach (var item in parsed!) {
                var obj = item.AsObject();
                if (obj is null) {
                    continue;
                }
                var id = obj.GetString("id");
                if (!string.IsNullOrWhiteSpace(id)) {
                    ids.Add(id!);
                }
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            AssertSequenceEqual(new[] { "IX001", "IX002", "IX003" }, ids, "analyze list-rules json pack includes");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesTierCounts() {
        var workspace = ResolveWorkspaceRoot();
        var (exit50, output50) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-50",
            "--format",
            "json"
        });
        var (exit100, output100) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-100",
            "--format",
            "json"
        });
        var (exit500, output500) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            workspace,
            "--pack",
            "all-500",
            "--format",
            "json"
        });

        AssertEqual(0, exit50, "analyze list-rules all-50 exit");
        AssertEqual(0, exit100, "analyze list-rules all-100 exit");
        AssertEqual(0, exit500, "analyze list-rules all-500 exit");

        var count50 = ParseListedRuleCount(output50, "all-50");
        var count100 = ParseListedRuleCount(output100, "all-100");
        var count500 = ParseListedRuleCount(output500, "all-500");

        AssertEqual(true, count50 >= 50, "analyze list-rules all-50 minimum");
        AssertEqual(true, count100 >= 100, "analyze list-rules all-100 minimum");
        AssertEqual(true, count100 >= count50, "analyze list-rules all-100 expands all-50");
        AssertEqual(true, count500 >= count100, "analyze list-rules all-500 expands all-100");
        AssertEqual(true, count500 <= 500, "analyze list-rules all-500 max bound");
    }

    private static void TestAnalyzeListRulesInvalidFormat() {
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--workspace",
            ResolveWorkspaceRoot(),
            "--format",
            "yaml"
        });
        AssertEqual(1, exitCode, "analyze list-rules invalid format exit");
        AssertContainsText(output, "Unsupported format 'yaml'", "analyze list-rules invalid format message");
    }

    private static void TestAnalyzeListRulesHelp() {
        var (exitCode, output) = RunAnalyzeAndCaptureOutput(new[] {
            "list-rules",
            "--help"
        });
        AssertEqual(0, exitCode, "analyze list-rules help exit");
        AssertContainsText(output, "intelligencex analyze list-rules", "analyze list-rules help usage");
    }

    private static void TestAnalyzeListRulesJsonWarningsToStderr() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-warn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IX001.json"), """
{
  "id": "IX001",
  "language": "internal",
  "tool": "IntelligenceX",
  "title": "Rule one",
  "description": "Rule one"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "strict.json"), """
{
  "id": "strict",
  "label": "Strict",
  "includes": ["missing-pack"],
  "rules": ["IX001"]
}
""");

            var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--pack",
                "strict",
                "--format",
                "json"
            });
            AssertEqual(0, exitCode, "analyze list-rules json warnings exit");
            var parsed = JsonLite.Parse(stdout.Trim())?.AsArray();
            AssertNotNull(parsed, "analyze list-rules json warnings payload");
            AssertContainsText(stderr, "Warning: Included pack not found: missing-pack", "analyze list-rules json warnings stderr");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeListRulesJsonEmptyOutputsArray() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-list-rules-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(new[] {
                "list-rules",
                "--workspace",
                temp,
                "--format",
                "json"
            });
            AssertEqual(0, exitCode, "analyze list-rules empty json exit");
            AssertEqual("[]", stdout.Trim(), "analyze list-rules empty json payload");
            AssertEqual(string.Empty, stderr.Trim(), "analyze list-rules empty json stderr");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static int ParseListedRuleCount(string output, string scope) {
        var parsed = JsonLite.Parse((output ?? string.Empty).Trim())?.AsArray();
        AssertNotNull(parsed, $"analyze list-rules {scope} json payload");
        var count = 0;
        foreach (var item in parsed!) {
            if (item.AsObject() is not null) {
                count++;
            }
        }
        return count;
    }

    private static void TestAnalysisCatalogRuleDocsPath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);
            Directory.CreateDirectory(Path.Combine(temp, "Docs", "reviewer"));

            var docsPath = "Docs/reviewer/static-analysis.md";
            File.WriteAllText(Path.Combine(temp, docsPath), "# docs");
            File.WriteAllText(Path.Combine(rulesDir, "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "docs": "Docs/reviewer/static-analysis.md"
}
""");

            var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(temp);
            AssertEqual(true, catalog.Rules.TryGetValue("IXLOC001", out var rule), "analysis docs rule exists");
            AssertEqual(docsPath, rule!.Docs, "analysis docs path stored");
            var resolvedDocsPath = Path.Combine(temp, rule.Docs!.Replace('/', Path.DirectorySeparatorChar));
            AssertEqual(true, File.Exists(resolvedDocsPath), "analysis docs path resolves from workspace");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunAnalyzeAndCaptureStreams(string[] args) {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Analysis.AnalyzeRunner.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static (int ExitCode, string Output) RunAnalyzeAndCaptureOutput(string[] args) {
        var (exitCode, stdout, stderr) = RunAnalyzeAndCaptureStreams(args);
        return (exitCode, stdout + stderr);
    }

    private static string ResolveWorkspaceRoot() {
        var current = Environment.CurrentDirectory;
        for (var i = 0; i < 12; i++) {
            var rulesDir = Path.Combine(current, "Analysis", "Catalog", "rules");
            var packsDir = Path.Combine(current, "Analysis", "Packs");
            if (Directory.Exists(rulesDir) && Directory.Exists(packsDir)) {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) {
                break;
            }
            current = parent.FullName;
        }
        return Environment.CurrentDirectory;
    }
}
#endif
