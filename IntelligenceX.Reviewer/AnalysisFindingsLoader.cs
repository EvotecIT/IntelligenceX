using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static class AnalysisFindingsLoader {
    private static readonly char[] GlobChars = { '*', '?', '[', ']' };

    public static IReadOnlyList<AnalysisFinding> Load(ReviewSettings settings, IReadOnlyList<PullRequestFile> files) {
        return LoadWithReport(settings, files).Findings;
    }

    public static AnalysisLoadResult LoadWithReport(ReviewSettings settings, IReadOnlyList<PullRequestFile> files) {
        if (settings?.Analysis?.Enabled != true) {
            return new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(0, 0, 0, 0));
        }
        var results = settings.Analysis.Results;
        var inputs = results?.Inputs;
        var configuredInputs = inputs?.Count ?? 0;
        if (inputs is null || inputs.Count == 0) {
            return new AnalysisLoadResult(Array.Empty<AnalysisFinding>(),
                new AnalysisLoadReport(configuredInputs, 0, 0, 0));
        }

        var workspace = ResolveWorkspaceRoot();
        var catalog = TryLoadCatalog(workspace);
        var toolRuleIndex = BuildToolRuleIndex(catalog);
        var minRank = AnalysisSeverity.Rank(results?.MinSeverity);
        var changed = BuildChangedPathSet(files, workspace);
        var disabledRules = new HashSet<string>(settings.Analysis.DisabledRules ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var severityOverrides = settings.Analysis.SeverityOverrides ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<AnalysisFinding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedFiles = ResolveInputFiles(workspace, inputs);
        var uniqueResolvedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsedInputFiles = 0;
        var failedInputFiles = 0;

        foreach (var file in resolvedFiles) {
            if (!uniqueResolvedFiles.Add(file)) {
                continue;
            }
            string text;
            try {
                text = File.ReadAllText(file);
            } catch (Exception ex) when (IsRecoverableFileReadException(ex)) {
                failedInputFiles++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            IReadOnlyList<AnalysisFinding> parsed;
            try {
                parsed = ParseFindings(text, workspace);
            } catch (Exception ex) when (IsRecoverableParseException(ex)) {
                failedInputFiles++;
                continue;
            }

            foreach (var finding in parsed) {
                if (string.IsNullOrWhiteSpace(finding.Path)) {
                    continue;
                }
                var normalizedPath = NormalizePath(finding.Path, workspace);
                if (string.IsNullOrWhiteSpace(normalizedPath)) {
                    continue;
                }
                var resolvedRuleId = ResolveCatalogRuleId(catalog, toolRuleIndex, finding.RuleId, finding.Tool);
                var primaryRuleId = string.IsNullOrWhiteSpace(resolvedRuleId) ? finding.RuleId : resolvedRuleId;
                var secondaryRuleId = string.IsNullOrWhiteSpace(resolvedRuleId) ||
                                      string.Equals(resolvedRuleId, finding.RuleId, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : finding.RuleId;
                if (!string.IsNullOrWhiteSpace(primaryRuleId) && disabledRules.Contains(primaryRuleId)) {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(secondaryRuleId) && disabledRules.Contains(secondaryRuleId)) {
                    continue;
                }
                if (changed.Count > 0 && !changed.Contains(normalizedPath)) {
                    continue;
                }
                var normalizedSeverity = AnalysisSeverity.Normalize(finding.Severity);
                if (!string.IsNullOrWhiteSpace(primaryRuleId) &&
                    severityOverrides.TryGetValue(primaryRuleId, out var overrideSeverity) &&
                    !string.IsNullOrWhiteSpace(overrideSeverity)) {
                    normalizedSeverity = AnalysisSeverity.Normalize(overrideSeverity);
                } else if (!string.IsNullOrWhiteSpace(secondaryRuleId) &&
                           severityOverrides.TryGetValue(secondaryRuleId, out var secondaryOverride) &&
                           !string.IsNullOrWhiteSpace(secondaryOverride)) {
                    normalizedSeverity = AnalysisSeverity.Normalize(secondaryOverride);
                }
                if (AnalysisSeverity.Rank(normalizedSeverity) < minRank) {
                    continue;
                }
                var normalizedFinding = finding with {
                    Path = normalizedPath,
                    Severity = normalizedSeverity,
                    RuleId = primaryRuleId
                };
                var key = BuildFindingKey(normalizedFinding);
                if (seen.Add(key)) {
                    findings.Add(normalizedFinding);
                }
            }
            parsedInputFiles++;
        }

        return new AnalysisLoadResult(findings,
            new AnalysisLoadReport(configuredInputs, uniqueResolvedFiles.Count, parsedInputFiles, failedInputFiles));
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
        foreach (var obj in EnumerateObjects(items)) {
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
        foreach (var run in EnumerateObjects(runs)) {
            var toolName = run.GetObject("tool")?.GetObject("driver")?.GetString("name");
            var results = run.GetArray("results");
            if (results is null || results.Count == 0) {
                continue;
            }
            foreach (var result in EnumerateObjects(results)) {
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
                var location = EnumerateObjects(locations).FirstOrDefault();
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
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && parsed.IsAbsoluteUri && parsed.IsFile) {
            return NormalizePath(parsed.LocalPath, workspace);
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
        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.IsFile) {
            trimmed = uri.LocalPath.Replace('\\', '/');
        }
        if (Path.IsPathRooted(trimmed)) {
            try {
                var full = Path.GetFullPath(trimmed);
                var baseFull = Path.GetFullPath(workspace);
                trimmed = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(baseFull, full).Replace('\\', '/')
                    : full.Replace('\\', '/');
            } catch {
                trimmed = trimmed.Replace('\\', '/');
            }
        }
        if (trimmed.StartsWith("./", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(2);
        }
        return trimmed;
    }

    private static AnalysisCatalog? TryLoadCatalog(string workspace) {
        try {
            return AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        } catch {
            return null;
        }
    }

    private static Dictionary<string, List<AnalysisRule>> BuildToolRuleIndex(AnalysisCatalog? catalog) {
        var index = new Dictionary<string, List<AnalysisRule>>(StringComparer.OrdinalIgnoreCase);
        if (catalog is null) {
            return index;
        }
        foreach (var rule in catalog.Rules.Values) {
            if (string.IsNullOrWhiteSpace(rule.ToolRuleId)) {
                continue;
            }
            if (!index.TryGetValue(rule.ToolRuleId, out var list)) {
                list = new List<AnalysisRule>();
                index[rule.ToolRuleId] = list;
            }
            list.Add(rule);
        }
        return index;
    }

    private static string? ResolveCatalogRuleId(AnalysisCatalog? catalog,
        IReadOnlyDictionary<string, List<AnalysisRule>> toolRuleIndex,
        string? ruleId,
        string? tool) {
        if (catalog is null || string.IsNullOrWhiteSpace(ruleId)) {
            return ruleId;
        }
        if (catalog.TryGetRule(ruleId, out var direct)) {
            return direct.Id;
        }
        if (!toolRuleIndex.TryGetValue(ruleId, out var candidates) || candidates.Count == 0) {
            return ruleId;
        }
        if (!string.IsNullOrWhiteSpace(tool)) {
            var match = candidates.FirstOrDefault(rule =>
                string.Equals(rule.Tool, tool, StringComparison.OrdinalIgnoreCase));
            if (match is not null) {
                return match.Id;
            }
        }
        return candidates.Count == 1 ? candidates[0].Id : ruleId;
    }

    private static bool IsRecoverableFileReadException(Exception ex) {
        return ex is IOException or UnauthorizedAccessException;
    }

    private static bool IsRecoverableParseException(Exception ex) {
        // Each file chooses one parse path (SARIF vs findings JSON), so parse failures
        // should increment counters once per unique file and never cascade across parser attempts.
        // Keep this narrow to payload-shape/parser errors only; unexpected exceptions
        // (for example InvalidDataException from future parser refactors) should surface.
        return ex is FormatException or JsonException;
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

    private static IEnumerable<JsonObject> EnumerateObjects(JsonArray? array) {
        if (array is null) {
            yield break;
        }
        foreach (var item in array) {
            var obj = item.AsObject();
            if (obj is not null) {
                yield return obj;
            }
        }
    }
}
