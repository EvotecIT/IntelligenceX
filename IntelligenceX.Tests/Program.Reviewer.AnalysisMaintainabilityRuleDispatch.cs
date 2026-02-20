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
            AssertNoFinding(findings, "IXTOOL999",
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
                "max-lines:1",
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

            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasFinding(findings, "IXLOC001",
                "analyze run internal dispatch ambiguous first handler emits findings");

            using var metricsDocument = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(output, "intelligencex.duplication.json")));
            var duplicationRules = metricsDocument.RootElement.GetProperty("rules");
            AssertEqual(0, duplicationRules.GetArrayLength(),
                "analyze run internal dispatch ambiguous first handler does not emit duplication metrics");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaintainabilityResolvesCanonicalRuleIdRegistration() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-internal-dispatch-canonical-rule-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupInternalMaintainabilityDispatchWorkspace(temp, "IXTOOL005", new[] {
                "include-ext:cs"
            });

            var eventLogDir = Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.EventLog");
            Directory.CreateDirectory(eventLogDir);
            File.WriteAllText(Path.Combine(eventLogDir, "SampleDispatchEventLogTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.EventLog;

public sealed class SampleDispatchEventLogTool {
    public int Read(JsonObject? arguments) {
        return ResolveMaxResults(arguments);
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var result = RunAnalyzeWithConsoleOutput(temp, Path.Combine(temp, ".intelligencex", "reviewer.json"), output);

            AssertEqual(0, result.ExitCode, "analyze run internal dispatch canonical rule-id registration exit");
            AssertEqual(false, result.Output.Contains("no registered handler", StringComparison.OrdinalIgnoreCase),
                "analyze run internal dispatch canonical rule-id registration no unmapped warning");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasExactlyOneFinding(findings, "IXTOOL005",
                "IntelligenceX.Tools/IntelligenceX.Tools.EventLog/SampleDispatchEventLogTool.cs",
                "analyze run internal dispatch canonical rule-id registration finding");
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

        File.WriteAllText(Path.Combine(workspacePath, "Sample.cs"),
            "public static class Sample {\n" +
            "    public static int Value = 1;\n" +
            "}\n");
    }
}
#endif
