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
            DeleteDirectoryIfExistsWithRetries(temp);
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

    private static void TestAnalysisCatalogLoaderUnderRootRejectsSiblingPrefixPath() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-catalog-loader-root-" + Guid.NewGuid().ToString("N"));
        var sibling = temp + "2";
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(sibling);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Analysis.AnalysisCatalogLoader).GetMethod("IsUnderRoot", flags);
            AssertNotNull(method, "analysis catalog loader IsUnderRoot exists");

            var rootPath = Path.GetFullPath(temp);
            var nestedPath = Path.Combine(rootPath, "Analysis", "Catalog", "rules", "internal", "IX001.json");
            var siblingPath = Path.Combine(Path.GetFullPath(sibling), "Analysis", "Catalog", "rules", "internal", "IX001.json");

            var nestedResult = (bool)method!.Invoke(null, new object[] { rootPath, nestedPath })!;
            var siblingResult = (bool)method!.Invoke(null, new object[] { rootPath, siblingPath })!;

            AssertEqual(true, nestedResult, "analysis catalog loader accepts nested path");
            AssertEqual(false, siblingResult, "analysis catalog loader rejects sibling prefix path");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
            DeleteDirectoryIfExistsWithRetries(sibling);
        }
    }

    private static void TestAnalysisCatalogLoaderUnderRootCaseSensitivityByPlatform() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-catalog-loader-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Analysis.AnalysisCatalogLoader).GetMethod("IsUnderRoot", flags);
            AssertNotNull(method, "analysis catalog loader IsUnderRoot exists (case sensitivity)");

            var rootPath = Path.GetFullPath(temp);
            var nestedPath = Path.Combine(rootPath, "Analysis", "Catalog", "rules", "internal", "IX001.json");
            var rootCaseVariant = TogglePathCase(rootPath);
            if (string.Equals(rootCaseVariant, rootPath, StringComparison.Ordinal)) {
                AssertEqual(true, true, "analysis catalog loader case sensitivity setup");
                return;
            }

            var caseVariantResult = (bool)method!.Invoke(null, new object[] { rootCaseVariant, nestedPath })!;
            var expectCaseInsensitive = Path.DirectorySeparatorChar == '\\';
            AssertEqual(expectCaseInsensitive, caseVariantResult,
                "analysis catalog loader root comparison follows platform case semantics");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisCatalogLoaderUnderRootAcceptsMixedSeparators() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-catalog-loader-separators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
            var method = typeof(IntelligenceX.Analysis.AnalysisCatalogLoader).GetMethod("IsUnderRoot", flags);
            AssertNotNull(method, "analysis catalog loader IsUnderRoot exists (mixed separators)");

            if (Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar) {
                AssertEqual(true, true, "analysis catalog loader mixed separator setup");
                return;
            }

            var rootPath = Path.GetFullPath(temp);
            var nestedPath = Path.Combine(rootPath, "Analysis", "Catalog", "rules", "internal", "IX001.json");
            var mixedRootPath = rootPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var mixedNestedPath = nestedPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var mixedCandidateResult = (bool)method!.Invoke(null, new object[] { rootPath, mixedNestedPath })!;
            var mixedRootResult = (bool)method!.Invoke(null, new object[] { mixedRootPath, nestedPath })!;

            AssertEqual(true, mixedCandidateResult, "analysis catalog loader accepts mixed-separator candidate path");
            AssertEqual(true, mixedRootResult, "analysis catalog loader accepts mixed-separator root path");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisCatalogLoaderTrimPreservesFilesystemRoot() {
        var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
        var trimMethod = typeof(IntelligenceX.Analysis.AnalysisCatalogLoader)
            .GetMethod("TrimEndingDirectorySeparators", flags);
        AssertNotNull(trimMethod, "analysis catalog loader trim helper exists");

        var filesystemRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath())) ?? string.Empty;
        if (string.IsNullOrEmpty(filesystemRoot)) {
            AssertEqual(true, true, "analysis catalog loader root trim setup");
            return;
        }

        var trimmedRoot = (string)trimMethod!.Invoke(null, new object[] { filesystemRoot })!;
        var paddedRoot = filesystemRoot + Path.DirectorySeparatorChar + Path.AltDirectorySeparatorChar;
        var trimmedPaddedRoot = (string)trimMethod.Invoke(null, new object[] { paddedRoot })!;

        AssertEqual(filesystemRoot, trimmedRoot, "analysis catalog loader trim preserves filesystem root");
        AssertEqual(filesystemRoot, trimmedPaddedRoot,
            "analysis catalog loader trim preserves filesystem root with trailing separators");
    }

    private static string TogglePathCase(string path) {
        if (string.IsNullOrEmpty(path)) {
            return string.Empty;
        }

        var chars = path.ToCharArray();
        for (var i = 0; i < chars.Length; i++) {
            if (!char.IsLetter(chars[i])) {
                continue;
            }
            chars[i] = char.IsUpper(chars[i]) ? char.ToLowerInvariant(chars[i]) : char.ToUpperInvariant(chars[i]);
            return new string(chars);
        }

        return path;
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }


}
#endif
