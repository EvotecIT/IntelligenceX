using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static async Task<RunnerResult> RunCsharpAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        AnalysisSettings settings, string? generatedEditorConfig, List<string> warnings) {
        var sarifPath = Path.Combine(outputDirectory, "intelligencex.roslyn.sarif");
        using var overrideScope = PrepareEditorConfigOverride(settings, workspace, generatedEditorConfig, warnings);

        var requestedFramework = options.DotnetFramework?.Trim();
        if (string.IsNullOrWhiteSpace(requestedFramework)) {
            var args = new List<string> { "build", "-nologo", $"/p:ErrorLog={sarifPath}" };
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

        // When a framework is specified (CI uses net8.0), build only projects that explicitly target that framework.
        // This avoids failures on Linux runners when the repo contains Windows-only TFMs (e.g. net10.0-windows).
        var candidates = DiscoverDotnetProjects(workspace);
        var projectsToBuild = candidates
            .Where(path => ProjectTargetsFramework(path, requestedFramework))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projectsToBuild.Count == 0) {
            warnings.Add($"No .NET projects target '{requestedFramework}'; Roslyn analysis skipped.");
            return new RunnerResult(true, string.Empty);
        }

        var sarifParts = new List<string>();
        for (var i = 0; i < projectsToBuild.Count; i++) {
            var project = projectsToBuild[i];
            var partPath = Path.Combine(outputDirectory, $"intelligencex.roslyn.{i + 1}.sarif");
            var args = new List<string> {
                "build",
                "-nologo",
                project,
                "--framework",
                requestedFramework,
                $"/p:ErrorLog={partPath}"
            };
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
            if (File.Exists(partPath)) {
                sarifParts.Add(partPath);
            }
        }

        if (sarifParts.Count == 0) {
            warnings.Add("Roslyn SARIF file was not generated.");
            return new RunnerResult(true, string.Empty);
        }

        if (sarifParts.Count == 1) {
            File.Copy(sarifParts[0], sarifPath, overwrite: true);
            Console.WriteLine($"Roslyn SARIF: {sarifPath}");
            return new RunnerResult(true, string.Empty);
        }

        if (!TryMergeSarif(sarifParts, sarifPath, out var mergeError)) {
            warnings.Add($"Roslyn SARIF merge failed: {mergeError}");
            File.Copy(sarifParts[0], sarifPath, overwrite: true);
        }

        Console.WriteLine($"Roslyn SARIF: {sarifPath}");
        return new RunnerResult(true, string.Empty);
    }

    private static IReadOnlyList<string> DiscoverDotnetProjects(string workspace) {
        // Prefer building projects from the workspace solution when present.
        var slnPath = Directory.EnumerateFiles(workspace, "IntelligenceX.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                      ?? Directory.EnumerateFiles(workspace, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(slnPath) && File.Exists(slnPath)) {
            var slnDir = Path.GetDirectoryName(slnPath) ?? workspace;
            var list = new List<string>();
            foreach (var line in File.ReadAllLines(slnPath)) {
                // Project("{GUID}") = "Name", "path\to\proj.csproj", "{GUID}"
                if (!line.Contains(".csproj", System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var parts = line.Split(',');
                if (parts.Length < 2) {
                    continue;
                }
                var rawPath = parts[1].Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(rawPath) || !rawPath.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var full = Path.GetFullPath(Path.Combine(slnDir, rawPath));
                if (File.Exists(full)) {
                    list.Add(full);
                }
            }
            return list;
        }

        // Fallback: enumerate projects under workspace and skip generated output folders.
        return Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
    }

    private static bool ProjectTargetsFramework(string csprojPath, string framework) {
        if (string.IsNullOrWhiteSpace(csprojPath) || string.IsNullOrWhiteSpace(framework) || !File.Exists(csprojPath)) {
            return false;
        }
        try {
            var content = File.ReadAllText(csprojPath);
            var tfms = ExtractTagValue(content, "TargetFrameworks") ?? ExtractTagValue(content, "TargetFramework");
            if (string.IsNullOrWhiteSpace(tfms)) {
                return false;
            }
            foreach (var item in tfms.Split(';')) {
                if (item.Trim().Equals(framework, System.StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        } catch {
            return false;
        }
    }

    private static string? ExtractTagValue(string content, string tagName) {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(tagName)) {
            return null;
        }
        var open = "<" + tagName + ">";
        var close = "</" + tagName + ">";
        var start = content.IndexOf(open, System.StringComparison.OrdinalIgnoreCase);
        if (start < 0) {
            return null;
        }
        start += open.Length;
        var end = content.IndexOf(close, start, System.StringComparison.OrdinalIgnoreCase);
        if (end < 0) {
            return null;
        }
        return content.Substring(start, end - start).Trim();
    }

    private static bool TryMergeSarif(IReadOnlyList<string> inputPaths, string outputPath, out string? error) {
        error = null;
        try {
            var allRuns = new List<JsonElement>();
            string? schema = null;
            string? version = null;

            foreach (var path in inputPaths ?? Array.Empty<string>()) {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                    continue;
                }
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (schema is null && root.TryGetProperty("$schema", out var schemaValue) &&
                    schemaValue.ValueKind == JsonValueKind.String) {
                    schema = schemaValue.GetString();
                }
                if (version is null && root.TryGetProperty("version", out var versionValue) &&
                    versionValue.ValueKind == JsonValueKind.String) {
                    version = versionValue.GetString();
                }
                if (root.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array) {
                    foreach (var run in runs.EnumerateArray()) {
                        allRuns.Add(run.Clone());
                    }
                }
            }

            using var stream = File.Create(outputPath);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(schema)) {
                writer.WriteString("$schema", schema);
            }
            writer.WriteString("version", string.IsNullOrWhiteSpace(version) ? "2.1.0" : version);
            writer.WritePropertyName("runs");
            writer.WriteStartArray();
            foreach (var run in allRuns) {
                run.WriteTo(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            return true;
        } catch (System.Exception ex) {
            error = ex.Message;
            return false;
        }
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

    private static async Task<RunnerResult> RunJavaScriptAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        List<string> warnings) {
        var sarifPath = Path.Combine(outputDirectory, "intelligencex.eslint.sarif");
        var args = new List<string> {
            "--yes",
            "eslint",
            ".",
            "--ext",
            ".js,.jsx,.mjs,.cjs,.ts,.tsx",
            "--format",
            "sarif",
            "--output-file",
            sarifPath
        };

        var result = await RunProcessAsync(options.NpxCommand, args, workspace).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.StdOut)) {
            Console.WriteLine(result.StdOut.Trim());
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr)) {
            Console.WriteLine(result.StdErr.Trim());
        }

        // ESLint returns 1 when findings are present; this is not a runner failure.
        if (result.ExitCode != 0 && result.ExitCode != 1) {
            return new RunnerResult(false,
                $"JavaScript/TypeScript analysis returned exit code {result.ExitCode}.");
        }

        if (!File.Exists(sarifPath)) {
            warnings.Add("ESLint SARIF file was not generated.");
            return new RunnerResult(true, string.Empty);
        }

        Console.WriteLine($"ESLint SARIF: {sarifPath}");
        return new RunnerResult(true, string.Empty);
    }

    private static async Task<RunnerResult> RunPythonAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        List<string> warnings) {
        var sarifPath = Path.Combine(outputDirectory, "intelligencex.ruff.sarif");
        var args = new List<string> {
            "check",
            ".",
            "--output-format",
            "sarif"
        };

        var result = await RunProcessAsync(options.RuffCommand, args, workspace).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.StdErr)) {
            Console.WriteLine(result.StdErr.Trim());
        }

        // Ruff returns 1 when findings are present; this is not a runner failure.
        if (result.ExitCode != 0 && result.ExitCode != 1) {
            return new RunnerResult(false, $"Python analysis returned exit code {result.ExitCode}.");
        }

        if (!string.IsNullOrWhiteSpace(result.StdOut)) {
            var payload = result.StdOut.Trim();
            if (!string.IsNullOrWhiteSpace(payload) && payload.StartsWith("{", StringComparison.Ordinal)) {
                File.WriteAllText(sarifPath, payload + Environment.NewLine);
            }
        }

        if (!File.Exists(sarifPath)) {
            warnings.Add("Ruff SARIF file was not generated.");
            return new RunnerResult(true, string.Empty);
        }

        Console.WriteLine($"Ruff SARIF: {sarifPath}");
        return new RunnerResult(true, string.Empty);
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
