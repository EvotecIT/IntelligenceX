namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunPowerShellScriptCapturesEngineErrors() {
        var script = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.BuildPowerShellRunnerScriptForTests();

        AssertContainsText(script, "[switch]$FailOnAnalyzerErrors", "powershell runner strict switch parameter");
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
    }
}
#endif
