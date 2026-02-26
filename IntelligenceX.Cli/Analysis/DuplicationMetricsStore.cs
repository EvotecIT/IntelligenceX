using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IntelligenceX.Cli.Analysis;

internal static class DuplicationMetricsStore {
    internal const string SchemaValue = "intelligencex.duplication.v2";
    internal const string LegacySchemaValue = "intelligencex.duplication.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Write(string path, IReadOnlyList<DuplicationRuleMetrics> rules) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        var payload = new DuplicationMetricsDocument {
            Schema = SchemaValue,
            Rules = rules?.ToList() ?? new List<DuplicationRuleMetrics>()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static bool TryRead(string path, out DuplicationMetricsDocument? document, out string? error) {
        document = null;
        error = null;
        if (!File.Exists(path)) {
            error = $"duplication metrics file not found: {path}";
            return false;
        }

        try {
            var parsed = JsonSerializer.Deserialize<DuplicationMetricsDocument>(File.ReadAllText(path), JsonOptions);
            if (parsed is null) {
                error = "duplication metrics root is empty";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(parsed.Schema) &&
                !string.Equals(parsed.Schema, SchemaValue, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.Schema, LegacySchemaValue, StringComparison.OrdinalIgnoreCase)) {
                error = $"unsupported duplication metrics schema '{parsed.Schema}'";
                return false;
            }
            parsed.Schema = SchemaValue;
            parsed.Rules ??= new List<DuplicationRuleMetrics>();
            foreach (var rule in parsed.Rules) {
                rule.Files ??= new List<DuplicationFileMetrics>();
                foreach (var file in rule.Files) {
                    if (string.IsNullOrWhiteSpace(file.Language)) {
                        file.Language = ResolveLanguageFromPath(file.Path);
                    }
                }
            }
            document = parsed;
            return true;
        } catch (Exception ex) {
            error = $"could not parse duplication metrics file ({FormatExceptionMessage(ex)})";
            return false;
        }
    }

    private static string FormatExceptionMessage(Exception ex) {
        var message = (ex.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(message)) {
            return ex.GetType().Name;
        }
        return $"{ex.GetType().Name}: {message}";
    }

    private static string ResolveLanguageFromPath(string? path) {
        return SourceLanguageConventions.ResolveLanguageFromPath(path);
    }
}

internal sealed class DuplicationMetricsDocument {
    public string Schema { get; set; } = DuplicationMetricsStore.SchemaValue;
    public List<DuplicationRuleMetrics> Rules { get; set; } = new();
}

internal sealed class DuplicationRuleMetrics {
    public string RuleId { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public int WindowLines { get; set; }
    public double ConfiguredMaxPercent { get; set; }
    public int TotalSignificantLines { get; set; }
    public int DuplicatedSignificantLines { get; set; }
    public double OverallDuplicatedPercent { get; set; }
    public int DuplicatedWindowGroups { get; set; }
    public int DuplicatedWindowOccurrences { get; set; }
    public List<DuplicationFileMetrics> Files { get; set; } = new();
}

internal sealed class DuplicationFileMetrics {
    public string Path { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public double? ConfiguredMaxPercent { get; set; }
    public int FirstDuplicatedLine { get; set; }
    public int SignificantLines { get; set; }
    public int DuplicatedLines { get; set; }
    public double DuplicatedPercent { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}
