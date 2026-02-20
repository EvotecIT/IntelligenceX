namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
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

    private static string BuildPythonTripleQuoteHashSample(string trailingKeyword) {
        return
            "def compute(input_value):\n" +
            "    \"\"\"\n" +
            $"    shared # {trailingKeyword} {trailingKeyword} {trailingKeyword}\n" +
            "    \"\"\"\n" +
            "    return input_value + 1\n";
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

    private static int CountFindings(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string? path = null) {
        if (findings is null || findings.Count == 0 || string.IsNullOrWhiteSpace(ruleId)) {
            return 0;
        }

        var hasPath = !string.IsNullOrWhiteSpace(path);
        return findings.Count(item =>
            item.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            (!hasPath || item.Path.Equals(path!, StringComparison.OrdinalIgnoreCase)));
    }

    private static int CountFindingsByPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix) {
        if (findings is null || findings.Count == 0 || string.IsNullOrWhiteSpace(ruleId) ||
            string.IsNullOrWhiteSpace(pathSuffix)) {
            return 0;
        }

        return findings.Count(item =>
            item.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            item.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertHasFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string assertionMessage) {
        AssertEqual(true, CountFindings(findings, ruleId) > 0, assertionMessage);
    }

    private static void AssertHasFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId, string path,
        string assertionMessage) {
        AssertEqual(true, CountFindings(findings, ruleId, path) > 0, assertionMessage);
    }

    private static void AssertHasExactlyOneFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string path, string assertionMessage) {
        AssertEqual(1, CountFindings(findings, ruleId, path), assertionMessage);
    }

    private static void AssertNoFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string assertionMessage) {
        AssertEqual(0, CountFindings(findings, ruleId), assertionMessage);
    }

    private static void AssertNoFinding(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId, string path,
        string assertionMessage) {
        AssertEqual(0, CountFindings(findings, ruleId, path), assertionMessage);
    }

    private static void AssertHasFindingWithPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix, string assertionMessage) {
        AssertEqual(true, CountFindingsByPathSuffix(findings, ruleId, pathSuffix) > 0, assertionMessage);
    }

    private static void AssertNoFindingWithPathSuffix(IReadOnlyList<(string RuleId, string Path)> findings, string ruleId,
        string pathSuffix, string assertionMessage) {
        AssertEqual(0, CountFindingsByPathSuffix(findings, ruleId, pathSuffix), assertionMessage);
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
