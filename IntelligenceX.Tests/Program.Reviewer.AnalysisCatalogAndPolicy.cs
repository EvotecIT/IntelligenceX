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
            AssertContainsText(block, "IXHOT001:fp-", "hotspots suggested key uses fingerprint hash");
            AssertEqual(false, block.Contains("fp-123", StringComparison.Ordinal), "hotspots suggested key does not include raw fingerprint");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsRedactsAbsoluteStatePath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-statepath-" + Guid.NewGuid().ToString("N"));
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

            var absoluteStatePath = Path.Combine(Path.GetTempPath(), "ix-private-" + Guid.NewGuid().ToString("N"), "hotspots.json");

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.ShowStateSummary = true;
            settings.Analysis.Hotspots.StatePath = absoluteStatePath;

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/test.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-123")
            };

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "- State file: `hotspots.json`", "hotspots state path redacts absolute directory");

            var tmp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            AssertEqual(false, block.Contains(tmp, StringComparison.OrdinalIgnoreCase), "hotspots state path not absolute");
            AssertEqual(false, block.Contains(tmp.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase),
                "hotspots state path not absolute (slash)");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsMaxItemsSemantics() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-maxitems-" + Guid.NewGuid().ToString("N"));
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

            var findings = new List<AnalysisFinding>();
            for (var i = 0; i < 11; i++) {
                findings.Add(new AnalysisFinding($"src/test{i:D2}.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", $"fp-{i}"));
            }

            Environment.CurrentDirectory = temp;

            var baseSettings = new ReviewSettings();
            baseSettings.Analysis.Enabled = true;
            baseSettings.Analysis.Hotspots.Show = true;
            baseSettings.Analysis.Hotspots.ShowStateSummary = false;

            // MaxItems=0 hides items.
            baseSettings.Analysis.Hotspots.MaxItems = 0;
            var hidden = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(hidden, "Items: hidden (maxItems=0)", "hotspots maxItems 0 hides items");

            // Default MaxItems=10 limits to 10 items.
            baseSettings.Analysis.Hotspots.MaxItems = 10;
            var defaulted = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(defaulted, "- Showing first 10 of 11 hotspot(s).", "hotspots maxItems default 10 limits items");

            // MaxItems>0 limits to that number.
            baseSettings.Analysis.Hotspots.MaxItems = 2;
            var limited = AnalysisHotspots.BuildBlock(baseSettings, findings);
            AssertContainsText(limited, "- Showing first 2 of 11 hotspot(s).", "hotspots maxItems 2 limits items");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsSuppressedCountSemantics() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-suppressed-" + Guid.NewGuid().ToString("N"));
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

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";
            settings.Analysis.Hotspots.ShowStateSummary = false;

            var findings = new List<AnalysisFinding> {
                new AnalysisFinding("src/visible.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-visible"),
                new AnalysisFinding("src/suppressed.cs", 10, "Review this usage.", "warning", "IXHOT001", "IntelligenceX", "fp-suppressed")
            };

            // One visible hotspot (to-review) and one suppressed hotspot.
            var visibleKey = AnalysisHotspots.ComputeHotspotKey(findings[0]);
            var suppressedKey = AnalysisHotspots.ComputeHotspotKey(findings[1]);
            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath,
                "{\n" +
                "  \"schema\": \"intelligencex.hotspots.v1\",\n" +
                "  \"items\": [\n" +
                $"    {{ \"key\": \"{visibleKey}\", \"status\": \"to-review\" }},\n" +
                $"    {{ \"key\": \"{suppressedKey}\", \"status\": \"suppress\" }}\n" +
                "  ]\n" +
                "}\n");

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "- Hotspots: 1", "hotspots headline excludes suppressed");
            AssertContainsText(block, "(suppressed: 1)", "hotspots suppressed count is reported");
            AssertContainsText(block, visibleKey, "hotspots list includes visible key");
            AssertEqual(false, block.Contains(suppressedKey, StringComparison.OrdinalIgnoreCase),
                "hotspots list excludes suppressed key");
        } finally {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisHotspotsKeyHashingUsesUtf8Bytes() {
        // Non-ASCII inputs should hash deterministically based on UTF-8 bytes.
        var finding = new AnalysisFinding(
            "src/naïve.cs",
            42,
            "Use café secrets",
            "warning",
            "IXHOT002",
            "IntelligenceX",
            null);

        var key = AnalysisHotspots.ComputeHotspotKey(finding);
        AssertEqual("IXHOT002:e4ab3f06e3e88089", key, "hotspots key hashing uses UTF-8 bytes (FNV-1a 64)");
    }

    private static void TestAnalysisHotspotsOutputEscapesMarkdownInjection() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-hotspots-escape-" + Guid.NewGuid().ToString("N"));
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

            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Hotspots.Show = true;
            settings.Analysis.Hotspots.StatePath = ".intelligencex/hotspots.json";
            settings.Analysis.Hotspots.ShowStateSummary = false;

            var finding = new AnalysisFinding(
                "src/test.cs",
                10,
                "Hello\n- [x] injected",
                "warning",
                "IXHOT001",
                "IntelligenceX",
                "fp-inject");
            var findings = new List<AnalysisFinding> { finding };

            var key = AnalysisHotspots.ComputeHotspotKey(finding);
            var statePath = Path.Combine(temp, ".intelligencex", "hotspots.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath,
                "{\n" +
                "  \"schema\": \"intelligencex.hotspots.v1\",\n" +
                "  \"items\": [\n" +
                $"    {{ \"key\": \"{key}\", \"status\": \"to-review\", \"note\": \"`oops`\\n**bold**\" }}\n" +
                "  ]\n" +
                "}\n");

            var block = AnalysisHotspots.BuildBlock(settings, findings);
            AssertContainsText(block, "`Hello - [x] injected`", "hotspots message is rendered as safe inline code");
            AssertContainsText(block, "Note: `'oops' **bold**`", "hotspots note is rendered as safe inline code");
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

            // GitHub Actions sets GITHUB_WORKSPACE; the loader resolves relative inputs against it when present.
            // Point it at this temp workspace so the test behaves the same locally and in CI.
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);
            Environment.CurrentDirectory = temp;
            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] { "artifacts/intelligencex.findings.json" };
            settings.Analysis.Results.MinSeverity = "warning";

            var load = AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
            AssertEqual(1, load.Findings.Count, "hotspot finding not filtered by minSeverity");
            AssertEqual("IXHOT001", load.Findings[0].RuleId, "hotspot rule id");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

}
#endif
