namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationIgnoresJavaScriptImports() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-js-imports-" + Guid.NewGuid().ToString("N"));
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

            // Keep the window small so imports would have caused duplication if considered significant.
            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 0%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:js"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.js"), """
import x from "lib";
import y from "lib2";
const alpha = 1;
return alpha + 1;
""");
            File.WriteAllText(Path.Combine(temp, "file-b.js"), """
import x from "lib";
import y from "lib2";
let beta = 2;
return beta - 2;
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication ignores js imports exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication ignores js imports findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication ignores js imports no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationIgnoresPowerShellUsingStatements() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-ps-using-" + Guid.NewGuid().ToString("N"));
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

            // Keep the window small so using statements would have caused duplication if considered significant.
            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXDUP001.json"), """
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Source files should keep duplicated code below 0%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:ps1"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.ps1"), """
using module Foo
using namespace Bar
return 1
break
""");
            File.WriteAllText(Path.Combine(temp, "file_b.ps1"), """
using module Foo
using namespace Bar
throw "x"
continue
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication ignores powershell using exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication ignores powershell using findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication ignores powershell using no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
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
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationIgnoresPythonImports() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-py-imports-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should keep duplicated code below 0%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:py"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.py"), """
import os
from math import sqrt
x += 1
assert x
""");
            File.WriteAllText(Path.Combine(temp, "file_b.py"), """
import os
from math import sqrt
x -= 1
return x
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication ignores python imports exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication ignores python imports findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("\"ruleId\": \"IXDUP001\"", StringComparison.Ordinal),
                "analyze run duplication ignores python imports no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationPythonTripleQuoteCommentHandling() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-py-triple-comment-" + Guid.NewGuid().ToString("N"));
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
  "title": "Source files should keep duplicated code below 10%",
  "description": "Flags files with high duplication percentages.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": ["max-duplication-percent:10", "dup-window-lines:3", "include-ext:py"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.py"), BuildPythonTripleQuoteHashSample("and"));
            File.WriteAllText(Path.Combine(temp, "file_b.py"), BuildPythonTripleQuoteHashSample("or"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication python triple-quote hash handling exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication python triple-quote hash findings exists");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertNoFinding(findings, "IXDUP001",
                "analyze run duplication python triple-quote hash does not produce false positive");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
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

            AssertHasFinding(findings, "IXLOC001", "oversized.cs",
                "analyze run include-ext per-rule has csharp max-lines finding");
            AssertNoFindingWithPathSuffix(findings, "IXLOC001", ".py",
                "analyze run include-ext per-rule does not apply csharp max-lines to python");
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".py",
                "analyze run include-ext per-rule applies duplication to python");
            AssertNoFindingWithPathSuffix(findings, "IXDUP001", ".cs",
                "analyze run include-ext per-rule does not apply python duplication to csharp");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
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
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".js",
                "analyze run duplication language threshold uses javascript override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificTagOnlyActivatesRule() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-only-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent-javascript:15", "include-ext:js"]
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

            AssertEqual(0, exit, "analyze run duplication language-only tag exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".js",
                "analyze run duplication language-only tag activates duplication rule");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language-only tag uses language threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif
