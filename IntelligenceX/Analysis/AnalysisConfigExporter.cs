using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Analysis;

/// <summary>
/// Result of exporting analyzer configuration files.
/// </summary>
public sealed class AnalysisConfigExportResult {
    /// <summary>
    /// Creates a new export result.
    /// </summary>
    public AnalysisConfigExportResult(string outputDirectory, IReadOnlyList<string> files, int ruleCount,
        IReadOnlyList<string> warnings) {
        OutputDirectory = outputDirectory;
        Files = files;
        RuleCount = ruleCount;
        Warnings = warnings;
    }

    /// <summary>
    /// Directory that received exported config files.
    /// </summary>
    public string OutputDirectory { get; }
    /// <summary>
    /// Generated config file paths.
    /// </summary>
    public IReadOnlyList<string> Files { get; }
    /// <summary>
    /// Number of rules exported.
    /// </summary>
    public int RuleCount { get; }
    /// <summary>
    /// Non-fatal warnings produced during export.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Exports analyzer configuration files based on analysis settings and packs.
/// </summary>
public static class AnalysisConfigExporter {
    /// <summary>
    /// Writes analyzer configuration files into the output directory.
    /// </summary>
    public static AnalysisConfigExportResult Export(AnalysisSettings settings, AnalysisCatalog catalog, string outputDirectory) {
        if (settings is null) {
            throw new ArgumentNullException(nameof(settings));
        }
        if (catalog is null) {
            throw new ArgumentNullException(nameof(catalog));
        }
        if (string.IsNullOrWhiteSpace(outputDirectory)) {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);

        var policy = AnalysisPolicyBuilder.Build(settings, catalog);
        var warnings = new List<string>(policy.Warnings);
        var selected = policy.Rules;
        var files = new List<string>();

        var byLanguage = selected.Values
            .GroupBy(entry => entry.Rule.Language.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var csharpRules = CollectRules(byLanguage, "csharp", "cs");
        if (csharpRules.Count > 0) {
            var path = Path.Combine(outputDirectory, ".editorconfig");
            WriteEditorConfig(path, csharpRules);
            files.Add(path);
        }

        var psRules = CollectRules(byLanguage, "powershell", "ps");
        if (psRules.Count > 0) {
            var path = Path.Combine(outputDirectory, "PSScriptAnalyzerSettings.psd1");
            WritePSScriptAnalyzerConfig(path, psRules);
            files.Add(path);
        }

        return new AnalysisConfigExportResult(outputDirectory, files, selected.Count, warnings);
    }

    private static void WriteEditorConfig(string path, IReadOnlyList<AnalysisRuleSelection> rules) {
        var lines = new List<string> {
            "root = true",
            "",
            "[*.cs]"
        };
        foreach (var rule in rules.OrderBy(rule => GetToolRuleId(rule.Rule), StringComparer.OrdinalIgnoreCase)) {
            var severity = MapEditorConfigSeverity(rule.Severity);
            if (string.IsNullOrWhiteSpace(severity)) {
                continue;
            }
            var ruleId = GetToolRuleId(rule.Rule);
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            lines.Add($"dotnet_diagnostic.{ruleId}.severity = {severity}");
        }
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static void WritePSScriptAnalyzerConfig(string path, IReadOnlyList<AnalysisRuleSelection> rules) {
        var lines = new List<string> { "@{", "  Rules = @{" };
        foreach (var rule in rules.OrderBy(rule => GetToolRuleId(rule.Rule), StringComparer.OrdinalIgnoreCase)) {
            var severity = MapPowerShellSeverity(rule.Severity);
            if (string.IsNullOrWhiteSpace(severity)) {
                continue;
            }
            var ruleId = GetToolRuleId(rule.Rule);
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            lines.Add($"    {ruleId} = @{{ Severity = '{severity}' }}");
        }
        lines.Add("  }");
        lines.Add("}");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static string MapEditorConfigSeverity(string severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "warning";
        }
        return severity.Trim().ToLowerInvariant() switch {
            "critical" => "error",
            "error" => "error",
            "high" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "medium" => "warning",
            "info" => "suggestion",
            "information" => "suggestion",
            "low" => "suggestion",
            "suggestion" => "suggestion",
            "none" => "none",
            _ => "warning"
        };
    }

    private static string? MapPowerShellSeverity(string severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "Warning";
        }
        return severity.Trim().ToLowerInvariant() switch {
            "critical" => "Error",
            "error" => "Error",
            "high" => "Error",
            "warning" => "Warning",
            "warn" => "Warning",
            "medium" => "Warning",
            "info" => "Information",
            "information" => "Information",
            "low" => "Information",
            "none" => null,
            _ => "Warning"
        };
    }

    private static List<AnalysisRuleSelection> CollectRules(
        IReadOnlyDictionary<string, List<AnalysisPolicyRule>> byLanguage,
        params string[] keys) {
        var combined = new Dictionary<string, AnalysisRuleSelection>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys) {
            if (!byLanguage.TryGetValue(key, out var rules)) {
                continue;
            }
            foreach (var rule in rules) {
                if (rule?.Rule is null) {
                    continue;
                }
                combined[rule.Rule.Id] = new AnalysisRuleSelection(rule.Rule, rule.Severity);
            }
        }
        return combined.Values.ToList();
    }

    private static string GetToolRuleId(AnalysisRule rule) {
        if (rule is null) {
            return string.Empty;
        }
        return string.IsNullOrWhiteSpace(rule.ToolRuleId) ? rule.Id : rule.ToolRuleId;
    }

    private sealed class AnalysisRuleSelection {
        public AnalysisRuleSelection(AnalysisRule rule, string severity) {
            Rule = rule;
            Severity = severity;
        }

        public AnalysisRule Rule { get; }
        public string Severity { get; }
    }
}
