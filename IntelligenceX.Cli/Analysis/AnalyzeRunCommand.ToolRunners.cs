using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static async Task<RunnerResult> RunCsharpAsync(AnalyzeRunOptions options, string workspace, string outputDirectory,
        WorkspaceSourceInventory? sourceInventory, AnalysisSettings settings, string? generatedEditorConfig, List<string> warnings) {
        var hasCsharpSources = TryDetectSourceFiles(
            workspace,
            sourceInventory,
            "C#",
            warnings,
            out var skippedSourceEnumerations,
            out _,
            SourceLanguageConventions.CSharpSourceExtensions);
        if (!hasCsharpSources) {
            if (skippedSourceEnumerations > 0) {
                warnings.Add($"C# source discovery skipped {skippedSourceEnumerations} path(s) due to access or IO errors.");
            }
            warnings.Add("No C# source files detected; skipping Roslyn analysis.");
            return new RunnerResult(true, string.Empty);
        }

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
                return new RunnerResult(false,
                    BuildExternalRunnerFailureMessage(
                        "C#",
                        options.DotnetCommand,
                        "--dotnet-command",
                        result));
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
                return new RunnerResult(false,
                    BuildExternalRunnerFailureMessage(
                        "C#",
                        options.DotnetCommand,
                        "--dotnet-command",
                        result));
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
        string findingsPath, WorkspaceSourceInventory? sourceInventory, AnalysisSettings settings, string? generatedSettingsPath,
        List<string> warnings) {
        var hasPowerShellSources = TryDetectSourceFiles(
            workspace,
            sourceInventory,
            "PowerShell",
            warnings,
            out var skippedSourceEnumerations,
            out _,
            SourceLanguageConventions.PowerShellSourceExtensions);
        if (!hasPowerShellSources) {
            if (skippedSourceEnumerations > 0) {
                warnings.Add($"PowerShell source discovery skipped {skippedSourceEnumerations} path(s) due to access or IO errors.");
            }
            warnings.Add("No PowerShell source files detected; skipping PSScriptAnalyzer analysis.");
            return new PowerShellRunnerResult(true, string.Empty, Array.Empty<AnalysisFindingItem>());
        }

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
                    BuildExternalRunnerFailureMessage(
                        "PowerShell",
                        options.PowerShellCommand,
                        "--pwsh-command",
                        result), Array.Empty<AnalysisFindingItem>());
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
        var hasJavaScriptSources = TryDetectSourceFiles(
            workspace,
            sourceInventory,
            "JavaScript/TypeScript",
            warnings,
            out var skippedSourceEnumerations,
            out _,
            SourceLanguageConventions.JavaScriptSourceExtensions);
        if (!hasJavaScriptSources) {
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
        var hasPythonSources = TryDetectSourceFiles(
            workspace,
            sourceInventory,
            "Python",
            warnings,
            out var skippedSourceEnumerations,
            out _,
            SourceLanguageConventions.PythonSourceExtensions);
        if (!hasPythonSources) {
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

    private static bool TryDetectSourceFiles(
        string workspace,
        WorkspaceSourceInventory? sourceInventory,
        string languageLabel,
        List<string> warnings,
        out int skippedSourceEnumerations,
        out bool usedDirectFallback,
        params string[] extensions) {
        usedDirectFallback = false;
        var scanLimitReached = false;
        var found = sourceInventory is null
            ? WorkspaceContainsAnySourceFile(workspace, out skippedSourceEnumerations, extensions)
            : WorkspaceContainsAnySourceFile(sourceInventory, out skippedSourceEnumerations, out scanLimitReached, extensions);

        if (!found && sourceInventory is not null && scanLimitReached) {
            usedDirectFallback = true;
            warnings.Add(
                $"Shared source inventory reached the configured file limit ({sourceInventory.MaxScannedFiles}); falling back to direct {languageLabel} source detection.");
            found = WorkspaceContainsAnySourceFileWithoutScanLimit(workspace, out var directSkipped, extensions);
            skippedSourceEnumerations += directSkipped;
        }

        return found;
    }

    private static List<string> BuildJavaScriptRunnerArgs(string sarifPath, IReadOnlyList<ExternalToolRuleSelector> selectors) {
        var args = new List<string> {
            "--yes",
            "eslint",
            ".",
            "--ext",
            SourceLanguageConventions.JavaScriptEslintExtensionsArg,
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

}
