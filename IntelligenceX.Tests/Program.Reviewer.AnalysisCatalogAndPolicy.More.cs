namespace IntelligenceX.Tests;

internal static partial class Program {
    #if INTELLIGENCEX_REVIEWER
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisCatalogPowerShellOverridesApply() {
        var workspace = ResolveWorkspaceRoot();
        // Hermetic: validates checked-in catalog JSON/overrides only (does not invoke PSScriptAnalyzer or the sync script).
        var catalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromWorkspace(workspace);

        var rulesDir = Path.Combine(workspace, "Analysis", "Catalog", "rules", "powershell");
        var overridesDir = Path.Combine(workspace, "Analysis", "Catalog", "overrides", "powershell");
        AssertEqual(true, Directory.Exists(overridesDir), "powershell overrides dir exists");

        // Load the catalog without overrides so we can compare base vs effective without needing per-override temp workspaces.
        var rulesRoot = Path.Combine(workspace, "Analysis", "Catalog", "rules");
        var packsRoot = Path.Combine(workspace, "Analysis", "Packs");
        var explicitOverridesRoot = Path.Combine(workspace, "Analysis", "Catalog", "overrides");
        var effectiveCatalogFromPaths = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromPaths(
            rulesRoot,
            explicitOverridesRoot,
            packsRoot);

        // Avoid creating/deleting temp directories (flake risk on Windows). Passing a non-existent overrides root
        // is sufficient to ensure no overrides are applied.
        var missingOverridesRoot = Path.Combine(Path.GetTempPath(), "ix-analysis-missing-overrides-" + Guid.NewGuid().ToString("N"));
        AssertEqual(false, Directory.Exists(missingOverridesRoot), "missing overrides root does not exist");
        var baseCatalog = IntelligenceX.Analysis.AnalysisCatalogLoader.LoadFromPaths(
            rulesRoot,
            missingOverridesRoot,
            packsRoot);

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

            AssertEqual(true, effectiveCatalogFromPaths.Rules.TryGetValue(id, out var effectiveFromPaths), $"{id} exists in explicit-overrides catalog");
            if (effectiveFromPaths is null) {
                throw new Exception($"{id} exists in explicit-overrides catalog but is null");
            }
            AssertEqual(effective.Title, effectiveFromPaths.Title, $"{id} explicit-overrides title matches workspace loader");
            AssertEqual(effective.Description, effectiveFromPaths.Description, $"{id} explicit-overrides description matches workspace loader");

            AssertEqual(true, baseCatalog.Rules.TryGetValue(id, out var resolvedBase), $"{id} exists in base catalog");
            var baseRule = resolvedBase ?? throw new Exception($"{id} exists in base catalog but is null");

            // Ensure our "base catalog" truly reflects the rule JSON without applying any overrides.
            var baseText = File.ReadAllText(basePath, System.Text.Encoding.UTF8);
            using (var baseDoc = System.Text.Json.JsonDocument.Parse(baseText)) {
                var baseRoot = baseDoc.RootElement;

                AssertEqual(true, baseRoot.TryGetProperty("title", out var baseTitle), $"{id} base rule json has 'title'");
                AssertEqual(System.Text.Json.JsonValueKind.String, baseTitle.ValueKind, $"{id} base rule json title is string");

                AssertEqual(true, baseRoot.TryGetProperty("description", out var baseDescription), $"{id} base rule json has 'description'");
                AssertEqual(System.Text.Json.JsonValueKind.String, baseDescription.ValueKind, $"{id} base rule json description is string");

                AssertEqual(baseTitle.GetString(), baseRule.Title, $"{id} base title matches rule json");
                AssertEqual(baseDescription.GetString(), baseRule.Description, $"{id} base description matches rule json");
            }

            // Validate override schema and verify that each overridden value is reflected in the effective rule.
            // Keep this block inside the per-override loop so each file is validated independently.
            var sawSupportedOverrideProperty = false;
            var changesBase = false;
            foreach (var prop in overrideRoot.EnumerateObject()) {
                if (prop.NameEquals("id")) {
                    continue;
                }

                switch (prop.Name) {
                    case "title": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override title must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.Title, effective.Title, $"{id} override title blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.Title, $"{id} override title applied");
                        if (!string.Equals(expected, baseRule.Title, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "description": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override description must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.Description, effective.Description, $"{id} override description blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.Description, $"{id} override description applied");
                        if (!string.Equals(expected, baseRule.Description, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "type": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override type must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.Type, effective.Type, $"{id} override type blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.Type, $"{id} override type applied");
                        if (!string.Equals(expected, baseRule.Type, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "category": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override category must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.Category, effective.Category, $"{id} override category blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.Category, $"{id} override category applied");
                        if (!string.Equals(expected, baseRule.Category, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "defaultSeverity": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override defaultSeverity must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.DefaultSeverity, effective.DefaultSeverity, $"{id} override defaultSeverity blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.DefaultSeverity, $"{id} override defaultSeverity applied");
                        if (!string.Equals(expected, baseRule.DefaultSeverity, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "docs": {
                        sawSupportedOverrideProperty = true;
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) {
                            throw new Exception($"{id} override docs must be a string");
                        }
                        var expectedRaw = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(expectedRaw)) {
                            // ApplyOverride treats whitespace as "no override".
                            AssertEqual(baseRule.Docs, effective.Docs, $"{id} override docs blank/no-op");
                            break;
                        }
                        var expected = expectedRaw;
                        AssertEqual(expected, effective.Docs, $"{id} override docs applied");
                        if (!string.Equals(expected, baseRule.Docs, StringComparison.Ordinal)) {
                            changesBase = true;
                        }
                        break;
                    }
                    case "tags": {
                        sawSupportedOverrideProperty = true;

                        static System.Collections.Generic.IReadOnlyList<string> MergeTags(
                            System.Collections.Generic.IReadOnlyList<string> existing,
                            System.Collections.Generic.IReadOnlyList<string> overrides) {
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
                            .Select(x => x.ValueKind == System.Text.Json.JsonValueKind.String
                                ? x.GetString()!
                                : throw new Exception($"{id} override tags entries must be strings"))
                            .ToArray();

                        var expectedMerged = MergeTags(baseRule.Tags ?? Array.Empty<string>(), overrideTags);
                        var expectedSet = new HashSet<string>(expectedMerged, StringComparer.OrdinalIgnoreCase);
                        var actualSet = new HashSet<string>(effective.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        AssertEqual(true, expectedSet.SetEquals(actualSet), $"{id} merged tags set equals");
                        var baseSet = new HashSet<string>(baseRule.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        if (!baseSet.SetEquals(actualSet)) {
                            changesBase = true;
                        }
                        break;
                    }
                    default:
                        // Production currently ignores unknown override properties; fail fast in tests so typos
                        // (e.g., "defualtSeverity") don't silently make overrides ineffective.
                        throw new Exception($"{id} override has unsupported property '{prop.Name}'.");
                }
            }

            AssertEqual(true, sawSupportedOverrideProperty, $"{id} override has at least one supported property besides id");

            // No-op overrides are allowed: they can document intent, normalize text, or future-proof rule metadata.
            _ = changesBase;
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
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    #endif
}
