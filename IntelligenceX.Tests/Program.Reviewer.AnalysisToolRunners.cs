namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunPowerShellScriptCapturesEngineErrors() {
        var script = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPowerShellRunnerScriptForTests();

        AssertContainsText(script, "[switch]$FailOnAnalyzerErrors", "powershell runner strict switch parameter");
        AssertContainsText(script, "[string]$ExcludedDirectoriesCsv", "powershell runner excluded directories parameter");
        AssertContainsText(script, "Workspace path not found", "powershell runner validates missing workspace");
        AssertContainsText(script, "Workspace path is not a directory", "powershell runner validates workspace directory type");
        AssertContainsText(script, "Get-AnalyzerPaths", "powershell runner pre-enumerates script paths");
        AssertContainsText(script, "[System.IO.FileAttributes]::ReparsePoint", "powershell runner skips reparse-point directories");
        AssertContainsText(script, "File]::GetAttributes($subdirectory)", "powershell runner checks directory attributes for reparse points");
        AssertContainsText(script, "GetAttributes($file)", "powershell runner checks file-level reparse points");
        AssertContainsText(script, "[System.StringComparison]::OrdinalIgnoreCase", "powershell runner extension filtering is case-insensitive");
        AssertContainsText(script, "System.Collections.Generic.List[object]", "powershell runner uses typed list result aggregation");
        AssertContainsText(script, "catch [System.UnauthorizedAccessException]", "powershell runner handles expected access exceptions");
        AssertContainsText(script, "catch [System.IO.IOException]", "powershell runner handles expected io exceptions");
        var compactScript = string.Join(" ", script.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        AssertContainsText(script,
            "foreach ($file in [System.IO.Directory]::EnumerateFiles($current))",
            "powershell runner enumerates files");
        AssertContainsText(compactScript,
            "catch [System.IO.IOException] { continue } try { foreach ($file in [System.IO.Directory]::EnumerateFiles($current))",
            "powershell runner handles io exceptions in directory enumeration");
        AssertContainsText(script, "if ($analysisPaths.Length -gt 0)", "powershell runner handles empty path list");
        AssertContainsText(script, "foreach ($analysisPath in $analysisPaths)", "powershell runner iterates filtered paths");
        AssertContainsText(script, "Invoke-ScriptAnalyzer -Path $analysisPath", "powershell runner invokes analyzer per path");
        AssertContainsText(script, "-ErrorAction Continue -ErrorVariable +localErrors",
            "powershell runner captures non-terminating engine errors per file");
        AssertContainsText(script, "foreach ($err in $localErrors)",
            "powershell runner aggregates engine errors across files");
        AssertContainsText(script, "if ($sawInvokeErrors -and $FailOnAnalyzerErrors)",
            "powershell runner strict-mode failure gate");
        AssertContainsText(script, "exit 2", "powershell runner strict-mode engine error exit code");
    }

    private static void TestAnalyzeRunPowerShellStrictArgsIncludeFailSwitch() {
        var strictArgs = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPowerShellRunnerArgsForTests(
            tempScript: "runner.ps1",
            workspace: "workspace",
            findingsPath: "findings.json",
            settingsPath: "PSScriptAnalyzerSettings.psd1",
            strict: true);
        var nonStrictArgs = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPowerShellRunnerArgsForTests(
            tempScript: "runner.ps1",
            workspace: "workspace",
            findingsPath: "findings.json",
            settingsPath: "PSScriptAnalyzerSettings.psd1",
            strict: false);

        var strictHasSwitch = false;
        foreach (var arg in strictArgs) {
            if (string.Equals(arg, "-FailOnAnalyzerErrors", StringComparison.Ordinal)) {
                strictHasSwitch = true;
                break;
            }
        }

        var nonStrictHasSwitch = false;
        foreach (var arg in nonStrictArgs) {
            if (string.Equals(arg, "-FailOnAnalyzerErrors", StringComparison.Ordinal)) {
                nonStrictHasSwitch = true;
                break;
            }
        }

        AssertEqual(true, strictHasSwitch, "powershell strict args include fail switch");
        AssertEqual(false, nonStrictHasSwitch, "powershell non-strict args omit fail switch");

        string? strictExcludedDirectories = null;
        for (var i = 0; i + 1 < strictArgs.Count; i++) {
            if (string.Equals(strictArgs[i], "-ExcludedDirectoriesCsv", StringComparison.Ordinal)) {
                strictExcludedDirectories = strictArgs[i + 1];
                break;
            }
        }

        AssertEqual(false, string.IsNullOrWhiteSpace(strictExcludedDirectories),
            "powershell strict args include excluded directories csv");
        AssertContainsText(strictExcludedDirectories ?? string.Empty, ".worktrees",
            "powershell strict args include .worktrees exclusion");
        AssertEqual(".git,.vs,.worktrees,bin,node_modules,obj", strictExcludedDirectories ?? string.Empty,
            "powershell strict args excluded directories order is deterministic");
    }

    private static void TestAnalyzeRunJavaScriptArgsIncludeConfiguredRules() {
        var args = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildJavaScriptRunnerArgsForTests(
            sarifPath: "artifacts/intelligencex.eslint.sarif",
            severityByToolRuleId: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["no-eval"] = "error",
                ["no-unused-vars"] = "warn"
            });

        AssertContainsText(string.Join(" ", args), "--yes eslint .", "javascript args run eslint via npx");
        AssertContainsText(string.Join(" ", args), "--format sarif", "javascript args use sarif output");
        AssertContainsText(string.Join(" ", args), "--output-file artifacts/intelligencex.eslint.sarif",
            "javascript args set output file");
        AssertContainsText(string.Join(" ", args), "--rule no-eval:error", "javascript args include no-eval severity");
        AssertContainsText(string.Join(" ", args), "--rule no-unused-vars:warn",
            "javascript args include no-unused-vars severity");
    }

    private static void TestAnalyzeRunPythonArgsIncludeSelectRuleIds() {
        var args = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPythonRunnerArgsForTests(new[] {
            "F821",
            "S602"
        });

        var joined = string.Join(" ", args);
        AssertContainsText(joined, "check . --output-format sarif", "python args run ruff sarif output");
        AssertContainsText(joined, "--select F821,S602", "python args include selected rule ids");
    }

    private static void TestAnalyzeRunPythonArgsIncludeOutputFileWhenConfigured() {
        var args = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPythonRunnerArgsWithOutputForTests(
            "artifacts/intelligencex.ruff.sarif",
            new[] { "F821" });

        var joined = string.Join(" ", args);
        AssertContainsText(joined, "--output-file artifacts/intelligencex.ruff.sarif",
            "python args can request explicit output file");
        AssertContainsText(joined, "--select F821", "python args preserve select when output file enabled");
    }

    private static void TestAnalyzeRunPythonOutputFileFallbackDetection() {
        var unsupported = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsUnsupportedRuffOutputFileOptionForTests(
            2,
            string.Empty,
            "error: unexpected argument '--output-file' found");
        var otherError = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsUnsupportedRuffOutputFileOptionForTests(
            2,
            string.Empty,
            "error: some other failure");

        AssertEqual(true, unsupported, "python output-file fallback detection matches unsupported-option errors");
        AssertEqual(false, otherError, "python output-file fallback detection ignores unrelated errors");
    }

    private static void TestAnalyzeRunExternalFailureMessageClassifiesMissingCommand() {
        var unavailableMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
            languageLabel: "Python",
            command: "ruff",
            optionName: "--ruff-command",
            exitCode: 127,
            stdOut: string.Empty,
            stdErr: "command not found");
        AssertContainsText(unavailableMessage, "analysis command 'ruff' is unavailable",
            "external runner unavailable message includes command guidance");
        AssertContainsText(unavailableMessage, "--ruff-command",
            "external runner unavailable message includes override option guidance");

        var unavailableByTextMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
            languageLabel: "Python",
            command: "ruff",
            optionName: "--ruff-command",
            exitCode: 2,
            stdOut: string.Empty,
            stdErr: "'ruff' is not recognized as an internal or external command");
        AssertContainsText(unavailableByTextMessage, "analysis command 'ruff' is unavailable",
            "external runner unavailable message recognizes command-not-found text heuristics");

        var genericFailureMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
            languageLabel: "Python",
            command: "ruff",
            optionName: "--ruff-command",
            exitCode: 2,
            stdOut: string.Empty,
            stdErr: "some analyzer failure");
        AssertContainsText(genericFailureMessage, "analysis returned exit code 2",
            "external runner generic failure message retains exit code");
    }

    private static void TestAnalyzeRunWorkspaceSourceDetectionSkipsExcludedDirectories() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var nodeModules = Path.Combine(workspace, "node_modules");
            Directory.CreateDirectory(nodeModules);
            File.WriteAllText(Path.Combine(nodeModules, "ignore.js"), "console.log('ignored');");

            var foundInExcludedDirectory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.WorkspaceContainsAnySourceFileForTests(
                workspace,
                ".js");
            AssertEqual(false, foundInExcludedDirectory,
                "source detection ignores excluded directories");

            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.ts"), "const answer = 42;");

            var foundInIncludedDirectory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.WorkspaceContainsAnySourceFileForTests(
                workspace,
                ".ts");
            AssertEqual(true, foundInIncludedDirectory,
                "source detection finds matching files in included directories");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunWorkspaceSourceDetectionDiagnosticsDefaultToZeroSkipped() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-detect-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.py"), "answer = 42");

            var result = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.WorkspaceContainsAnySourceFileWithDiagnosticsForTests(
                workspace,
                ".py");
            AssertEqual(true, result.Found, "source detection diagnostics reports found status");
            AssertEqual(0, result.SkippedEnumerations, "source detection diagnostics has zero skipped paths in healthy workspace");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunWorkspaceSourceInventoryCapturesMultipleExtensions() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.ts"), "const answer = 42;");

            var scripts = Path.Combine(workspace, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "tool.py"), "answer = 42");

            var inventory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.DiscoverWorkspaceSourceInventoryForTests(workspace);
            AssertEqual(0, inventory.SkippedEnumerations, "source inventory diagnostics has zero skipped paths in healthy workspace");

            var hasTypeScript = false;
            var hasPython = false;
            foreach (var extension in inventory.Extensions) {
                if (string.Equals(extension, ".ts", StringComparison.OrdinalIgnoreCase)) {
                    hasTypeScript = true;
                } else if (string.Equals(extension, ".py", StringComparison.OrdinalIgnoreCase)) {
                    hasPython = true;
                }
            }

            AssertEqual(true, hasTypeScript, "source inventory captures TypeScript extension");
            AssertEqual(true, hasPython, "source inventory captures Python extension");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunJavaScriptSelectorsIgnoreMismatchedTools() {
        var rules = new[] {
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXJS100",
                    language: "javascript",
                    tool: "eslint",
                    toolRuleId: "no-alert",
                    title: "no alert",
                    description: "desc",
                    category: "Best Practices",
                    defaultSeverity: "warning",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "warning"),
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXJS999",
                    language: "javascript",
                    tool: "internal",
                    toolRuleId: "IXLOC001",
                    title: "internal rule",
                    description: "desc",
                    category: "Maintainability",
                    defaultSeverity: "error",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "error"),
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXJS101",
                    language: "javascript",
                    tool: "eslint",
                    toolRuleId: "no-alert",
                    title: "no alert duplicate",
                    description: "desc",
                    category: "Best Practices",
                    defaultSeverity: "warning",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "critical")
        };

        var selectors = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildJavaScriptRuleSelectorsForTests(rules);
        AssertEqual(1, selectors.Count, "javascript selector count ignores mismatched tools");
        AssertEqual("error", selectors["no-alert"], "javascript selector keeps highest mapped severity");
        AssertEqual(false, selectors.ContainsKey("IXLOC001"), "javascript selector excludes non-eslint tool ids");
    }

    private static void TestAnalyzeRunPythonSelectedRuleIdsIgnoreMismatchedTools() {
        var rules = new[] {
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXPY100",
                    language: "python",
                    tool: "ruff",
                    toolRuleId: "F401",
                    title: "unused import",
                    description: "desc",
                    category: "F",
                    defaultSeverity: "warning",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "warning"),
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXPY999",
                    language: "python",
                    tool: "internal",
                    toolRuleId: "IXLOC001",
                    title: "internal rule",
                    description: "desc",
                    category: "Maintainability",
                    defaultSeverity: "error",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "error"),
            new IntelligenceX.Analysis.AnalysisPolicyRule(
                new IntelligenceX.Analysis.AnalysisRule(
                    id: "IXPY101",
                    language: "python",
                    tool: "ruff",
                    toolRuleId: "F821",
                    title: "undefined name",
                    description: "desc",
                    category: "F",
                    defaultSeverity: "warning",
                    tags: Array.Empty<string>(),
                    docs: null,
                    sourcePath: null),
                severity: "none")
        };

        var selectedRuleIds = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPythonSelectedRuleIdsForTests(rules);
        AssertEqual(1, selectedRuleIds.Count, "python selected rule ids ignore mismatched tools and disabled rules");
        AssertEqual("F401", selectedRuleIds[0], "python selected rule ids keep only ruff-enabled rules");
    }
}
#endif
