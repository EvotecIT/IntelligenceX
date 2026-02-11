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
        AssertContainsText(script, "catch [System.UnauthorizedAccessException]", "powershell runner handles expected access exceptions");
        AssertContainsText(script, "catch [System.IO.IOException]", "powershell runner handles expected io exceptions");
        AssertContainsText(script, "if ($analysisPaths.Length -gt 0)", "powershell runner handles empty path list");
        AssertContainsText(script, "foreach ($analysisPath in $analysisPaths)", "powershell runner iterates filtered paths");
        AssertContainsText(script, "Invoke-ScriptAnalyzer -Path $analysisPath", "powershell runner invokes analyzer per path");
        AssertContainsText(script, "-ErrorAction Continue -ErrorVariable +invokeErrors",
            "powershell runner captures non-terminating engine errors");
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
}
#endif
