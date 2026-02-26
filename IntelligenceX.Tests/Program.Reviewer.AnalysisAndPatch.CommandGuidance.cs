namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static readonly object AnalyzeRunCommandOutputCaptureLock = new();

    private static void TestAnalyzeRunMissingDotnetReportsUnavailableCommandGuidance() {
        var (exit, output) = RunAnalyzeRunWithMissingDotnetAndCaptureOutput(strict: true);
        AssertEqual(1, exit, "analyze run strict missing dotnet exits failure");
        AssertContainsText(output, "analysis command '__ix_missing_dotnet_command__' is unavailable",
            "analyze run strict missing dotnet reports unavailable command guidance");
        AssertContainsText(output, "--dotnet-command",
            "analyze run strict missing dotnet reports override option guidance");
    }

    private static void TestAnalyzeRunMissingDotnetWithFrameworkReportsUnavailableCommandGuidance() {
        var (exit, output) = RunAnalyzeRunWithMissingDotnetAndCaptureOutput(
            strict: true,
            strictBeforeFramework: true,
            frameworkOverride: "net8.0");
        AssertEqual(1, exit, "analyze run strict missing dotnet with framework exits failure");
        AssertContainsText(output, "analysis command '__ix_missing_dotnet_command__' is unavailable",
            "analyze run strict missing dotnet with framework reports unavailable command guidance");
        AssertContainsText(output, "--dotnet-command",
            "analyze run strict missing dotnet with framework reports override option guidance");
    }

    private static void TestAnalyzeRunMissingPowerShellReportsUnavailableCommandGuidance() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-run-missing-pwsh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "powershell"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));
            Directory.CreateDirectory(Path.Combine(temp, "scripts"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["powershell-default"],
    "run": {
      "strict": true
    }
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "powershell", "IXPS001.json"), """
{
  "id": "IXPS001",
  "language": "powershell",
  "tool": "PSScriptAnalyzer",
  "toolRuleId": "PSAvoidUsingWriteHost",
  "type": "bug",
  "title": "PowerShell smoke rule",
  "description": "Ensures PowerShell analyzer runner is selected.",
  "category": "Reliability",
  "defaultSeverity": "warning"
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "powershell-default.json"), """
{
  "id": "powershell-default",
  "label": "PowerShell Default",
  "rules": ["IXPS001"]
}
""");

            File.WriteAllText(Path.Combine(temp, "scripts", "sample.ps1"), "Write-Output 'hello'");

            var (exit, output) = RunAnalyzeAndCaptureOutput(new[] {
                "run",
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", Path.Combine(temp, "artifacts"),
                "--pwsh-command", "__ix_missing_pwsh_command__",
                "--strict"
            });

            AssertEqual(1, exit, "analyze run strict missing powershell exits failure");
            AssertContainsText(output, "analysis command '__ix_missing_pwsh_command__' is unavailable",
                "analyze run strict missing powershell reports unavailable command guidance");
            AssertContainsText(output, "--pwsh-command",
                "analyze run strict missing powershell reports override option guidance");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static (int ExitCode, string Output) RunAnalyzeRunWithMissingDotnetAndCaptureOutput(
        bool strict,
        bool? strictOverride = null,
        bool strictOverrideEqualsSyntax = false,
        string? packsOverride = null,
        bool strictBeforePacks = false,
        string? strictOverrideRawValue = null,
        bool strictBeforeFramework = false,
        string? frameworkOverride = null,
        bool includeCsharpSource = true) {
        lock (AnalyzeRunCommandOutputCaptureLock) {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var exitCode = RunAnalyzeRunWithMissingDotnet(
                    strict,
                    strictOverride,
                    strictOverrideEqualsSyntax,
                    packsOverride,
                    strictBeforePacks,
                    strictOverrideRawValue,
                    strictBeforeFramework,
                    frameworkOverride,
                    includeCsharpSource);
                outWriter.Flush();
                errWriter.Flush();
                return (exitCode, outWriter.ToString() + errWriter.ToString());
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}
#endif
