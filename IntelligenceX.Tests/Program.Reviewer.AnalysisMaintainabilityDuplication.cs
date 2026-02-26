namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalMaintainabilitySupportsMultipleRules() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-maint-multi-rule-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should stay below 10 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-lines:10"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 30%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:30", "dup-window-lines:5"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001", "IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "FileA.cs"), BuildDuplicateSample("FileA"));
            File.WriteAllText(Path.Combine(temp, "FileB.cs"), BuildDuplicateSample("FileB"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal multi-rule exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal multi-rule findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("\"ruleId\": \"IXLOC001\"", StringComparison.Ordinal),
                "analyze run internal multi-rule includes max-lines");
            AssertEqual(true, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run internal multi-rule includes duplication");
            AssertEqual(true, content.Contains("Duplicated significant lines", StringComparison.Ordinal),
                "analyze run internal multi-rule includes duplication message");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationRuleRespectsThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-threshold-" + Guid.NewGuid().ToString("N"));
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

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 100%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:100", "dup-window-lines:5"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "FileA.cs"), BuildDuplicateSample("FileA"));
            File.WriteAllText(Path.Combine(temp, "FileB.cs"), BuildDuplicateSample("FileB"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication threshold exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication threshold findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication threshold suppresses below-limit findings");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationRuleWarnsOnMalformedTags() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-malformed-tags-" + Guid.NewGuid().ToString("N"));
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

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 25%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:not-a-number", "dup-window-lines:1"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "FileA.cs"), BuildDuplicateSample("FileA"));
            File.WriteAllText(Path.Combine(temp, "FileB.cs"), BuildDuplicateSample("FileB"));

            var output = Path.Combine(temp, "artifacts");
            var result = RunAnalyzeDuplicationWithConsoleOutput(temp, Path.Combine(temp, ".intelligencex", "reviewer.json"), output);

            AssertEqual(0, result.ExitCode, "analyze run duplication malformed tags exit");
            AssertEqual(true,
                result.Output.Contains("malformed tag 'max-duplication-percent:not-a-number'",
                    StringComparison.OrdinalIgnoreCase),
                "analyze run duplication malformed max percent warning");
            AssertEqual(true,
                result.Output.Contains("malformed tag 'dup-window-lines:1'", StringComparison.OrdinalIgnoreCase),
                "analyze run duplication malformed window warning");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationRuleWarnsOnUnsupportedLanguageAliasList() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-unsupported-language-" + Guid.NewGuid().ToString("N"));
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

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 25%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent-ruby:10", "dup-window-lines:5"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "FileA.cs"), BuildDuplicateSample("FileA"));
            File.WriteAllText(Path.Combine(temp, "FileB.cs"), BuildDuplicateSample("FileB"));

            var output = Path.Combine(temp, "artifacts");
            var result = RunAnalyzeDuplicationWithConsoleOutput(temp, Path.Combine(temp, ".intelligencex", "reviewer.json"), output);

            AssertEqual(0, result.ExitCode, "analyze run duplication unsupported language tag exit");
            AssertEqual(true,
                result.Output.Contains("unsupported duplication language", StringComparison.OrdinalIgnoreCase),
                "analyze run duplication unsupported language warning");
            AssertEqual(true,
                result.Output.Contains("aliases: cs, ps, js, ts, py, sh, bash, zsh, yml", StringComparison.OrdinalIgnoreCase),
                "analyze run duplication unsupported language warning lists aliases");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

}
#endif
