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

    private static void TestAnalysisCatalogPowerShellOverridesApply() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        var rulesDir = Path.Combine(workspace, "Analysis", "Catalog", "rules", "powershell");
        var overridesDir = Path.Combine(workspace, "Analysis", "Catalog", "overrides", "powershell");
        AssertEqual(true, Directory.Exists(overridesDir), "powershell overrides dir exists");

        // Load the catalog without overrides so we can compare base vs effective without needing per-override temp workspaces.
        var rulesRoot = Path.Combine(workspace, "Analysis", "Catalog", "rules");
        var packsRoot = Path.Combine(workspace, "Analysis", "Packs");
        var tempOverridesRoot = Path.Combine(Path.GetTempPath(), "ix-analysis-overrides-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempOverridesRoot);

        Exception? testFailure = null;
        try {
            var baseCatalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromPaths(rulesRoot, tempOverridesRoot, packsRoot);

            foreach (var overridePath in Directory.EnumerateFiles(overridesDir, "*.json")) {
                var overrideText = File.ReadAllText(overridePath, System.Text.Encoding.UTF8);
                using var overrideDoc = System.Text.Json.JsonDocument.Parse(overrideText);
                var overrideRoot = overrideDoc.RootElement;

                if (!overrideRoot.TryGetProperty("id", out var idElement) || idElement.ValueKind != System.Text.Json.JsonValueKind.String) {
                    throw new InvalidOperationException($"Override '{Path.GetFileName(overridePath)}' is missing string 'id' property.");
                }

                var id = idElement.GetString();
                AssertEqual(false, string.IsNullOrWhiteSpace(id), $"{Path.GetFileName(overridePath)} override has id");
                if (string.IsNullOrWhiteSpace(id)) {
                    throw new Exception($"{Path.GetFileName(overridePath)} override has no id");
                }
                AssertEqual(id, Path.GetFileNameWithoutExtension(overridePath), $"{id} override filename matches id");

                var basePath = Path.Combine(rulesDir, id + ".json");
                AssertEqual(true, File.Exists(basePath), $"{id} base rule exists for override");

                AssertEqual(true, catalog.Rules.TryGetValue(id, out var effective), $"{id} exists in catalog");
                if (effective is null) {
                    throw new Exception($"{id} exists in catalog but is null");
                }

                AssertEqual(true, baseCatalog.Rules.TryGetValue(id, out var resolvedBase), $"{id} exists in base catalog");
                var baseRule = resolvedBase ?? throw new Exception($"{id} exists in base catalog but is null");

                // Ensure our "base catalog" truly reflects the rule JSON without applying any overrides.
                var baseText = File.ReadAllText(basePath, System.Text.Encoding.UTF8);
                using (var baseDoc = System.Text.Json.JsonDocument.Parse(baseText)) {
                    var baseRoot = baseDoc.RootElement;
                    if (!baseRoot.TryGetProperty("title", out var baseTitle) || baseTitle.ValueKind != System.Text.Json.JsonValueKind.String) {
                        throw new Exception($"{id} base rule json missing string 'title' property");
                    }
                    if (!baseRoot.TryGetProperty("description", out var baseDescription) || baseDescription.ValueKind != System.Text.Json.JsonValueKind.String) {
                        throw new Exception($"{id} base rule json missing string 'description' property");
                    }
                    AssertEqual(baseTitle.GetString(), baseRule.Title, $"{id} base title matches rule json");
                    AssertEqual(baseDescription.GetString(), baseRule.Description, $"{id} base description matches rule json");
                }

                var sawOverrideProperty = false;
                foreach (var prop in overrideRoot.EnumerateObject()) {
                    if (prop.NameEquals("id")) {
                        continue;
                    }
                    sawOverrideProperty = true;

                    switch (prop.Name) {
                        case "title": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override title must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override title must be a string");
                            AssertEqual(expected, effective.Title, $"{id} override title applied");
                            break;
                        }
                        case "description": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override description must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override description must be a string");
                            AssertEqual(expected, effective.Description, $"{id} override description applied");
                            break;
                        }
                        case "type": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override type must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override type must be a string");
                            AssertEqual(expected, effective.Type, $"{id} override type applied");
                            break;
                        }
                        case "category": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override category must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override category must be a string");
                            AssertEqual(expected, effective.Category, $"{id} override category applied");
                            break;
                        }
                        case "defaultSeverity": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override defaultSeverity must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override defaultSeverity must be a string");
                            AssertEqual(expected, effective.DefaultSeverity, $"{id} override defaultSeverity applied");
                            break;
                        }
                        case "docs": {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                                throw new Exception($"{id} override docs must be a string");
                            }
                            var expected = prop.Value.GetString() ?? throw new Exception($"{id} override docs must be a string");
                            AssertEqual(expected, effective.Docs, $"{id} override docs applied");
                            break;
                        }
                        case "tags": {
                            static IReadOnlyList<string> MergeTags(IReadOnlyList<string> existing, IReadOnlyList<string> overrides) {
                                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                var merged = new List<string>();
                                foreach (var tag in existing ?? Array.Empty<string>()) {
                                    if (string.IsNullOrWhiteSpace(tag)) {
                                        continue;
                                    }
                                    var value = tag.Trim();
                                    if (set.Add(value)) {
                                        merged.Add(value);
                                    }
                                }
                                foreach (var tag in overrides ?? Array.Empty<string>()) {
                                    if (string.IsNullOrWhiteSpace(tag)) {
                                        continue;
                                    }
                                    var value = tag.Trim();
                                    if (set.Add(value)) {
                                        merged.Add(value);
                                    }
                                }
                                return merged;
                            }

                            AssertEqual(System.Text.Json.JsonValueKind.Array, prop.Value.ValueKind, $"{id} override tags is array");
                            var overrideTags = prop.Value.EnumerateArray()
                                .Select(x => x.GetString() ?? throw new Exception($"{id} override tags must be strings"))
                                .ToArray();

                            var expectedMerged = MergeTags(baseRule.Tags, overrideTags);
                            var expectedSet = new HashSet<string>(expectedMerged, StringComparer.OrdinalIgnoreCase);
                            var actualSet = new HashSet<string>(effective.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                            AssertEqual(expectedSet.Count, actualSet.Count, $"{id} merged tag count matches");
                            foreach (var tag in expectedSet) {
                                AssertEqual(true, actualSet.Contains(tag), $"{id} merged tags contains '{tag}'");
                            }
                            break;
                        }
                        default:
                            throw new Exception($"Unsupported PowerShell override property '{prop.Name}' in {Path.GetFileName(overridePath)}");
                    }
                }

                AssertEqual(true, sawOverrideProperty, $"{id} override has at least one property besides id");
            }
        } catch (Exception ex) {
            testFailure = ex;
            throw;
        } finally {
            Exception? cleanupFailure = null;
            for (var attempt = 0; attempt < 5; attempt++) {
                try {
                    if (Directory.Exists(tempOverridesRoot)) {
                        Directory.Delete(tempOverridesRoot, true);
                    }
                    cleanupFailure = null;
                    break;
                } catch (Exception ex) {
                    cleanupFailure = ex;
                    System.Threading.Thread.Sleep(50);
                }
            }

            if (cleanupFailure is not null && Directory.Exists(tempOverridesRoot)) {
                if (testFailure is null) {
                    throw new Exception($"Failed to delete temp overrides dir '{tempOverridesRoot}'.", cleanupFailure);
                }
                Console.Error.WriteLine($"WARN: failed to delete temp overrides dir '{tempOverridesRoot}': {cleanupFailure.Message}");
            }
        }
    }

    private static void TestAnalysisCatalogPowerShellDocsLinksMatchLearnPattern() {
        var workspace = ResolveWorkspaceRoot();
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        foreach (var entry in catalog.Rules) {
            var rule = entry.Value;
            if (!string.Equals(rule.Language, "powershell", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!string.Equals(rule.Tool, "PSScriptAnalyzer", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            AssertEqual(false, string.IsNullOrWhiteSpace(rule.Docs), $"{rule.Id} docs is populated");

            var docs = rule.Docs!.Trim();
            AssertEqual(false, docs.Any(char.IsWhiteSpace), $"{rule.Id} docs has no whitespace");
            AssertEqual(true, Uri.IsWellFormedUriString(docs, UriKind.Absolute), $"{rule.Id} docs is well-formed");
            if (!Uri.TryCreate(docs, UriKind.Absolute, out var uri) || uri is null) {
                throw new InvalidOperationException($"Expected {rule.Id} docs to be a valid absolute url, got '{docs}'.");
            }
            AssertEqual("https", uri.Scheme, $"{rule.Id} docs uses https");

            AssertEqual("learn.microsoft.com", uri.Host, $"{rule.Id} docs host is Learn");

            var path = uri.AbsolutePath;
            const string learnPrefix = "/powershell/utility-modules/psscriptanalyzer/rules/";
            AssertEqual(true, path.StartsWith(learnPrefix, StringComparison.OrdinalIgnoreCase), $"{rule.Id} docs uses PSScriptAnalyzer Learn rules path");

            var expectedSlug = rule.ToolRuleId;
            if (expectedSlug.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) {
                expectedSlug = expectedSlug.Substring(2);
            }
            expectedSlug = expectedSlug.ToLowerInvariant();

            var actualSlug = path.Substring(learnPrefix.Length).Trim('/').ToLowerInvariant();
            AssertEqual(expectedSlug, actualSlug, $"{rule.Id} docs slug matches rule id");
        }
    }

    private static void TestAnalysisCatalogOverrideInvalidTypeFallsBack() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-overrides-invalid-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var overridesDir = Path.Combine(temp, "Analysis", "Catalog", "overrides", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(overridesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "IXSEC001.json"), """
{
  "id": "IXSEC001",
  "language": "internal",
  "tool": "IntelligenceX",
  "toolRuleId": "IXSEC001",
  "title": "Base rule",
  "description": "Base description",
  "category": "Security",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(overridesDir, "IXSEC001.json"), """
{
  "id": "IXSEC001",
  "type": "nonsense"
}
""");

            var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(temp);
            AssertEqual(true, catalog.Rules.TryGetValue("IXSEC001", out var rule), "override invalid-type rule exists");
            AssertEqual("vulnerability", rule!.Type, "override invalid type falls back to inferred category type");
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


}
#endif
