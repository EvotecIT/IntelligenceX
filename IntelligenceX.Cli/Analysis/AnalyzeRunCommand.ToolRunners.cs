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
        IReadOnlyList<AnalysisPolicyRule> rules, WorkspaceSourceInventory? sourceInventory, List<string> warnings) {
        var selectors = BuildJavaScriptRuleSelectors(rules);
        if (selectors.Count == 0) {
            warnings.Add("No JavaScript/TypeScript rule IDs selected; skipping ESLint analysis.");
            return new RunnerResult(true, string.Empty);
        }
        if (selectors.All(static selector => selector.Severity.Equals("off", StringComparison.OrdinalIgnoreCase))) {
            warnings.Add("All JavaScript/TypeScript rules are disabled by policy severity; skipping ESLint analysis.");
            return new RunnerResult(true, string.Empty);
        }
        if (!WorkspaceContainsAnySourceFile(sourceInventory, out var skippedSourceEnumerations, ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx")) {
            if (skippedSourceEnumerations > 0) {
                warnings.Add($"JavaScript/TypeScript source discovery skipped {skippedSourceEnumerations} path(s) due to access or IO errors.");
            }
            warnings.Add("No JavaScript/TypeScript source files detected; skipping ESLint analysis.");
            return new RunnerResult(true, string.Empty);
        }

        var sarifPath = Path.Combine(outputDirectory, "intelligencex.eslint.sarif");
        var args = BuildJavaScriptRunnerArgs(sarifPath, selectors);

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
                BuildExternalRunnerFailureMessage(
                    "JavaScript/TypeScript",
                    options.NpxCommand,
                    "--npx-command",
                    result));
        }

        if (!File.Exists(sarifPath)) {
            warnings.Add("ESLint SARIF file was not generated.");
            return new RunnerResult(true, string.Empty);
        }

        Console.WriteLine($"ESLint SARIF: {sarifPath}");
        return new RunnerResult(true, string.Empty);
    }

    private static async Task<RunnerResult> RunPythonAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        IReadOnlyList<AnalysisPolicyRule> rules, WorkspaceSourceInventory? sourceInventory, List<string> warnings) {
        var selectedRuleIds = BuildPythonSelectedRuleIds(rules);
        if (selectedRuleIds.Count == 0) {
            warnings.Add("All Python rules are disabled by policy severity; skipping Ruff analysis.");
            return new RunnerResult(true, string.Empty);
        }
        if (!WorkspaceContainsAnySourceFile(sourceInventory, out var skippedSourceEnumerations, ".py")) {
            if (skippedSourceEnumerations > 0) {
                warnings.Add($"Python source discovery skipped {skippedSourceEnumerations} path(s) due to access or IO errors.");
            }
            warnings.Add("No Python source files detected; skipping Ruff analysis.");
            return new RunnerResult(true, string.Empty);
        }

        var sarifPath = Path.Combine(outputDirectory, "intelligencex.ruff.sarif");
        var args = BuildPythonRunnerArgs(sarifPath, selectedRuleIds, includeOutputFile: true);

        var result = await RunProcessAsync(options.RuffCommand, args, workspace).ConfigureAwait(false);
        if (result.ExitCode != 0 && result.ExitCode != 1 && IsUnsupportedRuffOutputFileOption(result)) {
            warnings.Add("Ruff does not support --output-file; falling back to stdout SARIF capture.");
            args = BuildPythonRunnerArgs(sarifPath, selectedRuleIds, includeOutputFile: false);
            result = await RunProcessAsync(options.RuffCommand, args, workspace).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr)) {
            Console.WriteLine(result.StdErr.Trim());
        }

        // Ruff returns 1 when findings are present; this is not a runner failure.
        if (result.ExitCode != 0 && result.ExitCode != 1) {
            return new RunnerResult(false,
                BuildExternalRunnerFailureMessage(
                    "Python",
                    options.RuffCommand,
                    "--ruff-command",
                    result));
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

    private static List<string> BuildJavaScriptRunnerArgs(string sarifPath, IReadOnlyList<ExternalToolRuleSelector> selectors) {
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
        foreach (var selector in selectors.OrderBy(static selector => selector.ToolRuleId, StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(selector.ToolRuleId) || string.IsNullOrWhiteSpace(selector.Severity)) {
                continue;
            }
            args.Add("--rule");
            args.Add($"{selector.ToolRuleId}:{selector.Severity}");
        }
        return args;
    }

    private static IReadOnlyList<ExternalToolRuleSelector> BuildJavaScriptRuleSelectors(IReadOnlyList<AnalysisPolicyRule> rules) {
        var selectors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policyRule in rules ?? Array.Empty<AnalysisPolicyRule>()) {
            if (!IsPolicyRuleCompatibleWithTool(policyRule, "eslint")) {
                continue;
            }

            var toolRuleId = ResolveToolRuleId(policyRule.Rule);
            if (string.IsNullOrWhiteSpace(toolRuleId)) {
                continue;
            }

            var mappedSeverity = MapEslintSeverity(policyRule.Severity);
            if (selectors.TryGetValue(toolRuleId, out var existing)) {
                if (GetEslintSeverityRank(mappedSeverity) > GetEslintSeverityRank(existing)) {
                    selectors[toolRuleId] = mappedSeverity;
                }
                continue;
            }
            selectors[toolRuleId] = mappedSeverity;
        }

        return selectors
            .Select(static pair => new ExternalToolRuleSelector(pair.Key, pair.Value))
            .ToList();
    }

    private static string MapEslintSeverity(string? severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "warn";
        }
        // ESLint has only off|warn|error. We intentionally map all non-blocking IX severities to warn.
        return severity.Trim().ToLowerInvariant() switch {
            "critical" => "error",
            "error" => "error",
            "high" => "error",
            "warning" => "warn",
            "warn" => "warn",
            "medium" => "warn",
            "info" => "warn",
            "information" => "warn",
            "low" => "warn",
            "suggestion" => "warn",
            "none" => "off",
            _ => "warn"
        };
    }

    private static int GetEslintSeverityRank(string? severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return 1;
        }
        return severity.Trim().ToLowerInvariant() switch {
            "off" => 0,
            "warn" => 1,
            "error" => 2,
            _ => 1
        };
    }

    private static List<string> BuildPythonRunnerArgs(string sarifPath, IReadOnlyList<string> selectedRuleIds, bool includeOutputFile) {
        var args = new List<string> {
            "check",
            ".",
            "--output-format",
            "sarif"
        };
        if (includeOutputFile && !string.IsNullOrWhiteSpace(sarifPath)) {
            args.Add("--output-file");
            args.Add(sarifPath);
        }
        if (selectedRuleIds is { Count: > 0 }) {
            args.Add("--select");
            args.Add(string.Join(",", selectedRuleIds));
        }
        return args;
    }

    private static IReadOnlyList<string> BuildPythonSelectedRuleIds(IReadOnlyList<AnalysisPolicyRule> rules) {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policyRule in rules ?? Array.Empty<AnalysisPolicyRule>()) {
            if (!IsPolicyRuleCompatibleWithTool(policyRule, "ruff")) {
                continue;
            }

            if (!IsRuleSeverityEnabled(policyRule.Severity)) {
                continue;
            }

            var toolRuleId = ResolveToolRuleId(policyRule.Rule);
            if (string.IsNullOrWhiteSpace(toolRuleId)) {
                continue;
            }
            selected.Add(toolRuleId);
        }
        return selected.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsRuleSeverityEnabled(string? severity) {
        return !string.Equals(severity?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsupportedRuffOutputFileOption(CommandResult result) {
        var text = ((result.StdErr ?? string.Empty) + "\n" + (result.StdOut ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        return text.Contains("unexpected argument '--output-file'", StringComparison.Ordinal) ||
               text.Contains("unexpected argument \"--output-file\"", StringComparison.Ordinal) ||
               text.Contains("no such option: --output-file", StringComparison.Ordinal) ||
               text.Contains("unrecognized arguments: --output-file", StringComparison.Ordinal);
    }

    private static bool IsPolicyRuleCompatibleWithTool(AnalysisPolicyRule? policyRule, string expectedTool) {
        return policyRule?.Rule is not null &&
               !string.IsNullOrWhiteSpace(expectedTool) &&
               expectedTool.Equals(policyRule.Rule.Tool?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveToolRuleId(AnalysisRule rule) {
        if (rule is null) {
            return string.Empty;
        }
        return string.IsNullOrWhiteSpace(rule.ToolRuleId) ? rule.Id : rule.ToolRuleId;
    }

    internal static IReadOnlyList<string> BuildJavaScriptRunnerArgsForTests(
        string sarifPath,
        IReadOnlyDictionary<string, string> severityByToolRuleId) {
        var selectors = new List<ExternalToolRuleSelector>();
        foreach (var pair in severityByToolRuleId ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) {
                continue;
            }
            selectors.Add(new ExternalToolRuleSelector(pair.Key.Trim(), pair.Value.Trim()));
        }
        return BuildJavaScriptRunnerArgs(sarifPath, selectors);
    }

    internal static IReadOnlyList<string> BuildPythonRunnerArgsForTests(IReadOnlyList<string> selectedRuleIds) {
        return BuildPythonRunnerArgs(string.Empty, selectedRuleIds, includeOutputFile: false);
    }

    internal static IReadOnlyList<string> BuildPythonRunnerArgsWithOutputForTests(string sarifPath, IReadOnlyList<string> selectedRuleIds) {
        return BuildPythonRunnerArgs(sarifPath, selectedRuleIds, includeOutputFile: true);
    }

    internal static IReadOnlyDictionary<string, string> BuildJavaScriptRuleSelectorsForTests(IReadOnlyList<AnalysisPolicyRule> rules) {
        var selectors = BuildJavaScriptRuleSelectors(rules);
        return selectors.ToDictionary(
            static selector => selector.ToolRuleId,
            static selector => selector.Severity,
            StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> BuildPythonSelectedRuleIdsForTests(IReadOnlyList<AnalysisPolicyRule> rules) {
        return BuildPythonSelectedRuleIds(rules);
    }

    internal static bool IsUnsupportedRuffOutputFileOptionForTests(int exitCode, string stdOut, string stdErr) {
        return IsUnsupportedRuffOutputFileOption(new CommandResult(exitCode, stdOut, stdErr));
    }

    internal static string BuildExternalRunnerFailureMessageForTests(
        string languageLabel,
        string command,
        string optionName,
        int exitCode,
        string stdOut,
        string stdErr) {
        return BuildExternalRunnerFailureMessage(languageLabel, command, optionName, new CommandResult(exitCode, stdOut, stdErr));
    }

    internal static bool WorkspaceContainsAnySourceFileForTests(string workspace, params string[] extensions) {
        return WorkspaceContainsAnySourceFile(workspace, extensions);
    }

    internal static (bool Found, int SkippedEnumerations) WorkspaceContainsAnySourceFileWithDiagnosticsForTests(
        string workspace,
        params string[] extensions) {
        var found = WorkspaceContainsAnySourceFile(workspace, out var skippedEnumerations, extensions);
        return (found, skippedEnumerations);
    }

    internal static (IReadOnlyList<string> Extensions, int SkippedEnumerations) DiscoverWorkspaceSourceInventoryForTests(string workspace) {
        var inventory = DiscoverWorkspaceSourceInventory(workspace);
        if (inventory is null) {
            return (Array.Empty<string>(), 0);
        }

        var extensions = inventory.Extensions
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (extensions, inventory.SkippedEnumerations);
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
