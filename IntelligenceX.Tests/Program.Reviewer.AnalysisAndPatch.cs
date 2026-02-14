namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisPolicyDisableToolRuleId() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["default"] = new AnalysisPack(
                "default", "Default", "Test pack", new[] { "IX001" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null)
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "default" },
            DisabledRules = new[] { "CA2000" }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(0, policy.Warnings.Count, "policy warnings");
        AssertEqual(0, policy.Rules.Count, "policy disabled by tool rule id");
    }

    private static void TestAnalyzeRunDisabledWritesEmptyFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-run-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(temp, "artifacts");
        var config = Path.Combine(temp, "reviewer.json");
        Directory.CreateDirectory(temp);
        try {
            File.WriteAllText(config, "{ \"analysis\": { \"enabled\": false } }");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", config,
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run disabled exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run findings exists");
            var content = File.ReadAllText(findingsPath);
            var findingsJson = JsonLite.Parse(content)?.AsObject();
            AssertNotNull(findingsJson, "analyze run disabled findings json");
            AssertEqual("intelligencex.findings.v1", findingsJson!.GetString("schema") ?? string.Empty,
                "analyze run disabled schema");
            var items = findingsJson.GetArray("items");
            AssertNotNull(items, "analyze run disabled findings items");
            AssertEqual(0, items!.Count, "analyze run disabled findings count");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunNonStrictAllowsRunnerFailure() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: false);
        AssertEqual(0, exit, "analyze run non-strict exits success on runner failure");
    }

    private static void TestAnalyzeRunStrictFromConfigFailsRunnerFailure() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: true);
        AssertEqual(1, exit, "analyze run strict from config exits failure on runner failure");
    }

    private static void TestAnalyzeRunStrictFlagFalseOverridesConfigStrictTrue() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: true, strictOverride: false);
        AssertEqual(0, exit, "analyze run strict false flag overrides config strict true");
    }

    private static void TestAnalyzeRunStrictEqualsFalseOverridesConfigStrictTrue() {
        var exit = RunAnalyzeRunWithMissingDotnet(
            strict: true,
            strictOverride: false,
            strictOverrideEqualsSyntax: true);
        AssertEqual(0, exit, "analyze run --strict=false overrides config strict true");
    }

    private static void TestAnalyzeRunPacksOverrideSkipsConfiguredCsharpFailure() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: true, packsOverride: "internal-default");
        AssertEqual(0, exit, "analyze run pack override skips configured csharp runner failure");
    }

    private static void TestAnalyzeRunInvalidPackOverrideFails() {
        var exit = RunAnalyzeRunWithMissingDotnet(strict: false, packsOverride: "all-50,--force");
        AssertEqual(1, exit, "analyze run invalid pack override fails");
    }

    private static int RunAnalyzeRunWithMissingDotnet(
        bool strict,
        bool? strictOverride = null,
        bool strictOverrideEqualsSyntax = false,
        string? packsOverride = null) {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-run-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), $$"""
{
  "analysis": {
    "enabled": true,
    "packs": ["csharp-default"],
    "run": {
      "strict": {{strict.ToString().ToLowerInvariant()}}
    }
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp", "IXCS001.json"), """
{
  "id": "IXCS001",
  "language": "csharp",
  "tool": "roslyn",
  "toolRuleId": "CS0001",
  "type": "bug",
  "title": "C# smoke rule",
  "description": "Ensures C# analyzer runner is selected.",
  "category": "Reliability",
  "defaultSeverity": "warning"
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "csharp-default.json"), """
{
  "id": "csharp-default",
  "label": "C# Default",
  "rules": ["IXCS001"]
}
""");
            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXINT001.json"), """
{
  "id": "IXINT001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXINT001",
  "type": "maintainability",
  "title": "Internal smoke rule",
  "description": "Used for analyze run pack override tests.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-lines:10000",
    "include-ext:.cs"
  ]
}
""");
            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "internal-default.json"), """
{
  "id": "internal-default",
  "label": "Internal Default",
  "rules": ["IXINT001"]
}
""");

            var output = Path.Combine(temp, "artifacts");
            var args = new List<string> {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output,
                "--dotnet-command", "__ix_missing_dotnet_command__"
            };
            if (!string.IsNullOrWhiteSpace(packsOverride)) {
                args.Add("--packs");
                args.Add(packsOverride);
            }
            if (strictOverride.HasValue) {
                if (strictOverrideEqualsSyntax) {
                    args.Add("--strict=" + strictOverride.Value.ToString().ToLowerInvariant());
                } else {
                    args.Add("--strict");
                    if (!strictOverride.Value) {
                        args.Add("false");
                    }
                }
            }

            return IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(args.ToArray()).GetAwaiter().GetResult();
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFileSizeRule() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-lines:700",
    "generated-marker:<auto-generated",
    "generated-marker:<autogenerated",
    "generated-marker:@generated",
    "generated-suffix:.designer.cs",
    "generated-suffix:.generated.cs",
    "generated-suffix:.g.cs",
    "generated-suffix:.g.i.cs"
  ]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var largeFile = Path.Combine(temp, "LargeFile.cs");
            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(largeFile, string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal rule exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal findings exists");
            var content = File.ReadAllText(findingsPath);
            var findingsJson = JsonLite.Parse(content).AsObject();
            AssertNotNull(findingsJson, "analyze run internal findings json");
            AssertEqual("intelligencex.findings.v1", findingsJson!.GetString("schema") ?? string.Empty,
                "analyze run internal schema");
            var findingsItems = findingsJson!.GetArray("items");
            AssertNotNull(findingsItems, "analyze run internal findings items");
            var hasMappedInternalRule = false;
            foreach (var item in findingsItems!) {
                var finding = item.AsObject();
                if (finding is null) {
                    continue;
                }
                var ruleId = finding.GetString("ruleId");
                var tool = finding.GetString("tool");
                if (string.Equals(ruleId, "IXLOC001", StringComparison.Ordinal) &&
                    string.Equals(tool, "IntelligenceX.Maintainability", StringComparison.Ordinal)) {
                    hasMappedInternalRule = true;
                    break;
                }
            }
            AssertEqual(true, content.Contains("IXLOC001", StringComparison.Ordinal), "analyze run internal rule id");
            AssertEqual(true, content.Contains("IntelligenceX.Maintainability", StringComparison.Ordinal),
                "analyze run internal tool id");
            AssertEqual(true, hasMappedInternalRule, "analyze run internal rule/tool correlation");
            AssertEqual(true, content.Contains("LargeFile.cs", StringComparison.Ordinal), "analyze run internal file path");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFindingsUseCatalogToolMetadata() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-toolmeta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.CustomMaintainability",
  "toolRuleId": "IX-LOC-TOOL-001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-lines:700",
    "generated-marker:<auto-generated",
    "generated-marker:<autogenerated",
    "generated-marker:@generated",
    "generated-suffix:.designer.cs",
    "generated-suffix:.generated.cs",
    "generated-suffix:.g.cs",
    "generated-suffix:.g.i.cs"
  ]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var largeFile = Path.Combine(temp, "LargeFile.cs");
            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(largeFile, string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal metadata exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal metadata findings exists");
            var findingsJson = JsonLite.Parse(File.ReadAllText(findingsPath))?.AsObject();
            AssertNotNull(findingsJson, "analyze run internal metadata findings json");
            var findingsItems = findingsJson!.GetArray("items");
            AssertNotNull(findingsItems, "analyze run internal metadata findings items");

            var matched = false;
            foreach (var item in findingsItems!) {
                var finding = item.AsObject();
                if (finding is null) {
                    continue;
                }
                var path = finding.GetString("path");
                var ruleId = finding.GetString("ruleId");
                var tool = finding.GetString("tool");
                if (!string.Equals(path, "LargeFile.cs", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                AssertEqual("IX-LOC-TOOL-001", ruleId ?? string.Empty, "analyze run internal metadata rule id");
                AssertEqual("IntelligenceX.CustomMaintainability", tool ?? string.Empty, "analyze run internal metadata tool");
                matched = true;
                break;
            }
            AssertEqual(true, matched, "analyze run internal metadata finding");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFileSizeRuleDisabledBySeverity() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"],
    "severityOverrides": {
      "IXLOC001": "none"
    }
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-lines:700",
    "generated-marker:<auto-generated",
    "generated-marker:<autogenerated",
    "generated-marker:@generated",
    "generated-suffix:.designer.cs",
    "generated-suffix:.generated.cs",
    "generated-suffix:.g.cs",
    "generated-suffix:.g.i.cs"
  ]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var largeFile = Path.Combine(temp, "LargeFile.cs");
            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(largeFile, string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal severity none exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal severity none findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("IXLOC001", StringComparison.Ordinal), "analyze run internal severity none suppresses rule");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFileSizeRuleSkipsGeneratedAndExcluded() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));
            Directory.CreateDirectory(Path.Combine(temp, "OBJ"));
            Directory.CreateDirectory(Path.Combine(temp, "node_modules"));
            Directory.CreateDirectory(Path.Combine(temp, "obj-model"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-lines:700",
    "generated-marker:<auto-generated",
    "generated-marker:<autogenerated",
    "generated-marker:@generated",
    "generated-suffix:.designer.cs",
    "generated-suffix:.generated.cs",
    "generated-suffix:.g.cs",
    "generated-suffix:.g.i.cs"
  ]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(Path.Combine(temp, "Regular.cs"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "Generated.generated.cs"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "Upper.Designer.cs"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "HeaderGenerated.cs"),
                "// <AUTO-GENERATED>\n" + string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "OBJ", "Ignored.cs"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "node_modules", "Ignored.cs"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(temp, "obj-model", "StillIncluded.cs"), string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal skip exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal skip findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("Regular.cs", StringComparison.Ordinal), "analyze run internal skip regular file");
            AssertEqual(false, content.Contains("Generated.generated.cs", StringComparison.Ordinal),
                "analyze run internal skip generated suffix");
            AssertEqual(false, content.Contains("Upper.Designer.cs", StringComparison.Ordinal),
                "analyze run internal skip generated suffix case-insensitive");
            AssertEqual(false, content.Contains("HeaderGenerated.cs", StringComparison.OrdinalIgnoreCase),
                "analyze run internal skip generated header");
            AssertEqual(false, content.Contains("OBJ/Ignored.cs", StringComparison.OrdinalIgnoreCase),
                "analyze run internal skip excluded directory");
            AssertEqual(false, content.Contains("node_modules/Ignored.cs", StringComparison.OrdinalIgnoreCase),
                "analyze run internal skip node_modules");
            AssertEqual(true, content.Contains("obj-model/StillIncluded.cs", StringComparison.OrdinalIgnoreCase),
                "analyze run internal does not exclude directory substring");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestStructuredFindingsBlock() {
        var comments = new List<InlineReviewComment> {
            new("src/app.cs", 42, "Null check is missing."),
            new("", 0, "Snippet-only comment", "var x = 1;")
        };
        var block = ReviewFindingsBuilder.Build(comments);
        AssertEqual(true, block.Contains("<!-- intelligencex:findings -->", StringComparison.Ordinal), "findings marker");
        AssertEqual(true, block.Contains("\"path\":\"src/app.cs\"", StringComparison.Ordinal), "findings path");
        AssertEqual(true, block.Contains("\"line\":42", StringComparison.Ordinal), "findings line");
        AssertEqual(false, block.Contains("Snippet-only", StringComparison.Ordinal), "findings skips snippet-only");
    }

    private static void TestTrimPatchStopsAtHunkBoundary() {
        var patch = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a",
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        });
        var cutIndex = patch.IndexOf("@@ -10", StringComparison.Ordinal);
        var trimmed = CallTrimPatch(patch, cutIndex + 4);
        AssertEqual(false, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "tail hunk kept");
    }

    private static void TestTrimPatchKeepsTailHunk() {
        var newline = "\n";
        var headerLines = new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        };
        var hunk1Lines = new[] {
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        };
        var hunk2Lines = new[] {
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        };
        var hunk3Lines = new[] {
            "@@ -20,2 +20,2 @@",
            "-line20",
            "+line20a"
        };
        var header = string.Join(newline, headerLines);
        var hunk1 = string.Join(newline, hunk1Lines);
        var hunk2 = string.Join(newline, hunk2Lines);
        var hunk3 = string.Join(newline, hunk3Lines);
        var patch = string.Join(newline, headerLines)
                    + newline + hunk1
                    + newline + hunk2
                    + newline + hunk3;
        var marker = "... (truncated) ...";
        var maxChars = header.Length
                       + newline.Length + hunk1.Length
                       + newline.Length + marker.Length
                       + newline.Length + hunk3.Length;
        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(true, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk kept");
        AssertEqual(false, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "middle hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -20,2 +20,2 @@", StringComparison.Ordinal), "tail hunk kept");
    }
}
#endif
