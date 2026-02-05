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

        var warnings = new List<string>();
        var selected = BuildRuleSelection(settings, catalog, warnings);
        var files = new List<string>();

        var byLanguage = selected.Values
            .GroupBy(entry => entry.Rule.Language.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        if (byLanguage.TryGetValue("csharp", out var csharpRules) ||
            byLanguage.TryGetValue("cs", out csharpRules)) {
            var path = Path.Combine(outputDirectory, ".editorconfig");
            WriteEditorConfig(path, csharpRules);
            files.Add(path);
        }

        if (byLanguage.TryGetValue("powershell", out var psRules) ||
            byLanguage.TryGetValue("ps", out psRules)) {
            var path = Path.Combine(outputDirectory, "PSScriptAnalyzerSettings.psd1");
            WritePSScriptAnalyzerConfig(path, psRules);
            files.Add(path);
        }

        return new AnalysisConfigExportResult(outputDirectory, files, selected.Count, warnings);
    }

    private static Dictionary<string, AnalysisRuleSelection> BuildRuleSelection(AnalysisSettings settings,
        AnalysisCatalog catalog, List<string> warnings) {
        var selected = new Dictionary<string, AnalysisRuleSelection>(StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(settings.DisabledRules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var packId in settings.Packs ?? Array.Empty<string>()) {
            if (!catalog.TryGetPack(packId, out var pack)) {
                warnings.Add($"Pack not found: {packId}");
                continue;
            }
            foreach (var ruleId in pack.Rules) {
                if (disabled.Contains(ruleId)) {
                    continue;
                }
                if (!catalog.TryGetRule(ruleId, out var rule)) {
                    warnings.Add($"Rule not found: {ruleId}");
                    continue;
                }
                var severity = rule.DefaultSeverity;
                if (pack.SeverityOverrides.TryGetValue(ruleId, out var packSeverity) &&
                    !string.IsNullOrWhiteSpace(packSeverity)) {
                    severity = packSeverity;
                }
                selected[rule.Id] = new AnalysisRuleSelection(rule, severity);
            }
        }

        foreach (var entry in settings.SeverityOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) {
            if (!selected.TryGetValue(entry.Key, out var selection)) {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(entry.Value)) {
                selection.Severity = entry.Value;
            }
        }

        return selected;
    }

    private static void WriteEditorConfig(string path, IReadOnlyList<AnalysisRuleSelection> rules) {
        var lines = new List<string> {
            "root = true",
            "",
            "[*.cs]"
        };
        foreach (var rule in rules.OrderBy(rule => rule.Rule.Id, StringComparer.OrdinalIgnoreCase)) {
            var severity = MapEditorConfigSeverity(rule.Severity);
            if (string.IsNullOrWhiteSpace(severity)) {
                continue;
            }
            lines.Add($"dotnet_diagnostic.{rule.Rule.Id}.severity = {severity}");
        }
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    private static void WritePSScriptAnalyzerConfig(string path, IReadOnlyList<AnalysisRuleSelection> rules) {
        var lines = new List<string> { "@{", "  Rules = @{" };
        foreach (var rule in rules.OrderBy(rule => rule.Rule.Id, StringComparer.OrdinalIgnoreCase)) {
            var severity = MapPowerShellSeverity(rule.Severity);
            if (string.IsNullOrWhiteSpace(severity)) {
                continue;
            }
            lines.Add($"    {rule.Rule.Id} = @{{ Severity = '{severity}' }}");
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
            "error" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "info" => "suggestion",
            "information" => "suggestion",
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
            "error" => "Error",
            "warning" => "Warning",
            "warn" => "Warning",
            "info" => "Information",
            "information" => "Information",
            "none" => null,
            _ => "Warning"
        };
    }

    private sealed class AnalysisRuleSelection {
        public AnalysisRuleSelection(AnalysisRule rule, string severity) {
            Rule = rule;
            Severity = severity;
        }

        public AnalysisRule Rule { get; }
        public string Severity { get; set; }
    }
}
