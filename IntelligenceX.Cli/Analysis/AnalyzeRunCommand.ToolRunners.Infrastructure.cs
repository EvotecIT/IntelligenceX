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
