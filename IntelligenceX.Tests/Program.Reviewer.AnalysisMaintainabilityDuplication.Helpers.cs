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

