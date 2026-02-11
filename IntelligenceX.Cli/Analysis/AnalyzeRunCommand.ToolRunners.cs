using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static async Task<RunnerResult> RunCsharpAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        AnalysisSettings settings, string? generatedEditorConfig, List<string> warnings) {
        var sarifPath = Path.Combine(outputDirectory, "intelligencex.roslyn.sarif");
        using var overrideScope = PrepareEditorConfigOverride(settings, workspace, generatedEditorConfig, warnings);

        var buildArgs = new[] {
            "build",
            "-nologo"
        };
        var args = new List<string>(buildArgs);
        if (!string.IsNullOrWhiteSpace(options.DotnetFramework)) {
            args.Add("--framework");
            args.Add(options.DotnetFramework);
        }
        args.Add($"/p:ErrorLog={sarifPath}");

        var result = await RunProcessAsync(options.DotnetCommand, args, workspace).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.StdOut)) {
            Console.WriteLine(result.StdOut.Trim());
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr)) {
            Console.WriteLine(result.StdErr.Trim());
        }

        if (result.ExitCode != 0) {
            return new RunnerResult(false, $"dotnet build returned exit code {result.ExitCode}.");
        }
        if (!File.Exists(sarifPath)) {
            warnings.Add("Roslyn SARIF file was not generated.");
            return new RunnerResult(true, string.Empty);
        }

        Console.WriteLine($"Roslyn SARIF: {sarifPath}");
        return new RunnerResult(true, string.Empty);
    }

    private static async Task<PowerShellRunnerResult> RunPowerShellAsync(AnalyzeRunOptions options, string workspace,
        string findingsPath, AnalysisSettings settings, string? generatedSettingsPath, List<string> warnings) {
        var settingsPath = ResolvePowerShellSettingsPath(settings, workspace, generatedSettingsPath, warnings);
        if (string.IsNullOrWhiteSpace(settingsPath)) {
            warnings.Add("PowerShell settings could not be resolved; writing empty findings.");
            return new PowerShellRunnerResult(true, string.Empty, Array.Empty<AnalysisFindingItem>());
        }

        var tempScript = Path.Combine(Path.GetTempPath(), "ix-pssa-" + Guid.NewGuid().ToString("N") + ".ps1");
        try {
            File.WriteAllText(tempScript, BuildPowerShellRunnerScript());
            var args = BuildPowerShellRunnerArgs(tempScript, workspace, findingsPath, settingsPath, options.Strict);
            var result = await RunProcessAsync(options.PowerShellCommand, args, workspace).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.StdOut)) {
                Console.WriteLine(result.StdOut.Trim());
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr)) {
                Console.WriteLine(result.StdErr.Trim());
            }
            if (result.ExitCode != 0) {
                return new PowerShellRunnerResult(false,
                    $"PowerShell analysis returned exit code {result.ExitCode}.", Array.Empty<AnalysisFindingItem>());
            }
            var findings = ReadFindingsJson(findingsPath);
            Console.WriteLine($"PowerShell findings: {findings.Count} item(s).");
            return new PowerShellRunnerResult(true, string.Empty, findings);
        } finally {
            TryDeleteFile(tempScript);
        }
    }

    internal static IReadOnlyList<string> BuildPowerShellRunnerArgsForTests(
        string tempScript,
        string workspace,
        string findingsPath,
        string settingsPath,
        bool strict) {
        return BuildPowerShellRunnerArgs(tempScript, workspace, findingsPath, settingsPath, strict);
    }

    private static List<string> BuildPowerShellRunnerArgs(
        string tempScript,
        string workspace,
        string findingsPath,
        string settingsPath,
        bool strict) {
        var args = new List<string> {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            tempScript,
            "-Workspace",
            workspace,
            "-OutFile",
            findingsPath,
            "-SettingsPath",
            settingsPath
        };
        var excludedDirectoriesCsv = BuildExcludedDirectoriesCsv();
        if (!string.IsNullOrWhiteSpace(excludedDirectoriesCsv)) {
            args.Add("-ExcludedDirectoriesCsv");
            args.Add(excludedDirectoriesCsv);
        }
        if (strict) {
            args.Add("-FailOnAnalyzerErrors");
        }
        return args;
    }

    private static string BuildExcludedDirectoriesCsv() {
        var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in DefaultExcludedDirectorySegments) {
            if (string.IsNullOrWhiteSpace(segment)) {
                continue;
            }
            segments.Add(segment.Trim());
        }
        return string.Join(",", segments.OrderBy(static segment => segment, StringComparer.OrdinalIgnoreCase));
    }

    private static TemporaryFileScope? PrepareEditorConfigOverride(AnalysisSettings settings, string workspace,
        string? generatedEditorConfig, List<string> warnings) {
        if (string.IsNullOrWhiteSpace(generatedEditorConfig) || !File.Exists(generatedEditorConfig)) {
            warnings.Add("Generated .editorconfig not available; using repository defaults.");
            return null;
        }

        var repoEditorConfig = Path.Combine(workspace, ".editorconfig");
        var generatedText = File.ReadAllText(generatedEditorConfig);
        var repoHasConfig = File.Exists(repoEditorConfig);
        switch (settings.ConfigMode) {
            case AnalysisConfigMode.Respect:
                if (repoHasConfig) {
                    return null;
                }
                return TemporaryFileScope.Replace(repoEditorConfig, generatedText);
            case AnalysisConfigMode.Overlay:
                if (repoHasConfig) {
                    var existing = File.ReadAllText(repoEditorConfig).TrimEnd();
                    var merged = existing + "\n\n# IntelligenceX overlay\n" + generatedText.Trim();
                    return TemporaryFileScope.Replace(repoEditorConfig, merged + "\n");
                }
                return TemporaryFileScope.Replace(repoEditorConfig, generatedText);
            case AnalysisConfigMode.Replace:
                return TemporaryFileScope.Replace(repoEditorConfig, generatedText);
            default:
                return null;
        }
    }

    private static string? ResolvePowerShellSettingsPath(AnalysisSettings settings, string workspace,
        string? generatedSettingsPath, List<string> warnings) {
        var repoSettingsPath = Path.Combine(workspace, "PSScriptAnalyzerSettings.psd1");
        var repoHasSettings = File.Exists(repoSettingsPath);

        switch (settings.ConfigMode) {
            case AnalysisConfigMode.Respect:
                if (repoHasSettings) {
                    return repoSettingsPath;
                }
                return generatedSettingsPath;
            case AnalysisConfigMode.Overlay:
                warnings.Add("PowerShell overlay currently uses generated settings for this run.");
                return generatedSettingsPath;
            case AnalysisConfigMode.Replace:
                return generatedSettingsPath;
            default:
                return repoHasSettings ? repoSettingsPath : generatedSettingsPath;
        }
    }

    private static async Task<CommandResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory) {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in arguments ?? Array.Empty<string>()) {
            psi.ArgumentList.Add(arg);
        }

        try {
            using var process = Process.Start(psi);
            if (process is null) {
                return new CommandResult(1, string.Empty, $"Failed to start process: {fileName}");
            }
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);
            return new CommandResult(process.ExitCode, stdOut, stdErr);
        } catch (Win32Exception ex) {
            return new CommandResult(127, string.Empty, ex.Message);
        } catch (Exception ex) {
            return new CommandResult(1, string.Empty, ex.Message);
        }
    }
}
