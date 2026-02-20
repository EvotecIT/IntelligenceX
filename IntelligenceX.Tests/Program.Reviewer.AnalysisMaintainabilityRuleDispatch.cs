namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalMaintainabilityWarnsOnUnmappedInternalRule() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-internal-dispatch-unmapped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupInternalMaintainabilityDispatchWorkspace(temp, "IXTOOL999", new[] {
                "include-ext:cs"
            });

            var output = Path.Combine(temp, "artifacts");
            var result = RunAnalyzeWithConsoleOutput(temp, Path.Combine(temp, ".intelligencex", "reviewer.json"), output);

            AssertEqual(0, result.ExitCode, "analyze run internal dispatch unmapped exit");
            AssertEqual(
                true,
                result.Output.Contains("IXTOOL999", StringComparison.OrdinalIgnoreCase) &&
                result.Output.Contains("no registered handler", StringComparison.OrdinalIgnoreCase),
                "analyze run internal dispatch unmapped warning");

            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL999", StringComparison.OrdinalIgnoreCase)),
                "analyze run internal dispatch unmapped no findings");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaintainabilityWarnsOnAmbiguousInternalRuleMatch() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-internal-dispatch-ambiguous-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupInternalMaintainabilityDispatchWorkspace(temp, "IXLOC001", new[] {
                "max-lines:700",
                "dup-window-lines:8",
                "include-ext:cs"
            });

            var output = Path.Combine(temp, "artifacts");
            var result = RunAnalyzeWithConsoleOutput(temp, Path.Combine(temp, ".intelligencex", "reviewer.json"), output);

            AssertEqual(0, result.ExitCode, "analyze run internal dispatch ambiguous exit");
            AssertEqual(
                true,
                result.Output.Contains("IXLOC001", StringComparison.OrdinalIgnoreCase) &&
                result.Output.Contains("matched multiple handlers", StringComparison.OrdinalIgnoreCase),
                "analyze run internal dispatch ambiguous warning");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void SetupInternalMaintainabilityDispatchWorkspace(string workspacePath, string ruleId,
        IReadOnlyList<string> tags) {
        Directory.CreateDirectory(Path.Combine(workspacePath, ".intelligencex"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "Analysis", "Catalog", "rules", "internal"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "Analysis", "Packs"));

        File.WriteAllText(Path.Combine(workspacePath, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

        var tagsJson = string.Join(", ", (tags ?? Array.Empty<string>()).Select(static tag => $"\"{tag}\""));
        File.WriteAllText(Path.Combine(workspacePath, "Analysis", "Catalog", "rules", "internal", $"{ruleId}.json"), $$"""
{
  "id": "{{ruleId}}",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "{{ruleId}}",
  "title": "Internal dispatch test rule",
  "description": "Verifies internal maintainability dispatch behavior.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [{{tagsJson}}]
}
""");

        File.WriteAllText(Path.Combine(workspacePath, "Analysis", "Packs", "intelligencex-maintainability-default.json"),
            $$"""
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["{{ruleId}}"]
}
""");

        File.WriteAllText(Path.Combine(workspacePath, "Sample.cs"), "public static class Sample { }\n");
    }
}
#endif
