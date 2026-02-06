using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Cli.Analysis;

internal static class AnalyzeRunCommand {
    private const string DefaultOutputDirectory = "artifacts";
    private const string MaxFileLinesRuleId = "IXLOC001";
    private const int MaxFileLinesLimit = 700;
    private const string InternalToolName = "IntelligenceX.Maintainability";
    private static readonly string[] GeneratedSuffixes = {
        ".designer.cs",
        ".generated.cs",
        ".g.cs",
        ".g.i.cs"
    };
    private static readonly string[] ExcludedDirectoryMarkers = {
        "/.git/",
        "/.worktrees/",
        "/bin/",
        "/obj/",
        "/artifacts/"
    };
    private static readonly JsonSerializerOptions FindingsJsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> RunAsync(string[] args) {
        if (!TryParseOptions(args, out var options, out var error)) {
            if (!string.IsNullOrWhiteSpace(error)) {
                Console.WriteLine(error);
            }
            PrintHelp();
            return 1;
        }

        var workspace = AnalyzeRunner.ResolveWorkspace(options.Workspace);
        var outputDirectory = ResolveOutputDirectory(workspace, options.OutputDirectory);
        var configPath = AnalyzeRunner.ResolveConfigPath(options.ConfigPath, workspace);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            Console.WriteLine($"Config not found: {configPath ?? "<null>"}");
            return 1;
        }

        JsonObject? root;
        try {
            root = JsonLite.Parse(File.ReadAllText(configPath))?.AsObject();
        } catch (Exception ex) {
            Console.WriteLine($"Failed to parse config: {ex.Message}");
            return 1;
        }
        if (root is null) {
            Console.WriteLine("Config root must be a JSON object.");
            return 1;
        }

        var reviewObj = root.GetObject("review") ?? root;
        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root, reviewObj, settings);
        Directory.CreateDirectory(outputDirectory);

        var findingsPath = Path.Combine(outputDirectory, "intelligencex.findings.json");
        if (!settings.Enabled) {
            Console.WriteLine("analysis.enabled is false. Writing empty findings output.");
            WriteFindingsJson(findingsPath, Array.Empty<AnalysisFindingItem>());
            return 0;
        }

        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        var policy = AnalysisPolicyBuilder.Build(settings, catalog);
        foreach (var warning in policy.Warnings) {
            Console.WriteLine($"Warning: {warning}");
        }

        if (policy.Rules.Count == 0) {
            Console.WriteLine("No rules selected from analysis packs. Writing empty findings output.");
            WriteFindingsJson(findingsPath, Array.Empty<AnalysisFindingItem>());
            return 0;
        }

        var csharpRules = policy.SelectByLanguage("csharp", "cs");
        var powershellRules = policy.SelectByLanguage("powershell", "ps");
        var internalRules = policy.SelectByLanguage("internal");
        var runWarnings = new List<string>();
        var runFailures = new List<string>();
        var findings = new List<AnalysisFindingItem>();

        var tempConfigDirectory = Path.Combine(Path.GetTempPath(), "ix-analysis-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigDirectory);

        try {
            var export = AnalysisConfigExporter.Export(settings, catalog, tempConfigDirectory);
            foreach (var warning in export.Warnings) {
                runWarnings.Add(warning);
            }
            var generatedEditorConfig = export.Files.FirstOrDefault(file =>
                Path.GetFileName(file).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase));
            var generatedPowerShellSettings = export.Files.FirstOrDefault(file =>
                Path.GetFileName(file).Equals("PSScriptAnalyzerSettings.psd1", StringComparison.OrdinalIgnoreCase));

            if (csharpRules.Count > 0) {
                var csharpResult = await RunCsharpAsync(options, workspace, outputDirectory, settings,
                    generatedEditorConfig, runWarnings).ConfigureAwait(false);
                if (!csharpResult.Success) {
                    runFailures.Add(csharpResult.Message);
                }
            }

            if (powershellRules.Count > 0) {
                var psResult = await RunPowerShellAsync(options, workspace, findingsPath, settings,
                    generatedPowerShellSettings, runWarnings).ConfigureAwait(false);
                if (!psResult.Success) {
                    runFailures.Add(psResult.Message);
                } else {
                    findings.AddRange(psResult.Findings);
                }
            }
            if (internalRules.Count > 0) {
                findings.AddRange(RunInternalMaintainabilityChecks(workspace, internalRules, runWarnings));
            }

            if (csharpRules.Count == 0) {
                runWarnings.Add("No C# rules selected; skipping Roslyn analysis.");
            }
            if (powershellRules.Count == 0) {
                runWarnings.Add("No PowerShell rules selected; skipping PSScriptAnalyzer analysis.");
            }
        } finally {
            TryDeleteDirectory(tempConfigDirectory);
        }

        WriteFindingsJson(findingsPath, findings);

        foreach (var warning in runWarnings.Distinct(StringComparer.OrdinalIgnoreCase)) {
            Console.WriteLine($"Warning: {warning}");
        }
        if (runFailures.Count == 0) {
            Console.WriteLine($"Analysis complete. Findings JSON: {findingsPath}");
            return 0;
        }

        foreach (var failure in runFailures.Distinct(StringComparer.OrdinalIgnoreCase)) {
            Console.WriteLine($"Error: {failure}");
        }
        if (options.Strict) {
            Console.WriteLine("Analyze run failed (strict mode).");
            return 1;
        }

        Console.WriteLine("Analyze run completed with warnings (non-strict mode).");
        return 0;
    }

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

    private static IReadOnlyList<AnalysisFindingItem> RunInternalMaintainabilityChecks(string workspace,
        IReadOnlyList<AnalysisPolicyRule> rules, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var maxLinesRule = rules.FirstOrDefault(rule => IsRuleMatch(rule.Rule, MaxFileLinesRuleId));
        if (maxLinesRule is null) {
            return findings;
        }

        var severity = NormalizeSeverity(maxLinesRule.Severity);
        foreach (var file in Directory.EnumerateFiles(workspace, "*.cs", SearchOption.AllDirectories)) {
            var fullPath = Path.GetFullPath(file);
            var relativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/');
            if (IsExcludedSourceFile(relativePath)) {
                continue;
            }

            int lineCount;
            try {
                lineCount = CountFileLines(fullPath);
            } catch (Exception ex) {
                warnings.Add($"Failed to read file for line-count check ({relativePath}): {ex.Message}");
                continue;
            }
            if (lineCount <= MaxFileLinesLimit) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = relativePath,
                Line = 1,
                Severity = severity,
                Message = $"File has {lineCount} lines (limit {MaxFileLinesLimit}). Split into smaller units.",
                RuleId = maxLinesRule.Rule.Id,
                Tool = InternalToolName,
                Fingerprint = $"{maxLinesRule.Rule.Id}:{relativePath}:{lineCount}"
            });
        }

        Console.WriteLine($"Internal maintainability findings: {findings.Count} item(s).");
        return findings;
    }

    private static bool IsRuleMatch(AnalysisRule rule, string expectedRuleId) {
        if (rule is null || string.IsNullOrWhiteSpace(expectedRuleId)) {
            return false;
        }
        if (string.Equals(rule.Id, expectedRuleId, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return string.Equals(rule.ToolRuleId, expectedRuleId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedSourceFile(string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return true;
        }
        var normalized = "/" + relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var marker in ExcludedDirectoryMarkers) {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        foreach (var suffix in GeneratedSuffixes) {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static int CountFileLines(string path) {
        var count = 0;
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is not null) {
            count++;
        }
        return count;
    }

    private static string NormalizeSeverity(string? severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "warning";
        }
        return severity.Trim().ToLowerInvariant() switch {
            "critical" => "error",
            "high" => "error",
            "error" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "medium" => "warning",
            "info" => "info",
            "information" => "info",
            "low" => "info",
            _ => "warning"
        };
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

    private static string ResolveOutputDirectory(string workspace, string outputDirectory) {
        if (Path.IsPathRooted(outputDirectory)) {
            return outputDirectory;
        }
        return Path.Combine(workspace, outputDirectory);
    }

    private static void WriteFindingsJson(string path, IReadOnlyList<AnalysisFindingItem> items) {
        var payload = new FindingsEnvelope {
            Schema = "intelligencex.findings.v1",
            Items = items?.ToList() ?? new List<AnalysisFindingItem>()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, FindingsJsonOptions));
    }

    private static IReadOnlyList<AnalysisFindingItem> ReadFindingsJson(string path) {
        if (!File.Exists(path)) {
            return Array.Empty<AnalysisFindingItem>();
        }
        try {
            var envelope = JsonSerializer.Deserialize<FindingsEnvelope>(File.ReadAllText(path), FindingsJsonOptions);
            return envelope?.Items is { } items ? items : Array.Empty<AnalysisFindingItem>();
        } catch {
            return Array.Empty<AnalysisFindingItem>();
        }
    }

    private static void TryDeleteDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
            return;
        }
        try {
            Directory.Delete(path, true);
        } catch {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteFile(string path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return;
        }
        try {
            File.Delete(path);
        } catch {
            // Best-effort cleanup.
        }
    }

    private static bool TryParseOptions(string[] args, out AnalyzeRunOptions options, out string? error) {
        options = new AnalyzeRunOptions();
        error = null;
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("help", StringComparison.OrdinalIgnoreCase)) {
                error = null;
                return false;
            }
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.ConfigPath = args[++i];
                continue;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.OutputDirectory = args[++i];
                continue;
            }
            if (arg.Equals("--dotnet-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.DotnetCommand = args[++i];
                continue;
            }
            if ((arg.Equals("--framework", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("--dotnet-framework", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length) {
                options.DotnetFramework = args[++i];
                continue;
            }
            if (arg.Equals("--pwsh-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.PowerShellCommand = args[++i];
                continue;
            }
            if (arg.Equals("--strict", StringComparison.OrdinalIgnoreCase)) {
                options.Strict = true;
                continue;
            }
            error = $"Unknown argument: {arg}";
            return false;
        }
        return true;
    }

    public static void PrintHelp() {
        Console.WriteLine("  intelligencex analyze run [--config <path>] [--workspace <path>] [--out <dir>]");
        Console.WriteLine("                         [--dotnet-command <path>] [--framework <tfm>] [--pwsh-command <path>] [--strict]");
    }

    private static string BuildPowerShellRunnerScript() {
        return @"param(
    [Parameter(Mandatory=$true)][string]$Workspace,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter()][string]$SettingsPath
)
$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}
Import-Module PSScriptAnalyzer -ErrorAction Stop

$invoke = @{
    Path = $Workspace
    Recurse = $true
    Severity = @('Error','Warning','Information')
}
if ($SettingsPath -and (Test-Path -LiteralPath $SettingsPath)) {
    $invoke['Settings'] = $SettingsPath
}

$results = Invoke-ScriptAnalyzer @invoke
$items = @()
foreach ($result in $results) {
    if (-not $result.ScriptPath -or -not $result.Message) {
        continue
    }
    $severity = switch ($result.Severity) {
        'Error' { 'error' }
        'Warning' { 'warning' }
        default { 'info' }
    }
    $items += [ordered]@{
        path = [string]$result.ScriptPath
        line = [int]($result.Line)
        severity = $severity
        message = [string]$result.Message
        ruleId = [string]$result.RuleName
        tool = 'PSScriptAnalyzer'
    }
}

$directory = [System.IO.Path]::GetDirectoryName($OutFile)
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

[ordered]@{
    schema = 'intelligencex.findings.v1'
    items = $items
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8

Write-Output ('PSScriptAnalyzer findings: ' + $items.Count)";
    }

    private sealed class AnalyzeRunOptions {
        public string? ConfigPath { get; set; }
        public string? Workspace { get; set; }
        public string OutputDirectory { get; set; } = DefaultOutputDirectory;
        public string DotnetCommand { get; set; } = "dotnet";
        public string? DotnetFramework { get; set; }
        public string PowerShellCommand { get; set; } = "pwsh";
        public bool Strict { get; set; }
    }

    private sealed class TemporaryFileScope : IDisposable {
        private readonly string _path;
        private readonly bool _hadOriginal;
        private readonly string _originalContent;
        private bool _disposed;

        private TemporaryFileScope(string path, bool hadOriginal, string originalContent) {
            _path = path;
            _hadOriginal = hadOriginal;
            _originalContent = originalContent;
        }

        public static TemporaryFileScope Replace(string path, string content) {
            var hadOriginal = File.Exists(path);
            var originalContent = hadOriginal ? File.ReadAllText(path) : string.Empty;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory!);
            }
            File.WriteAllText(path, content ?? string.Empty);
            return new TemporaryFileScope(path, hadOriginal, originalContent);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            try {
                if (_hadOriginal) {
                    File.WriteAllText(_path, _originalContent);
                } else if (File.Exists(_path)) {
                    File.Delete(_path);
                }
            } catch {
                // Best-effort restore.
            }
        }
    }

    private sealed record RunnerResult(bool Success, string Message);
    private sealed record PowerShellRunnerResult(bool Success, string Message, IReadOnlyList<AnalysisFindingItem> Findings);
    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

    private sealed class FindingsEnvelope {
        public string Schema { get; set; } = string.Empty;
        public List<AnalysisFindingItem> Items { get; set; } = new List<AnalysisFindingItem>();
    }

    private sealed class AnalysisFindingItem {
        public string Path { get; set; } = string.Empty;
        public int Line { get; set; }
        public string Severity { get; set; } = "warning";
        public string Message { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;
        public string? Fingerprint { get; set; }
    }
}
