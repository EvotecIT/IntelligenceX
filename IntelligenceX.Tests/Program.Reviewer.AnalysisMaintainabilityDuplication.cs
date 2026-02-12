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
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
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
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
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
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalDuplicationTokenizesJavaScript() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-js-tokenized-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should keep duplicated code below 15%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:15", "dup-window-lines:4", "include-ext:js"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.js"), BuildDuplicateJavaScriptSample("sumAlpha", "alphaInput"));
            File.WriteAllText(Path.Combine(temp, "file-b.js"), BuildDuplicateJavaScriptSample("sumBeta", "betaInput"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication tokenized javascript exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication tokenized javascript findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication tokenized javascript includes duplication finding");
            AssertEqual(true, content.Contains("file-a.js", StringComparison.Ordinal) ||
                              content.Contains("file-b.js", StringComparison.Ordinal),
                "analyze run duplication tokenized javascript finding path");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalDuplicationTokenizesPython() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-py-tokenized-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should keep duplicated code below 15%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:15", "dup-window-lines:4", "include-ext:py"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.py"), BuildDuplicatePythonSample("compute_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "file_b.py"), BuildDuplicatePythonSample("compute_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication tokenized python exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication tokenized python findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication tokenized python includes duplication finding");
            AssertEqual(true, content.Contains("file_a.py", StringComparison.Ordinal) ||
                              content.Contains("file_b.py", StringComparison.Ordinal),
                "analyze run duplication tokenized python finding path");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalMaintainabilityIncludeExtIsPerRule() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-maint-include-ext-per-rule-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should stay below 1 line",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-lines:1", "include-ext:cs"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 15%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:15", "dup-window-lines:4", "include-ext:py"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001", "IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "oversized.cs"), """
namespace Demo;
public class Oversized {
    public int Value => 1;
}
""");
            File.WriteAllText(Path.Combine(temp, "file_a.py"), BuildDuplicatePythonSample("compute_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "file_b.py"), BuildDuplicatePythonSample("compute_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run include-ext per-rule exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);

            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXLOC001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("oversized.cs", StringComparison.OrdinalIgnoreCase)),
                "analyze run include-ext per-rule has csharp max-lines finding");
            AssertEqual(false, findings.Any(item =>
                    item.RuleId.Equals("IXLOC001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)),
                "analyze run include-ext per-rule does not apply csharp max-lines to python");
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXDUP001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)),
                "analyze run include-ext per-rule applies duplication to python");
            AssertEqual(false, findings.Any(item =>
                    item.RuleId.Equals("IXDUP001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)),
                "analyze run include-ext per-rule does not apply python duplication to csharp");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThreshold() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should keep duplicated code below threshold",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:100", "max-duplication-percent-javascript:15", "dup-window-lines:4", "include-ext:js"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.js"), BuildDuplicateJavaScriptSample("sumAlpha", "alphaInput"));
            File.WriteAllText(Path.Combine(temp, "file-b.js"), BuildDuplicateJavaScriptSample("sumBeta", "betaInput"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXDUP001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)),
                "analyze run duplication language threshold uses javascript override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold emits per-file configured threshold");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static string BuildDuplicateSample(string className) {
        return $$"""
namespace Demo;
public class {{className}} {
    public int Calculate(int input) {
        var total = 0;
        total += input;
        total += 1;
        total += 2;
        total += 3;
        total += 4;
        total += 5;
        total += 6;
        total += 7;
        total += 8;
        return total;
    }
}
""";
    }

    private static string BuildDuplicateJavaScriptSample(string functionName, string inputName) {
        return $$"""
export function {{functionName}}({{inputName}}) {
  let total = 0;
  total += {{inputName}};
  total += 1;
  total += 2;
  total += 3;
  total += 4;
  total += 5;
  return total;
}
""";
    }

    private static string BuildDuplicatePythonSample(string functionName, string inputName) {
        return $$"""
def {{functionName}}({{inputName}}):
    total = 0
    total += {{inputName}}
    total += 1
    total += 2
    total += 3
    total += 4
    total += 5
    return total
""";
    }

    private static IReadOnlyList<(string RuleId, string Path)> ReadFindingsRulePathPairs(string findingsPath) {
        var content = File.ReadAllText(findingsPath);
        using var document = System.Text.Json.JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != System.Text.Json.JsonValueKind.Array) {
            return Array.Empty<(string RuleId, string Path)>();
        }

        var list = new List<(string RuleId, string Path)>();
        foreach (var item in items.EnumerateArray()) {
            var ruleId = item.TryGetProperty("ruleId", out var ruleIdValue) ? ruleIdValue.GetString() ?? string.Empty : string.Empty;
            var path = item.TryGetProperty("path", out var pathValue) ? pathValue.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(ruleId) && !string.IsNullOrWhiteSpace(path)) {
                list.Add((ruleId, path));
            }
        }

        return list;
    }

    private static (int ExitCode, string Output) RunAnalyzeDuplicationWithConsoleOutput(string workspace, string configPath,
        string outputPath) {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try {
            var exitCode = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", workspace,
                "--config", configPath,
                "--out", outputPath
            }).GetAwaiter().GetResult();
            return (exitCode, writer.ToString());
        } finally {
            Console.SetOut(originalOut);
        }
    }
}
#endif
