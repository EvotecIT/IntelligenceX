using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static class AnalysisFindingsLoader {
    private static readonly char[] GlobChars = { '*', '?', '[', ']' };

    public static IReadOnlyList<AnalysisFinding> Load(ReviewSettings settings, IReadOnlyList<PullRequestFile> files) {
        if (settings is null || !settings.Analysis.Enabled) {
            return Array.Empty<AnalysisFinding>();
        }
        var inputs = settings.Analysis.Results.Inputs;
        if (inputs is null || inputs.Count == 0) {
            return Array.Empty<AnalysisFinding>();
        }

        var workspace = ResolveWorkspaceRoot();
        var minRank = AnalysisSeverity.Rank(settings.Analysis.Results.MinSeverity);
        var changed = BuildChangedPathSet(files, workspace);
        var disabledRules = new HashSet<string>(settings.Analysis.DisabledRules ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var severityOverrides = settings.Analysis.SeverityOverrides ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<AnalysisFinding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ResolveInputFiles(workspace, inputs)) {
            if (!File.Exists(file)) {
                continue;
            }
            try {
                var text = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(text)) {
                    continue;
                }
                var parsed = ParseFindings(text, workspace);
                foreach (var finding in parsed) {
                    if (string.IsNullOrWhiteSpace(finding.Path)) {
                        continue;
                    }
                    var normalizedPath = NormalizePath(finding.Path, workspace);
                    if (string.IsNullOrWhiteSpace(normalizedPath)) {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(finding.RuleId) && disabledRules.Contains(finding.RuleId)) {
                        continue;
                    }
                    if (changed.Count > 0 && !changed.Contains(normalizedPath)) {
                        continue;
                    }
                    var normalizedSeverity = AnalysisSeverity.Normalize(finding.Severity);
                    if (!string.IsNullOrWhiteSpace(finding.RuleId) &&
                        severityOverrides.TryGetValue(finding.RuleId, out var overrideSeverity) &&
                        !string.IsNullOrWhiteSpace(overrideSeverity)) {
                        normalizedSeverity = AnalysisSeverity.Normalize(overrideSeverity);
                    }
                    if (AnalysisSeverity.Rank(normalizedSeverity) < minRank) {
                        continue;
                    }
                    var normalizedFinding = finding with {
                        Path = normalizedPath,
                        Severity = normalizedSeverity
                    };
                    var key = BuildFindingKey(normalizedFinding);
                    if (seen.Add(key)) {
                        findings.Add(normalizedFinding);
                    }
                }
            } catch {
                // Ignore malformed analysis files to keep the review resilient.
            }
        }

        return findings;
    }

    private static IReadOnlyList<AnalysisFinding> ParseFindings(string text, string workspace) {
        var value = JsonLite.Parse(text);
        var root = value?.AsObject();
        if (root is null) {
            return Array.Empty<AnalysisFinding>();
        }
        var runs = root.GetArray("runs");
        if (runs is not null) {
            return ParseSarif(root, workspace);
        }
        return ParseFindingsJson(root);
    }

    private static IReadOnlyList<AnalysisFinding> ParseFindingsJson(JsonObject root) {
        var items = root.GetArray("items");
        if (items is null || items.Count == 0) {
            return Array.Empty<AnalysisFinding>();
        }
        var findings = new List<AnalysisFinding>();
        foreach (var item in items) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }
            var path = obj.GetString("path") ?? string.Empty;
            var line = (int)(obj.GetInt64("line") ?? 0);
            var severity = obj.GetString("severity") ?? "unknown";
            var message = obj.GetString("message") ?? string.Empty;
            var ruleId = obj.GetString("ruleId");
            var tool = obj.GetString("tool");
            var fingerprint = obj.GetString("fingerprint");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(message)) {
                continue;
            }
            findings.Add(new AnalysisFinding(path, line, message, severity, ruleId, tool, fingerprint));
        }
        return findings;
    }

    private static IReadOnlyList<AnalysisFinding> ParseSarif(JsonObject root, string workspace) {
        var runs = root.GetArray("runs");
        if (runs is null || runs.Count == 0) {
            return Array.Empty<AnalysisFinding>();
        }
        var findings = new List<AnalysisFinding>();
        foreach (var runValue in runs) {
            var run = runValue.AsObject();
            if (run is null) {
                continue;
            }
            var toolName = run.GetObject("tool")?.GetObject("driver")?.GetString("name");
            var results = run.GetArray("results");
            if (results is null || results.Count == 0) {
                continue;
            }
            foreach (var resultValue in results) {
                var result = resultValue.AsObject();
                if (result is null) {
                    continue;
                }
                var message = ReadSarifMessage(result);
                if (string.IsNullOrWhiteSpace(message)) {
                    continue;
                }
                var level = result.GetString("level") ?? "unknown";
                var ruleId = result.GetString("ruleId") ?? result.GetObject("rule")?.GetString("id");
                var locations = result.GetArray("locations");
                if (locations is null || locations.Count == 0) {
                    continue;
                }
                var location = locations[0].AsObject();
                if (location is null) {
                    continue;
                }
                var physical = location.GetObject("physicalLocation");
                if (physical is null) {
                    continue;
                }
                var uri = physical.GetObject("artifactLocation")?.GetString("uri") ?? string.Empty;
                var path = ResolveSarifPath(uri, workspace);
                var region = physical.GetObject("region");
                var line = (int)(region?.GetInt64("startLine") ?? 0);
                if (string.IsNullOrWhiteSpace(path)) {
                    continue;
                }
                findings.Add(new AnalysisFinding(path, line, message, level, ruleId, toolName));
            }
        }
        return findings;
    }

    private static string ReadSarifMessage(JsonObject result) {
        var messageObj = result.GetObject("message");
        var message = messageObj?.GetString("text") ?? messageObj?.GetString("markdown");
        if (!string.IsNullOrWhiteSpace(message)) {
            return message!;
        }
        return result.GetString("message") ?? string.Empty;
    }

    private static string ResolveSarifPath(string uri, string workspace) {
        if (string.IsNullOrWhiteSpace(uri)) {
            return string.Empty;
        }
        var trimmed = uri.Trim();
        var fragmentIndex = trimmed.IndexOf('#');
        if (fragmentIndex >= 0) {
            trimmed = trimmed.Substring(0, fragmentIndex);
        }
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && parsed.IsAbsoluteUri) {
            if (parsed.IsFile) {
                return NormalizePath(parsed.LocalPath, workspace);
            }
        }
        return NormalizePath(trimmed, workspace);
    }

    private static IReadOnlyList<string> ResolveInputFiles(string workspace, IReadOnlyList<string> inputs) {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs) {
            if (string.IsNullOrWhiteSpace(input)) {
                continue;
            }
            var trimmed = input.Trim();
            if (trimmed.IndexOfAny(GlobChars) < 0) {
                var candidate = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(workspace, trimmed);
                if (File.Exists(candidate)) {
                    results.Add(Path.GetFullPath(candidate));
                }
                continue;
            }
            var searchRoot = ResolveSearchRoot(workspace, trimmed);
            if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot)) {
                continue;
            }
            var normalizedPattern = NormalizeGlobPattern(trimmed, workspace);
            var useAbsolute = Path.IsPathRooted(trimmed);
            foreach (var file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)) {
                var full = Path.GetFullPath(file);
                var value = useAbsolute
                    ? full.Replace('\\', '/')
                    : Path.GetRelativePath(workspace, full).Replace('\\', '/');
                if (GlobMatcher.IsMatch(normalizedPattern, value)) {
                    results.Add(full);
                }
            }
        }
        return results.ToList();
    }

    private static string ResolveSearchRoot(string workspace, string pattern) {
        var normalized = pattern.Replace('\\', '/');
        var wildcardIndex = normalized.IndexOfAny(GlobChars);
        if (wildcardIndex < 0) {
            var basePath = Path.IsPathRooted(pattern) ? pattern : Path.Combine(workspace, pattern);
            return Path.GetDirectoryName(basePath) ?? workspace;
        }
        var prefix = normalized.Substring(0, wildcardIndex);
        var lastSlash = prefix.LastIndexOf('/');
        if (lastSlash <= 0) {
            return workspace;
        }
        var root = prefix.Substring(0, lastSlash);
        if (!Path.IsPathRooted(root)) {
            root = Path.Combine(workspace, root);
        }
        return root;
    }

    private static string NormalizeGlobPattern(string pattern, string workspace) {
        var normalized = pattern.Replace('\\', '/').Trim();
        if (!Path.IsPathRooted(normalized)) {
            return normalized;
        }
        var full = Path.GetFullPath(normalized);
        return full.Replace('\\', '/');
    }

    private static HashSet<string> BuildChangedPathSet(IReadOnlyList<PullRequestFile> files, string workspace) {
        if (files.Count == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Filename)) {
                continue;
            }
            var normalized = NormalizePath(file.Filename, workspace);
            if (!string.IsNullOrWhiteSpace(normalized)) {
                set.Add(normalized);
            }
        }
        return set;
    }

    private static string NormalizePath(string path, string workspace) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile) {
                trimmed = uri.LocalPath.Replace('\\', '/');
            }
        }
        if (Path.IsPathRooted(trimmed)) {
            try {
                var full = Path.GetFullPath(trimmed);
                var baseFull = Path.GetFullPath(workspace);
                if (full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)) {
                    trimmed = Path.GetRelativePath(baseFull, full).Replace('\\', '/');
                } else {
                    trimmed = full.Replace('\\', '/');
                }
            } catch {
                trimmed = trimmed.Replace('\\', '/');
            }
        }
        if (trimmed.StartsWith("./", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(2);
        }
        return trimmed;
    }

    private static string BuildFindingKey(AnalysisFinding finding) {
        var message = string.IsNullOrWhiteSpace(finding.Message) ? string.Empty : finding.Message.Trim();
        var rule = string.IsNullOrWhiteSpace(finding.RuleId) ? string.Empty : finding.RuleId.Trim();
        return $"{finding.Path}:{finding.Line}:{rule}:{message}";
    }

    private static string ResolveWorkspaceRoot() {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(workspace)) {
            return workspace;
        }
        return Environment.CurrentDirectory;
    }
}
