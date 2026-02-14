using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private const string DefaultOutputDirectory = "artifacts";
    private const string DuplicationMetricsFileName = "intelligencex.duplication.json";
    private const int DefaultMaxFileLinesLimit = 700;
    private const string MaxLinesTagPrefix = "max-lines:";
    private const int GeneratedHeaderLinesToInspect = 80;
    private const string GeneratedHeaderLinesTagPrefix = "generated-header-lines:";
    private const string InternalToolName = "IntelligenceX.Maintainability";
    private const string GeneratedSuffixTagPrefix = "generated-suffix:";
    private const string GeneratedMarkerTagPrefix = "generated-marker:";
    private const string ExcludedDirectoryTagPrefix = "exclude-dir:";
    private static readonly string[] DefaultExcludedDirectorySegments = {
        ".git",
        ".worktrees",
        ".vs",
        "bin",
        "obj",
        "node_modules"
    };
    private static readonly JsonSerializerOptions FindingsJsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex PackIdRegex = new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,126}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        if (options.PacksOverride.Count > 0) {
            settings.Packs = options.PacksOverride.ToArray();
        }
        if (!options.StrictSet) {
            options.Strict = settings.Run.Strict;
        }
        Directory.CreateDirectory(outputDirectory);

        var findingsPath = Path.Combine(outputDirectory, "intelligencex.findings.json");
        var duplicationMetricsPath = Path.Combine(outputDirectory, DuplicationMetricsFileName);
        if (!settings.Enabled) {
            Console.WriteLine("analysis.enabled is false. Writing empty findings output.");
            WriteFindingsJson(findingsPath, Array.Empty<AnalysisFindingItem>());
            DuplicationMetricsStore.Write(duplicationMetricsPath, Array.Empty<DuplicationRuleMetrics>());
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
            DuplicationMetricsStore.Write(duplicationMetricsPath, Array.Empty<DuplicationRuleMetrics>());
            return 0;
        }

        var csharpRules = policy.SelectByLanguage("csharp", "cs");
        var powershellRules = policy.SelectByLanguage("powershell", "ps");
        var javascriptRules = policy.SelectByLanguage("javascript", "js", "typescript", "ts");
        var pythonRules = policy.SelectByLanguage("python", "py");
        var internalRules = policy.SelectByLanguage("internal");
        var runWarnings = new List<string>();
        var runFailures = new List<string>();
        var findings = new List<AnalysisFindingItem>();
        var duplicationRuleMetrics = new List<DuplicationRuleMetrics>();

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
            if (javascriptRules.Count > 0) {
                var jsResult = await RunJavaScriptAsync(options, workspace, outputDirectory, runWarnings).ConfigureAwait(false);
                if (!jsResult.Success) {
                    runFailures.Add(jsResult.Message);
                }
            }
            if (pythonRules.Count > 0) {
                var pyResult = await RunPythonAsync(options, workspace, outputDirectory, runWarnings).ConfigureAwait(false);
                if (!pyResult.Success) {
                    runFailures.Add(pyResult.Message);
                }
            }
            if (internalRules.Count > 0) {
                var internalResult = RunInternalMaintainabilityChecks(workspace, outputDirectory, internalRules, runWarnings);
                findings.AddRange(internalResult.Findings);
                duplicationRuleMetrics.AddRange(internalResult.DuplicationRuleMetrics);
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
        DuplicationMetricsStore.Write(duplicationMetricsPath, duplicationRuleMetrics);

        foreach (var warning in runWarnings.Distinct(StringComparer.OrdinalIgnoreCase)) {
            Console.WriteLine($"Warning: {warning}");
        }
        if (runFailures.Count == 0) {
            Console.WriteLine($"Analysis complete. Findings JSON: {findingsPath}");
            Console.WriteLine($"Duplication metrics JSON: {duplicationMetricsPath}");
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
            if (arg.Equals("--npx-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.NpxCommand = args[++i];
                continue;
            }
            if (arg.Equals("--ruff-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.RuffCommand = args[++i];
                continue;
            }
            if (arg.Equals("--pack", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--packs", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = $"Missing value for {arg}.";
                    return false;
                }
                if (!TryAddPackValues(options.PacksOverride, args[++i], out error)) {
                    return false;
                }
                continue;
            }
            if (arg.StartsWith("--strict=", StringComparison.OrdinalIgnoreCase)) {
                var strictValue = arg.Substring("--strict=".Length);
                if (!TryParseStrictValue(strictValue, out var parsedStrict)) {
                    error = $"Invalid value for --strict: {strictValue}. Expected true|false.";
                    return false;
                }
                options.Strict = parsedStrict;
                options.StrictSet = true;
                continue;
            }
            if (arg.Equals("--strict", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                    var strictValue = args[++i];
                    if (!TryParseStrictValue(strictValue, out var parsedStrict)) {
                        error = $"Invalid value for --strict: {strictValue}. Expected true|false.";
                        return false;
                    }
                    options.Strict = parsedStrict;
                    options.StrictSet = true;
                    continue;
                }
                options.Strict = true;
                options.StrictSet = true;
                continue;
            }
            error = $"Unknown argument: {arg}";
            return false;
        }
        return true;
    }

    private static bool TryAddPackValues(ICollection<string> packs, string? raw, out string? error) {
        error = null;
        if (packs is null) {
            error = "Pack override destination is unavailable.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw)) {
            error = "Pack override value cannot be empty.";
            return false;
        }

        var values = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) {
            error = "Pack override value cannot be empty.";
            return false;
        }

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            if (!PackIdRegex.IsMatch(value)) {
                error = $"Invalid pack id '{value}'. Use comma-separated ids like all-50, all-security-default, powershell-50.";
                return false;
            }
            if (!packs.Contains(value, StringComparer.OrdinalIgnoreCase)) {
                packs.Add(value);
            }
        }

        if (packs.Count == 0) {
            error = "Pack override value cannot be empty.";
            return false;
        }

        return true;
    }

    private static bool TryParseStrictValue(string value, out bool strict) {
        strict = false;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        switch (value.Trim().ToLowerInvariant()) {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "on":
                strict = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "off":
                strict = false;
                return true;
            default:
                return false;
        }
    }

    public static void PrintHelp() {
        Console.WriteLine("  intelligencex analyze run [--config <path>] [--workspace <path>] [--out <dir>]");
        Console.WriteLine("                         [--dotnet-command <path>] [--framework <tfm>] [--pwsh-command <path>]");
        Console.WriteLine("                         [--npx-command <path>] [--ruff-command <path>] [--pack <id>] [--packs <id1,id2>]");
        Console.WriteLine("                         [--strict [true|false]]");
    }

    internal static string BuildPowerShellRunnerScriptForTests() {
        return BuildPowerShellRunnerScript();
    }

    private static string BuildPowerShellRunnerScript() {
        return @"param(
    [Parameter(Mandatory=$true)][string]$Workspace,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter()][string]$SettingsPath,
    [Parameter()][string]$ExcludedDirectoriesCsv,
    [Parameter()][switch]$FailOnAnalyzerErrors
)
$ErrorActionPreference = 'Stop'

if ([System.IO.File]::Exists($Workspace)) {
    throw ('Workspace path is not a directory: ' + $Workspace)
}
if (-not [System.IO.Directory]::Exists($Workspace)) {
    throw ('Workspace path not found: ' + $Workspace)
}
$workspaceRoot = [System.IO.Path]::GetFullPath($Workspace)

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}
Import-Module PSScriptAnalyzer -ErrorAction Stop

$excludedSegmentSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if ($ExcludedDirectoriesCsv) {
    foreach ($segment in $ExcludedDirectoriesCsv.Split(',')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }
        [void]$excludedSegmentSet.Add($segment.Trim())
    }
}

function Get-AnalyzerPaths {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][System.Collections.Generic.HashSet[string]]$ExcludedSegments
    )

    $paths = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push([System.IO.Path]::GetFullPath($Root))

    while ($stack.Count -gt 0) {
        $current = $stack.Pop()

        try {
            foreach ($subdirectory in [System.IO.Directory]::EnumerateDirectories($current)) {
                $name = [System.IO.Path]::GetFileName($subdirectory)
                if (-not [string]::IsNullOrWhiteSpace($name) -and $ExcludedSegments.Contains($name)) {
                    continue
                }

                try {
                    $attributes = [System.IO.File]::GetAttributes($subdirectory)
                    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        continue
                    }
                } catch [System.UnauthorizedAccessException] {
                    continue
                } catch [System.IO.PathTooLongException] {
                    continue
                } catch [System.IO.DirectoryNotFoundException] {
                    continue
                } catch [System.IO.IOException] {
                    continue
                }

                $stack.Push($subdirectory)
            }
        } catch [System.UnauthorizedAccessException] {
            continue
        } catch [System.IO.PathTooLongException] {
            continue
        } catch [System.IO.DirectoryNotFoundException] {
            continue
        } catch [System.IO.IOException] {
            continue
        }

        try {
            foreach ($file in [System.IO.Directory]::EnumerateFiles($current)) {
                try {
                    $attributes = [System.IO.File]::GetAttributes($file)
                    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        continue
                    }
                } catch [System.UnauthorizedAccessException] {
                    continue
                } catch [System.IO.PathTooLongException] {
                    continue
                } catch [System.IO.DirectoryNotFoundException] {
                    continue
                } catch [System.IO.IOException] {
                    continue
                }

                $extension = [System.IO.Path]::GetExtension($file)
                if ([string]::Equals($extension, '.ps1', [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($extension, '.psm1', [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($extension, '.psd1', [System.StringComparison]::OrdinalIgnoreCase)) {
                    [void]$paths.Add($file)
                }
            }
        } catch [System.UnauthorizedAccessException] {
            continue
        } catch [System.IO.PathTooLongException] {
            continue
        } catch [System.IO.DirectoryNotFoundException] {
            continue
        } catch [System.IO.IOException] {
            continue
        }
    }

    return $paths.ToArray()
}

$analysisPaths = Get-AnalyzerPaths -Root $workspaceRoot -ExcludedSegments $excludedSegmentSet
$invokeSeverity = @('Error','Warning','Information')
$hasSettings = $SettingsPath -and (Test-Path -LiteralPath $SettingsPath)

$invokeErrors = @()
$results = New-Object System.Collections.Generic.List[object]
if ($analysisPaths.Length -gt 0) {
    foreach ($analysisPath in $analysisPaths) {
        if ([string]::IsNullOrWhiteSpace($analysisPath)) {
            continue
        }

        try {
            if ($hasSettings) {
                foreach ($result in @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -Settings $SettingsPath -ErrorAction Continue -ErrorVariable +invokeErrors)) {
                    [void]$results.Add($result)
                }
            } else {
                foreach ($result in @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -ErrorAction Continue -ErrorVariable +invokeErrors)) {
                    [void]$results.Add($result)
                }
            }
        } catch {
            $invokeErrors += $_
        }
    }
}

$sawInvokeErrors = $false
foreach ($invokeError in $invokeErrors) {
    $sawInvokeErrors = $true
    $errorText = if ($invokeError.Exception -and $invokeError.Exception.Message) {
        $invokeError.Exception.Message
    } else {
        [string]$invokeError
    }
    [Console]::Error.WriteLine('PSScriptAnalyzer engine error: ' + $errorText)
}

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

Write-Output ('PSScriptAnalyzer findings: ' + $items.Count)
if ($sawInvokeErrors -and $FailOnAnalyzerErrors) {
    [Console]::Error.WriteLine('PSScriptAnalyzer reported one or more engine errors.')
    exit 2
}";
    }

    private sealed class AnalyzeRunOptions {
        public string? ConfigPath { get; set; }
        public string? Workspace { get; set; }
        public string OutputDirectory { get; set; } = DefaultOutputDirectory;
        public string DotnetCommand { get; set; } = "dotnet";
        public string? DotnetFramework { get; set; }
        public string PowerShellCommand { get; set; } = "pwsh";
        public string NpxCommand { get; set; } = "npx";
        public string RuffCommand { get; set; } = "ruff";
        public List<string> PacksOverride { get; } = new();
        public bool Strict { get; set; }
        public bool StrictSet { get; set; }
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
