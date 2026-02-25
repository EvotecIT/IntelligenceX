namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalDuplicationTokenizesTypeScriptModuleExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-ts-mts-tokenized-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:15", "dup-window-lines:4", "include-ext:mts"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.mts"), BuildDuplicateJavaScriptSample("sumAlpha", "alphaInput"));
            File.WriteAllText(Path.Combine(temp, "file-b.mts"), BuildDuplicateJavaScriptSample("sumBeta", "betaInput"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication tokenized mts exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication tokenized mts findings exists");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".mts",
                "analyze run duplication tokenized mts includes duplication finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationTokenizesPythonStubExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-py-pyi-tokenized-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:15", "dup-window-lines:4", "include-ext:pyi"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.pyi"), BuildDuplicatePythonSample("compute_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "file_b.pyi"), BuildDuplicatePythonSample("compute_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication tokenized pyi exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run duplication tokenized pyi findings exists");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".pyi",
                "analyze run duplication tokenized pyi includes duplication finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesTypeScriptModuleExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-mts-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:100", "max-duplication-percent-typescript:15", "dup-window-lines:4", "include-ext:mts"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.mts"), BuildDuplicateJavaScriptSample("sumAlpha", "alphaInput"));
            File.WriteAllText(Path.Combine(temp, "file-b.mts"), BuildDuplicateJavaScriptSample("sumBeta", "betaInput"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold mts exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".mts",
                "analyze run duplication language threshold mts uses typescript override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold mts emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesPythonStubExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-pyi-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:100", "max-duplication-percent-python:15", "dup-window-lines:4", "include-ext:pyi"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file_a.pyi"), BuildDuplicatePythonSample("compute_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "file_b.pyi"), BuildDuplicatePythonSample("compute_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold pyi exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".pyi",
                "analyze run duplication language threshold pyi uses python override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold pyi emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif
