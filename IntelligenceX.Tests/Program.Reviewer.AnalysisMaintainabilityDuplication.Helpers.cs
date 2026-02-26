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

    private static string BuildDuplicateShellSample(string functionName, string inputName) {
        return $$"""
{{functionName}}() {
  local {{inputName}}="$1"
  local total=0
  total=$((total + {{inputName}}))
  total=$((total + 1))
  total=$((total + 2))
  total=$((total + 3))
  total=$((total + 4))
  echo "$total"
}
""";
    }

    private static string BuildDuplicateYamlSample(string serviceName, string imageName) {
        return $$"""
services:
  {{serviceName}}:
    image: {{imageName}}
    restart: always
    environment:
      - LOG_LEVEL=info
      - RETRIES=3
    ports:
      - "8080:80"
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
