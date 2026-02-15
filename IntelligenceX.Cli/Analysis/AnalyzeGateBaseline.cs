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
            if (item is null) {
                continue;
            }
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            var key = obj.GetString("key");
            if (string.IsNullOrWhiteSpace(key)) {
                key = BuildBaselineKey(new AnalysisFinding(
                    Path: obj.GetString("path") ?? string.Empty,
                    Line: ReadLineForBaselineItem(obj),
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

    public static bool TryLoadDuplicationOverallBaselines(string path, out Dictionary<string, DuplicationOverallBaseline> baselines,
        out string? error) {
        baselines = new Dictionary<string, DuplicationOverallBaseline>(StringComparer.OrdinalIgnoreCase);
        error = null;

        if (string.IsNullOrWhiteSpace(path)) {
            error = "baseline path not provided";
            return false;
        }
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

        var items = root.GetArray("items");
        if (items is null || items.Count == 0) {
            return true;
        }

        foreach (var item in items) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }
            var findingPath = (obj.GetString("path") ?? string.Empty).Trim().Replace('\\', '/');
            if (!findingPath.Equals(".intelligencex/duplication-overall", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var ruleId = (obj.GetString("ruleId") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            var fingerprint = (obj.GetString("fingerprint") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fingerprint)) {
                continue;
            }
            if (!TryParseDuplicationOverallFingerprint(fingerprint, out var duplicated, out var significant, out var windowLines,
                    out var scope)) {
                continue;
            }
            if (significant <= 0) {
                continue;
            }
            var percent = Math.Round((duplicated * 100.0) / significant, 2, MidpointRounding.AwayFromZero);
            var key = $"{ruleId}|{scope}";
            baselines[key] = new DuplicationOverallBaseline(ruleId, scope, significant, duplicated, percent, windowLines, fingerprint);
        }

        return true;
    }

    public static bool TryLoadDuplicationFileBaselines(string path, out Dictionary<string, DuplicationFileBaseline> baselines,
        out string? error) {
        baselines = new Dictionary<string, DuplicationFileBaseline>(StringComparer.OrdinalIgnoreCase);
        error = null;

        if (string.IsNullOrWhiteSpace(path)) {
            error = "baseline path not provided";
            return false;
        }
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

        var items = root.GetArray("items");
        if (items is null || items.Count == 0) {
            return true;
        }

        foreach (var item in items) {
            if (item is null) {
                continue;
            }
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }
            var findingPath = (obj.GetString("path") ?? string.Empty).Trim().Replace('\\', '/');
            if (!findingPath.Equals(".intelligencex/duplication-file", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var ruleId = (obj.GetString("ruleId") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            var fingerprint = (obj.GetString("fingerprint") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fingerprint)) {
                continue;
            }
            if (!TryParseDuplicationFileFingerprint(fingerprint, out var filePath, out var duplicated, out var significant,
                    out var windowLines, out var scope)) {
                continue;
            }
            var normalizedPath = NormalizeDuplicationPathForKey(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || significant <= 0) {
                continue;
            }
            var percent = Math.Round((duplicated * 100.0) / significant, 2, MidpointRounding.AwayFromZero);
            var key = $"{ruleId}|{scope}|{normalizedPath}";
            baselines[key] = new DuplicationFileBaseline(ruleId, scope, normalizedPath, significant, duplicated, percent, windowLines,
                fingerprint);
        }

        return true;
    }

    internal static string NormalizeDuplicationPathForKey(string? path) {
        // Duplication metrics + baselines can include "./" prefixes, mixed separators, repeated slashes, or safe ../ segments.
        // Normalize so delta gating finds matches deterministically while avoiding path traversal above the root.
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');

        var hasDotRelativePrefix = normalized.StartsWith("./", StringComparison.Ordinal);
        var hasLeadingSlash = normalized.StartsWith("/", StringComparison.Ordinal);

        while (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        while (normalized.Contains("//", StringComparison.Ordinal)) {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) {
            return string.Empty;
        }

        var stack = new List<string>(parts.Length);
        foreach (var part in parts) {
            if (part.Equals(".", StringComparison.Ordinal)) {
                continue;
            }
            if (part.Equals("..", StringComparison.Ordinal)) {
                if (stack.Count == 0) {
                    return string.Empty;
                }
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }

        var rebuilt = string.Join("/", stack);
        if (hasDotRelativePrefix) {
            rebuilt = rebuilt.TrimStart('/');
        }
        if (hasLeadingSlash && !rebuilt.StartsWith("/", StringComparison.Ordinal)) {
            rebuilt = "/" + rebuilt;
        }
        return rebuilt;
    }

    private static bool TryParseDuplicationOverallFingerprint(string fingerprint, out int duplicatedLines, out int significantLines,
        out int windowLines, out string scope) {
        duplicatedLines = 0;
        significantLines = 0;
        windowLines = 0;
        scope = "all";
        var tokens = (fingerprint ?? string.Empty).Split(':');
        if (tokens.Length < 4) {
            return false;
        }
        if (!tokens[1].Equals("overall", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (!int.TryParse(tokens[2], out duplicatedLines) || duplicatedLines < 0) {
            return false;
        }
        if (!int.TryParse(tokens[3], out significantLines) || significantLines < 0) {
            return false;
        }

        // Accepted shapes:
        // - <ruleId>:overall:<duplicated>:<significant>
        // - <ruleId>:overall:<duplicated>:<significant>:<windowLines>
        // - <ruleId>:overall:<duplicated>:<significant>:scope:changed-files
        // - <ruleId>:overall:<duplicated>:<significant>:<windowLines>:scope:changed-files
        if (tokens.Length == 4) {
            return true;
        }
        if (tokens.Length == 5) {
            return int.TryParse(tokens[4], out windowLines) && windowLines >= 0;
        }
        if (tokens.Length == 6) {
            if (!tokens[4].Equals("scope", StringComparison.OrdinalIgnoreCase) ||
                !tokens[5].Equals("changed-files", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            scope = "changed-files";
            return true;
        }
        if (tokens.Length == 7) {
            if (!int.TryParse(tokens[4], out windowLines) || windowLines < 0) {
                return false;
            }
            if (!tokens[5].Equals("scope", StringComparison.OrdinalIgnoreCase) ||
                !tokens[6].Equals("changed-files", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            scope = "changed-files";
            return true;
        }
        return false;
    }

    private static bool TryParseDuplicationFileFingerprint(string fingerprint, out string path, out int duplicatedLines,
        out int significantLines, out int windowLines, out string scope) {
        path = string.Empty;
        duplicatedLines = 0;
        significantLines = 0;
        windowLines = 0;
        scope = "all";

        var tokens = (fingerprint ?? string.Empty).Split(':');
        if (tokens.Length < 5) {
            return false;
        }
        var format = tokens[1].Trim();
        var isFileUri = format.Equals("file-uri", StringComparison.OrdinalIgnoreCase);
        if (!isFileUri && !format.Equals("file", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // Optional suffix: :scope:changed-files
        var effectiveLength = tokens.Length;
        if (effectiveLength >= 2 &&
            tokens[effectiveLength - 2].Equals("scope", StringComparison.OrdinalIgnoreCase) &&
            tokens[effectiveLength - 1].Equals("changed-files", StringComparison.OrdinalIgnoreCase)) {
            scope = "changed-files";
            effectiveLength -= 2;
        }
        if (effectiveLength < 5) {
            return false;
        }

        if (isFileUri) {
            // Shape: <ruleId>:file-uri:<escapedPath>:<duplicated>:<significant>[:<windowLines>][:scope:changed-files]
            if (effectiveLength != 5 && effectiveLength != 6) {
                return false;
            }
            if (!int.TryParse(tokens[3], out duplicatedLines) || duplicatedLines < 0) {
                return false;
            }
            if (!int.TryParse(tokens[4], out significantLines) || significantLines < 0) {
                return false;
            }
            if (effectiveLength == 6) {
                if (!int.TryParse(tokens[5], out windowLines) || windowLines < 0) {
                    return false;
                }
            }
            try {
                path = Uri.UnescapeDataString(tokens[2]).Trim().Replace('\\', '/');
            } catch {
                return false;
            }
            return true;
        }

        // File paths may contain ":" (for example Windows drive letters).
        // Parse the numeric tail and treat the rest as the path token(s).
        var hasWindow = effectiveLength >= 6 &&
                        int.TryParse(tokens[effectiveLength - 1], out windowLines) && windowLines >= 0 &&
                        int.TryParse(tokens[effectiveLength - 2], out significantLines) && significantLines >= 0 &&
                        int.TryParse(tokens[effectiveLength - 3], out duplicatedLines) && duplicatedLines >= 0;
        if (!hasWindow) {
            windowLines = 0;
            if (!int.TryParse(tokens[effectiveLength - 1], out significantLines) || significantLines < 0) {
                return false;
            }
            if (!int.TryParse(tokens[effectiveLength - 2], out duplicatedLines) || duplicatedLines < 0) {
                return false;
            }
        }

        var pathTokenCount = effectiveLength - (hasWindow ? 5 : 4);
        if (pathTokenCount <= 0) {
            return false;
        }
        path = string.Join(":", tokens, 2, pathTokenCount).Trim().Replace('\\', '/');
        return true;
    }

    private static string NormalizePathForBaselineKey(string? path) {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        var hasDotRelativePrefix = normalized.StartsWith("./", StringComparison.Ordinal);
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

    private static int ReadLineForBaselineItem(JsonObject obj) {
        var line = obj.GetInt64("line");
        if (!line.HasValue || line.Value <= 0) {
            return 0;
        }
        if (line.Value > int.MaxValue) {
            return int.MaxValue;
        }
        return (int)line.Value;
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

    public sealed record DuplicationOverallBaseline(
        string RuleId,
        string Scope,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);

    public sealed record DuplicationFileBaseline(
        string RuleId,
        string Scope,
        string Path,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);
}

