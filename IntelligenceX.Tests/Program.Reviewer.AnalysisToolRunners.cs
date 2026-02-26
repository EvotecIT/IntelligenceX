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
        AssertContainsText(string.Join(" ", args), "--ext .js,.jsx,.mjs,.cjs,.ts,.tsx,.mts,.cts",
            "javascript args include modern TypeScript module extensions");
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

    private static void TestAnalyzeRunProcessExceptionClassificationIncludesExpectedExceptions() {
        AssertEqual(true,
            IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsExpectedProcessExecutionExceptionForTests(
                new InvalidOperationException("test")),
            "process exception classifier includes invalid operation");
        AssertEqual(true,
            IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsExpectedProcessExecutionExceptionForTests(
                new IOException("test")),
            "process exception classifier includes io exception");
        AssertEqual(true,
            IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsExpectedProcessExecutionExceptionForTests(
                new UnauthorizedAccessException("test")),
            "process exception classifier includes unauthorized access");
    }

    private static void TestAnalyzeRunProcessExceptionClassificationExcludesUnexpectedExceptions() {
        AssertEqual(false,
            IntelligenceX.Cli.Analysis.AnalyzeRunCommand.IsExpectedProcessExecutionExceptionForTests(
                new NullReferenceException("test")),
            "process exception classifier excludes unexpected exceptions");
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

    private static void TestAnalyzeRunExternalFailureMessageRecognizesToolSpecificUnavailableMarkers() {
        var unavailableByToolSpecificMarker = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
            languageLabel: "Python",
            command: "ruff",
            optionName: "--ruff-command",
            exitCode: 2,
            stdOut: string.Empty,
            stdErr: "Traceback (most recent call last): ModuleNotFoundError: No module named ruff");
        AssertContainsText(unavailableByToolSpecificMarker, "analysis command 'ruff' is unavailable",
            "external runner unavailable message recognizes ruff-specific missing-module marker");

        var genericForDifferentTool = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
            languageLabel: "JavaScript/TypeScript",
            command: "npx",
            optionName: "--npx-command",
            exitCode: 2,
            stdOut: string.Empty,
            stdErr: "Traceback (most recent call last): ModuleNotFoundError: No module named ruff");
        AssertContainsText(genericForDifferentTool, "analysis returned exit code 2",
            "external runner tool-specific marker does not leak to unrelated command");
    }

    private static void TestAnalyzeRunExternalFailureMessageSupportsConfiguredUnavailableMarkers() {
        const string globalEnv = "INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS";
        const string npxEnv = "INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS_NPX";
        var previousGlobal = Environment.GetEnvironmentVariable(globalEnv);
        var previousNpx = Environment.GetEnvironmentVariable(npxEnv);
        try {
            Environment.SetEnvironmentVariable(globalEnv, "missing-custom-executable");
            Environment.SetEnvironmentVariable(npxEnv, "npx-custom-missing");

            var globalConfiguredMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "Python",
                command: "ruff",
                optionName: "--ruff-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "ERROR: missing-custom-executable");
            AssertContainsText(globalConfiguredMessage, "analysis command 'ruff' is unavailable",
                "external runner global configured marker classifies unavailable commands");

            var toolConfiguredMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "JavaScript/TypeScript",
                command: "npx",
                optionName: "--npx-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "npx-custom-missing");
            AssertContainsText(toolConfiguredMessage, "analysis command 'npx' is unavailable",
                "external runner tool-specific configured marker classifies unavailable commands");

            var unrelatedToolMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "Python",
                command: "ruff",
                optionName: "--ruff-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "npx-custom-missing");
            AssertContainsText(unrelatedToolMessage, "analysis returned exit code 2",
                "external runner tool-specific configured marker does not leak across tools");
        } finally {
            Environment.SetEnvironmentVariable(globalEnv, previousGlobal);
            Environment.SetEnvironmentVariable(npxEnv, previousNpx);
        }
    }

    private static void TestAnalyzeRunExternalFailureMessageConfiguredMarkersRefreshOnEnvironmentChange() {
        const string globalEnv = "INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS";
        var previousGlobal = Environment.GetEnvironmentVariable(globalEnv);
        try {
            Environment.SetEnvironmentVariable(globalEnv, "missing-marker-one");
            var firstMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "Python",
                command: "ruff",
                optionName: "--ruff-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "missing-marker-one");
            AssertContainsText(firstMessage, "analysis command 'ruff' is unavailable",
                "external runner configured markers classify first env marker");

            Environment.SetEnvironmentVariable(globalEnv, "missing-marker-two");
            var staleMarkerMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "Python",
                command: "ruff",
                optionName: "--ruff-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "missing-marker-one");
            AssertContainsText(staleMarkerMessage, "analysis returned exit code 2",
                "external runner marker cache refresh drops stale env marker");

            var updatedMarkerMessage = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildExternalRunnerFailureMessageForTests(
                languageLabel: "Python",
                command: "ruff",
                optionName: "--ruff-command",
                exitCode: 2,
                stdOut: string.Empty,
                stdErr: "missing-marker-two");
            AssertContainsText(updatedMarkerMessage, "analysis command 'ruff' is unavailable",
                "external runner marker cache refresh picks up updated env marker");
        } finally {
            Environment.SetEnvironmentVariable(globalEnv, previousGlobal);
        }
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
            File.WriteAllText(Path.Combine(src, "main.pyi"), "def answer() -> int: ...");

            var result = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.WorkspaceContainsAnySourceFileWithDiagnosticsForTests(
                workspace,
                ".pyi");
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

    private static void TestAnalyzeRunWorkspaceSourceDetectionFindsPowerShellModuleFiles() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-detect-ps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var modulePath = Path.Combine(workspace, "Module");
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(Path.Combine(modulePath, "TestModule.psm1"), "function Get-Answer { 42 }");

            var found = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.WorkspaceContainsAnySourceFileForTests(
                workspace,
                ".ps1",
                ".psm1",
                ".psd1");
            AssertEqual(true, found, "source detection finds PowerShell module files");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunWorkspaceSourceInventoryKeepsTrackedExtensionsOnly() {
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-filtered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.ts"), "export const answer = 42;");
            File.WriteAllText(Path.Combine(src, "deploy.yaml"), "name: test\n");
            File.WriteAllText(Path.Combine(src, "notes.randomext"), "ignored");

            var inventory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.DiscoverWorkspaceSourceInventoryForTests(workspace);
            AssertEqual(0, inventory.SkippedEnumerations, "source inventory tracked-only diagnostics has zero skipped paths");

            var hasTypeScript = false;
            var hasYaml = false;
            var hasRandomExtension = false;
            foreach (var extension in inventory.Extensions) {
                if (string.Equals(extension, ".ts", StringComparison.OrdinalIgnoreCase)) {
                    hasTypeScript = true;
                } else if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)) {
                    hasYaml = true;
                } else if (string.Equals(extension, ".randomext", StringComparison.OrdinalIgnoreCase)) {
                    hasRandomExtension = true;
                }
            }

            AssertEqual(true, hasTypeScript, "source inventory retains tracked extension");
            AssertEqual(true, hasYaml, "source inventory retains tracked yaml extension");
            AssertEqual(false, hasRandomExtension, "source inventory ignores untracked extensions");
        } finally {
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunSharedSourceInventoryFallsBackWhenScanLimitReached() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.ts"), "export const answer = 42;");

            var inventory = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.DiscoverWorkspaceSourceInventoryForTests(workspace);
            AssertEqual(true, inventory.ScanLimitReached, "source inventory reports scan-limit reached when max files is zero");
            AssertEqual(0, inventory.MaxScannedFiles, "source inventory reports configured max files");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "JavaScript/TypeScript",
                ".ts");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds source files when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory uses direct fallback when scan limit is reached");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct JavaScript/TypeScript source detection.",
                "shared source inventory fallback emits scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsPowerShellSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-ps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var scripts = Path.Combine(workspace, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "tool.ps1"), "Write-Host 'ok'");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "PowerShell",
                ".ps1",
                ".psm1",
                ".psd1");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds PowerShell sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for PowerShell");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct PowerShell source detection.",
                "shared source inventory fallback emits PowerShell scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsCsharpSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-cs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var src = Path.Combine(workspace, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "Program.cs"), "public static class Program { }");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "C#",
                ".cs");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds csharp sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for csharp");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct C# source detection.",
                "shared source inventory fallback emits csharp scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
            try {
                Directory.Delete(workspace, recursive: true);
            } catch {
                // Best-effort cleanup for temp harness directories.
            }
        }
    }

    private static void TestAnalyzeRunSharedSourceInventoryFallbackDetectsShellSources() {
        const string maxFilesEnv = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
        var previousValue = Environment.GetEnvironmentVariable(maxFilesEnv);
        var workspace = Path.Combine(Path.GetTempPath(), "ix-source-inventory-fallback-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try {
            Environment.SetEnvironmentVariable(maxFilesEnv, "0");
            var scripts = Path.Combine(workspace, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "build.sh"), "echo ok");

            var fallbackResult = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.TryDetectSourceFilesWithSharedInventoryForTests(
                workspace,
                "Shell",
                ".sh",
                ".bash",
                ".zsh");
            AssertEqual(true, fallbackResult.Found, "shared source inventory fallback finds shell sources when scan limit is reached");
            AssertEqual(true, fallbackResult.UsedDirectFallback, "shared source inventory fallback uses direct detection for shell");
            AssertContainsText(string.Join("\n", fallbackResult.Warnings),
                "Shared source inventory reached the configured file limit (0); falling back to direct Shell source detection.",
                "shared source inventory fallback emits shell scan-limit warning");
        } finally {
            Environment.SetEnvironmentVariable(maxFilesEnv, previousValue);
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
