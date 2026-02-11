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

internal static partial class AnalyzeRunCommand {
    private const string DefaultOutputDirectory = "artifacts";
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
                findings.AddRange(RunInternalMaintainabilityChecks(workspace, outputDirectory, internalRules, runWarnings));
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
                $stack.Push($subdirectory)
            }
        } catch {
            continue
        }

        try {
            foreach ($file in [System.IO.Directory]::EnumerateFiles($current)) {
                $extension = [System.IO.Path]::GetExtension($file)
                switch ($extension.ToLowerInvariant()) {
                    '.ps1' { [void]$paths.Add($file) }
                    '.psm1' { [void]$paths.Add($file) }
                    '.psd1' { [void]$paths.Add($file) }
                }
            }
        } catch {
            continue
        }
    }

    return $paths.ToArray()
}

$analysisPaths = Get-AnalyzerPaths -Root $Workspace -ExcludedSegments $excludedSegmentSet
$invokeSeverity = @('Error','Warning','Information')
$hasSettings = $SettingsPath -and (Test-Path -LiteralPath $SettingsPath)

$invokeErrors = @()
$results = @()
if ($analysisPaths.Length -gt 0) {
    foreach ($analysisPath in $analysisPaths) {
        if ([string]::IsNullOrWhiteSpace($analysisPath)) {
            continue
        }

        try {
            if ($hasSettings) {
                $results += @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -Settings $SettingsPath -ErrorAction Continue -ErrorVariable +invokeErrors)
            } else {
                $results += @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -ErrorAction Continue -ErrorVariable +invokeErrors)
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
