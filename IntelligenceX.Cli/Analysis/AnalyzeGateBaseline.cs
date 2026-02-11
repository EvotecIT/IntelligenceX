using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Analysis;
using IntelligenceX.Json;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Analysis;

internal static class AnalyzeGateBaseline {
    private const string BaselineSchemaValue = "intelligencex.analysis-baseline.v1";
    private const string FindingsSchemaValue = "intelligencex.findings.v1";
    private static readonly JsonSerializerOptions BaselineJsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryWriteBaselineFile(string path, IReadOnlyList<AnalysisFinding> findings, out string? error) {
        error = null;
        if (string.IsNullOrWhiteSpace(path)) {
            error = "baseline path is empty";
            return false;
        }

        try {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            var byKey = new Dictionary<string, BaselineItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var finding in findings ?? Array.Empty<AnalysisFinding>()) {
                var key = BuildBaselineKey(finding);
                if (string.IsNullOrWhiteSpace(key) || byKey.ContainsKey(key)) {
                    continue;
                }
                byKey[key] = new BaselineItem {
                    Key = key,
                    Path = finding.Path,
                    Line = finding.Line,
                    RuleId = finding.RuleId ?? string.Empty,
                    Tool = finding.Tool ?? string.Empty,
                    Severity = AnalysisSeverity.Normalize(finding.Severity),
                    Fingerprint = finding.Fingerprint
                };
            }

            var envelope = new BaselineEnvelope {
                Schema = BaselineSchemaValue,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                Items = byKey.Values
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            File.WriteAllText(path, JsonSerializer.Serialize(envelope, BaselineJsonOptions));
            return true;
        } catch (Exception ex) {
            error = $"could not write baseline file ({FormatExceptionMessage(ex)})";
            return false;
        }
    }

    public static bool TryLoadBaselineKeys(string path, out HashSet<string> keys, out string schema, out bool schemaInferred,
        out string? error) {
        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        schema = string.Empty;
        schemaInferred = false;
        error = null;

        if (!File.Exists(path)) {
            error = $"baseline file not found: {path}";
            return false;
        }

        JsonObject? root;
        try {
            root = JsonLite.Parse(File.ReadAllText(path))?.AsObject();
        } catch (Exception ex) {
            error = $"could not parse baseline file ({FormatExceptionMessage(ex)})";
            return false;
        }
        if (root is null) {
            error = "baseline file root must be a JSON object";
            return false;
        }

        schema = root.GetString("schema") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(schema) &&
            !schema.Equals(BaselineSchemaValue, StringComparison.OrdinalIgnoreCase) &&
            !schema.Equals(FindingsSchemaValue, StringComparison.OrdinalIgnoreCase)) {
            error = $"unsupported baseline schema '{schema}'";
            return false;
        }
        if (string.IsNullOrWhiteSpace(schema)) {
            schema = FindingsSchemaValue;
            schemaInferred = true;
        }

        var items = root.GetArray("items");
        if (items is null || items.Count == 0) {
            return true;
        }

        foreach (var item in items) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            var key = obj.GetString("key");
            if (string.IsNullOrWhiteSpace(key)) {
                key = BuildBaselineKey(new AnalysisFinding(
                    Path: obj.GetString("path") ?? string.Empty,
                    Line: (int)(obj.GetInt64("line") ?? 0),
                    Message: obj.GetString("message") ?? string.Empty,
                    Severity: obj.GetString("severity") ?? "unknown",
                    RuleId: obj.GetString("ruleId"),
                    Tool: obj.GetString("tool"),
                    Fingerprint: obj.GetString("fingerprint")));
            }
            key = NormalizeBaselineKey(key);

            if (!string.IsNullOrWhiteSpace(key)) {
                keys.Add(key);
            }
        }

        return true;
    }

    public static string BuildBaselineKey(AnalysisFinding finding) {
        var path = NormalizePathForBaselineKey(finding.Path);
        var ruleId = (finding.RuleId ?? string.Empty).Trim();
        var tool = (finding.Tool ?? string.Empty).Trim();
        var line = finding.Line < 0 ? 0 : finding.Line;
        var fingerprint = (finding.Fingerprint ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fingerprint)) {
            return $"{ruleId}|{path}|{line}|{tool}|fp:{fingerprint}";
        }

        var message = (finding.Message ?? string.Empty)
            .Trim()
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\n', ' ');
        return $"{ruleId}|{path}|{line}|{tool}|msg:{message}";
    }

    private static string NormalizePathForBaselineKey(string? path) {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        var hasDotRelativePrefix = normalized.StartsWith(".", StringComparison.Ordinal);
        while (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        if (hasDotRelativePrefix) {
            normalized = normalized.TrimStart('/');
        }
        return normalized.ToLowerInvariant();
    }

    private static string NormalizeBaselineKey(string? key) {
        var trimmed = (key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return string.Empty;
        }

        // Key shape is: ruleId|path|line|tool|fingerprint-or-message...
        var first = trimmed.IndexOf('|');
        if (first < 0) {
            return trimmed;
        }
        var second = trimmed.IndexOf('|', first + 1);
        if (second < 0) {
            return trimmed;
        }

        var path = trimmed.Substring(first + 1, second - first - 1);
        return trimmed.Substring(0, first + 1) +
               NormalizePathForBaselineKey(path) +
               trimmed.Substring(second);
    }

    private static string FormatExceptionMessage(Exception ex) {
        var message = (ex.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(message)) {
            return ex.GetType().Name;
        }
        return $"{ex.GetType().Name}: {message}";
    }

    private sealed class BaselineEnvelope {
        public string Schema { get; set; } = BaselineSchemaValue;
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public List<BaselineItem> Items { get; set; } = new();
    }

    private sealed class BaselineItem {
        public string Key { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Line { get; set; }
        public string RuleId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;
        public string Severity { get; set; } = "unknown";
        public string? Fingerprint { get; set; }
    }
}
