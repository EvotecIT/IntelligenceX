namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesShellAliasAndBashExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-bash-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:100", "max-duplication-percent-bash:15", "dup-window-lines:4", "include-ext:bash"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "build-a.bash"), BuildDuplicateShellSample("run_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "build-b.bash"), BuildDuplicateShellSample("run_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold bash exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".bash",
                "analyze run duplication language threshold bash uses shell alias override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold bash emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesShellAliasAndZshExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-zsh-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:100", "max-duplication-percent-zsh:15", "dup-window-lines:4", "include-ext:zsh"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "build-a.zsh"), BuildDuplicateShellSample("run_alpha", "input_alpha"));
            File.WriteAllText(Path.Combine(temp, "build-b.zsh"), BuildDuplicateShellSample("run_beta", "input_beta"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold zsh exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".zsh",
                "analyze run duplication language threshold zsh uses shell alias override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold zsh emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesYamlAliasAndYamlExtension() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-language-threshold-yaml-alias-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:100", "max-duplication-percent-yml:15", "dup-window-lines:4", "include-ext:yaml"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "config-a.yaml"), BuildDuplicateYamlSample("api_alpha", "demo/api:latest"));
            File.WriteAllText(Path.Combine(temp, "config-b.yaml"), BuildDuplicateYamlSample("api_beta", "demo/api:stable"));

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication language threshold yaml alias exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".yaml",
                "analyze run duplication language threshold yaml extension uses yml alias override");

            var metricsPath = Path.Combine(output, "intelligencex.duplication.json");
            var metricsContent = File.ReadAllText(metricsPath);
            AssertContainsText(metricsContent, "\"configuredMaxPercent\": 15",
                "analyze run duplication language threshold yaml alias emits per-file configured threshold");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationIgnoresShellShebangAndCommentOnlyLines() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-shell-comments-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:sh"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.sh"),
                "#!/usr/bin/env bash\n# shared-comment one\n# shared-comment two\necho \"alpha\"\n");
            File.WriteAllText(Path.Combine(temp, "file-b.sh"),
                "#!/usr/bin/env bash\n# shared-comment one\n# shared-comment two\nprintf \"beta\"\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication shell shebang comments exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertNoFinding(findings, "IXDUP001",
                "analyze run duplication shell shebang comments do not produce false positive");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationIgnoresYamlCommentOnlyLines() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-yaml-comments-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:yaml"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.yaml"),
                "# shared-comment one\n# shared-comment two\nservice: alpha\n");
            File.WriteAllText(Path.Combine(temp, "file-b.yaml"),
                "# shared-comment one\n# shared-comment two\nservice: beta\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication yaml comments exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertNoFinding(findings, "IXDUP001",
                "analyze run duplication yaml comments do not produce false positive");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalDuplicationShellHashInParameterExpansionDoesNotTriggerCommentStripping() {
        AssertShellHashContextDoesNotCauseFalseDuplication(
            "shell-param-expansion-hash",
            "local trimmed=\"${base#prefix}\" && echo ready",
            "local trimmed=\"${base#prefix}\" || echo ready");
    }

    private static void TestAnalyzeRunInternalDuplicationShellHashInDoublePrefixRemovalDoesNotTriggerCommentStripping() {
        AssertShellHashContextDoesNotCauseFalseDuplication(
            "shell-param-double-prefix-hash",
            "local trimmed=\"${base##prefix}\" && echo ready",
            "local trimmed=\"${base##prefix}\" || echo ready");
    }

    private static void TestAnalyzeRunInternalDuplicationShellHashInArithmeticDoesNotTriggerCommentStripping() {
        AssertShellHashContextDoesNotCauseFalseDuplication(
            "shell-arithmetic-hash",
            "local converted=$((16#FF + 1)) && echo ready",
            "local converted=$((16#FF + 1)) || echo ready");
    }

    private static void AssertShellHashContextDoesNotCauseFalseDuplication(string testCaseName, string lineA, string lineB) {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-dup-" + testCaseName + "-" + Guid.NewGuid().ToString("N"));
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
  "tags": ["max-duplication-percent:0", "dup-window-lines:2", "include-ext:sh"]
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "file-a.sh"), $"local base=\"$1\"\n{lineA}\n");
            File.WriteAllText(Path.Combine(temp, "file-b.sh"), $"local base=\"$1\"\n{lineB}\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run duplication shell hash context exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            var findings = ReadFindingsRulePathPairs(findingsPath);
            AssertNoFinding(findings, "IXDUP001",
                "analyze run duplication shell hash context does not produce false positive");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif
