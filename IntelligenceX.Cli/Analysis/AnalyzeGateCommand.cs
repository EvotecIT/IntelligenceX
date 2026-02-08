using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Analysis;

internal static class AnalyzeGateCommand {
    private const int ExitSuccess = 0;
    private const int ExitError = 1;
    private const int ExitGateFailed = 2;

    public static Task<int> RunAsync(string[] args) {
        var options = ParseArgs(args, out var error);
        if (options.ShowHelp) {
            PrintHelp();
            return Task.FromResult(ExitSuccess);
        }
        if (error is not null) {
            Console.WriteLine(error);
            PrintHelp();
            return Task.FromResult(ExitError);
        }

        var workspace = AnalyzeRunner.ResolveWorkspace(options.Workspace);
        var configPath = AnalyzeRunner.ResolveConfigPath(options.ConfigPath, workspace);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            Console.WriteLine($"Config not found: {configPath ?? "<null>"}");
            return Task.FromResult(ExitError);
        }

        JsonObject? root;
        try {
            root = JsonLite.Parse(File.ReadAllText(configPath))?.AsObject();
        } catch (Exception ex) {
            Console.WriteLine($"Failed to parse config: {ex.Message}");
            return Task.FromResult(ExitError);
        }
        if (root is null) {
            Console.WriteLine("Config root must be a JSON object.");
            return Task.FromResult(ExitError);
        }

        var reviewObj = root.GetObject("review") ?? root;
        var analysisSettings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root, reviewObj, analysisSettings);

        if (!analysisSettings.Enabled) {
            Console.WriteLine("Static analysis is disabled (analysis.enabled=false). Gate skipped.");
            return Task.FromResult(ExitSuccess);
        }
        if (!analysisSettings.Gate.Enabled) {
            Console.WriteLine("Static analysis gate is disabled (analysis.gate.enabled=false).");
            return Task.FromResult(ExitSuccess);
        }

        AnalysisCatalog catalog;
        try {
            catalog = AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        } catch (Exception ex) {
            Console.WriteLine($"Static analysis gate failed: could not load analysis catalog ({ex.Message}).");
            return Task.FromResult(ExitGateFailed);
        }

        AnalysisPolicy policy;
        try {
            policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(analysisSettings, catalog);
        } catch (Exception ex) {
            Console.WriteLine($"Static analysis gate failed: could not build analysis policy ({ex.Message}).");
            return Task.FromResult(ExitGateFailed);
        }

        if (policy.Rules.Count == 0 && analysisSettings.Gate.FailOnNoEnabledRules) {
            Console.WriteLine("Static analysis gate failed: no enabled rules configured (analysis.packs empty or packs contain no rules).");
            return Task.FromResult(ExitGateFailed);
        }

        var minSeverity = string.IsNullOrWhiteSpace(analysisSettings.Gate.MinSeverity)
            ? analysisSettings.Results.MinSeverity
            : analysisSettings.Gate.MinSeverity;
        var minRank = AnalysisSeverity.Rank(minSeverity);

        var prFiles = LoadChangedFiles(options.ChangedFilesPath, workspace);
        var reviewSettings = new ReviewSettings();
        reviewSettings.Analysis.Enabled = true;
        reviewSettings.Analysis.Results.Inputs = analysisSettings.Results.Inputs;
        reviewSettings.Analysis.Results.MinSeverity = minSeverity;
        reviewSettings.Analysis.DisabledRules = analysisSettings.DisabledRules;
        reviewSettings.Analysis.SeverityOverrides = analysisSettings.SeverityOverrides;

        var previousCwd = Environment.CurrentDirectory;
        AnalysisLoadResult load;
        try {
            Environment.CurrentDirectory = workspace;
            load = AnalysisFindingsLoader.LoadWithReport(reviewSettings, prFiles);
        } catch (Exception ex) {
            var msg = (ex.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(msg)) {
                msg = ex.GetType().Name;
            } else {
                msg = $"{ex.GetType().Name}: {msg}";
            }
            Console.WriteLine($"Static analysis gate: unavailable ({msg}).");
            return Task.FromResult(analysisSettings.Gate.FailOnUnavailable ? ExitGateFailed : ExitSuccess);
        } finally {
            Environment.CurrentDirectory = previousCwd;
        }

        if (load.Report.ResolvedInputFiles == 0) {
            var msg = "no analysis result files matched configured inputs";
            Console.WriteLine($"Static analysis gate: unavailable ({msg}).");
            return Task.FromResult(analysisSettings.Gate.FailOnUnavailable ? ExitGateFailed : ExitSuccess);
        }
        if (load.Report.FailedInputFiles > 0 && analysisSettings.Gate.FailOnUnavailable) {
            Console.WriteLine($"Static analysis gate: unavailable ({load.Report.FailedInputFiles} result file(s) failed load/parse).");
            return Task.FromResult(ExitGateFailed);
        }

        var allFindings = load.Findings ?? Array.Empty<AnalysisFinding>();
        var allowedTypes = NormalizeTypes(analysisSettings.Gate.Types);
        var includeAllTypes = allowedTypes.Count == 0;

        var enabledRuleIds = new HashSet<string>(policy.Rules.Keys, StringComparer.OrdinalIgnoreCase);
        var violations = new List<AnalysisFinding>();
        var outsidePack = 0;

        foreach (var finding in allFindings) {
            if (AnalysisSeverity.Rank(finding.Severity) < minRank) {
                continue;
            }
            if (string.IsNullOrWhiteSpace(finding.RuleId)) {
                continue;
            }
            var ruleId = finding.RuleId!.Trim();
            var isEnabled = enabledRuleIds.Contains(ruleId);
            if (!isEnabled) {
                outsidePack++;
                if (!analysisSettings.Gate.IncludeOutsidePackRules) {
                    continue;
                }
            }

            var type = ResolveRuleType(ruleId, catalog, fallback: "unknown");
            if (!includeAllTypes && !allowedTypes.Contains(type)) {
                continue;
            }
            violations.Add(finding);
        }

        var hotspotFailures = EvaluateHotspotsToReview(
            analysisSettings,
            workspace,
            catalog,
            allFindings,
            enabledRuleIds,
            minRank,
            allowedTypes,
            includeAllTypes);
        var hasHotspotFailures = hotspotFailures.Count > 0;

        if (violations.Count == 0 && !hasHotspotFailures) {
            Console.WriteLine("Static analysis gate: pass");
            Console.WriteLine($"- Findings considered: {allFindings.Count}");
            Console.WriteLine($"- Violations: 0");
            if (outsidePack > 0) {
                Console.WriteLine($"- Outside-pack findings: {outsidePack} (ignored)");
            }
            return Task.FromResult(ExitSuccess);
        }

        Console.WriteLine("Static analysis gate: fail");
        Console.WriteLine($"- Findings considered: {allFindings.Count}");
        Console.WriteLine($"- Violations: {violations.Count}" + (hasHotspotFailures ? $", hotspots to-review: {hotspotFailures.Count}" : string.Empty));
        if (outsidePack > 0) {
            Console.WriteLine($"- Outside-pack findings: {outsidePack}" + (analysisSettings.Gate.IncludeOutsidePackRules ? " (included)" : " (ignored)"));
        }

        PrintViolationSummary(violations, catalog, maxRules: 10, maxItems: 20);
        if (hasHotspotFailures) {
            Console.WriteLine();
            Console.WriteLine("Hotspots to-review (blocking):");
            foreach (var item in hotspotFailures.Take(10)) {
                Console.WriteLine($"- {item}");
            }
            if (hotspotFailures.Count > 10) {
                Console.WriteLine($"- (truncated, {hotspotFailures.Count - 10} more)");
            }
        }

        return Task.FromResult(ExitGateFailed);
    }

    public static void PrintHelp() {
        Console.WriteLine("  intelligencex analyze gate [--workspace <path>] [--config <path>] [--changed-files <path>]");
    }

    private static void PrintViolationSummary(IReadOnlyList<AnalysisFinding> violations, AnalysisCatalog catalog,
        int maxRules, int maxItems) {
        if (violations is null || violations.Count == 0) {
            return;
        }

        var byRule = violations
            .Where(v => !string.IsNullOrWhiteSpace(v.RuleId))
            .GroupBy(v => v.RuleId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (RuleId: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Violations by rule:");
        foreach (var item in byRule.Take(maxRules)) {
            Console.WriteLine($"- {DescribeRule(item.RuleId, catalog)}={item.Count}");
        }
        if (byRule.Count > maxRules) {
            Console.WriteLine($"- (truncated, {byRule.Count - maxRules} more)");
        }

        Console.WriteLine();
        Console.WriteLine("Top violations:");
        foreach (var finding in violations
                     .OrderByDescending(f => AnalysisSeverity.Rank(f.Severity))
                     .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.Line)
                     .Take(maxItems)) {
            var sev = AnalysisSeverity.Normalize(finding.Severity);
            Console.WriteLine($"- {finding.Path}:{finding.Line} {finding.RuleId} {sev} {finding.Message}");
        }
        if (violations.Count > maxItems) {
            Console.WriteLine($"- (truncated, {violations.Count - maxItems} more)");
        }
    }

    private static string DescribeRule(string ruleId, AnalysisCatalog catalog) {
        if (catalog is not null && catalog.TryGetRule(ruleId, out var rule) && !string.IsNullOrWhiteSpace(rule.Title)) {
            return $"{rule.Id} ({rule.Title})";
        }
        return ruleId;
    }

    private static string ResolveRuleType(string ruleId, AnalysisCatalog catalog, string fallback) {
        if (catalog is null || string.IsNullOrWhiteSpace(ruleId)) {
            return fallback;
        }
        if (catalog.TryGetRule(ruleId, out var rule) && !string.IsNullOrWhiteSpace(rule.Type)) {
            return rule.Type.Trim().ToLowerInvariant();
        }
        return fallback;
    }

    private static HashSet<string> NormalizeTypes(IReadOnlyList<string>? types) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(type)) {
                continue;
            }
            set.Add(type.Trim().ToLowerInvariant());
        }
        return set;
    }

    private static IReadOnlyList<PullRequestFile> LoadChangedFiles(string? path, string workspace) {
        if (string.IsNullOrWhiteSpace(path)) {
            return Array.Empty<PullRequestFile>();
        }
        var resolved = Path.IsPathRooted(path) ? path : Path.Combine(workspace, path);
        if (!File.Exists(resolved)) {
            Console.WriteLine($"Warning: changed files list not found: {resolved}");
            return Array.Empty<PullRequestFile>();
        }
        var list = new List<PullRequestFile>();
        foreach (var line in File.ReadAllLines(resolved)) {
            var file = (line ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(file)) {
                continue;
            }
            list.Add(new PullRequestFile(file, "modified", patch: null));
        }
        return list;
    }

    private static List<string> EvaluateHotspotsToReview(
        AnalysisSettings settings,
        string workspace,
        AnalysisCatalog catalog,
        IReadOnlyList<AnalysisFinding> findings,
        HashSet<string> enabledRuleIds,
        int minRank,
        HashSet<string> allowedTypes,
        bool includeAllTypes) {
        var list = new List<string>();
        if (settings?.Gate?.FailOnHotspotsToReview != true) {
            return list;
        }
        if (findings is null || findings.Count == 0) {
            return list;
        }

        var hotspotFindings = new List<AnalysisFinding>();
        foreach (var finding in findings) {
            if (AnalysisSeverity.Rank(finding.Severity) < minRank) {
                continue;
            }
            if (string.IsNullOrWhiteSpace(finding.RuleId)) {
                continue;
            }

            var ruleId = finding.RuleId!.Trim();
            var resolvedType = ResolveRuleType(
                ruleId,
                catalog,
                fallback: ruleId.StartsWith("IXHOT", StringComparison.OrdinalIgnoreCase) ? "security-hotspot" : string.Empty);
            var isHotspot = resolvedType == "security-hotspot" || ruleId.StartsWith("IXHOT", StringComparison.OrdinalIgnoreCase);
            if (!isHotspot) {
                continue;
            }
            if (!includeAllTypes && !allowedTypes.Contains(resolvedType)) {
                continue;
            }

            var isEnabled = enabledRuleIds is not null && enabledRuleIds.Contains(ruleId);
            if (!isEnabled && settings.Gate.IncludeOutsidePackRules != true) {
                continue;
            }

            hotspotFindings.Add(finding);
        }
        if (hotspotFindings.Count == 0) {
            return list;
        }

        var statePath = ResolveWorkspaceBoundPath(workspace, settings.Hotspots.StatePath);
        if (statePath is null) {
            // When hotspot gating is enabled, treat an out-of-workspace path as a hard failure signal.
            list.Add("hotspots statePath resolves outside workspace");
            return list;
        }
        var stateFile = HotspotStateStore.TryLoad(statePath);
        var map = HotspotStateStore.ToMap(stateFile.Items);

        foreach (var finding in hotspotFindings) {
            var key = AnalysisHotspots.ComputeHotspotKey(finding);
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }
            if (map.TryGetValue(key, out var entry)) {
                var status = AnalysisHotspots.NormalizeStatus(entry.Status);
                if (string.Equals(status, "to-review", StringComparison.OrdinalIgnoreCase)) {
                    list.Add($"{finding.RuleId}:{finding.Path}:{finding.Line} ({key})");
                }
            } else {
                // Missing entry should be handled by hotspots sync-state --check; treat as to-review to be safe.
                list.Add($"{finding.RuleId}:{finding.Path}:{finding.Line} ({key})");
            }
        }
        return list;
    }

    private static string? ResolveWorkspaceBoundPath(string workspace, string configuredPath) {
        if (string.IsNullOrWhiteSpace(configuredPath)) {
            configuredPath = ".intelligencex/hotspots.json";
        }
        var resolved = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(workspace, configuredPath);
        try {
            var fullWorkspace = Path.GetFullPath(workspace);
            var full = Path.GetFullPath(resolved);

            // Use relative path computation to avoid prefix-matching bypasses (e.g. /ws2 matching /ws).
            var relative = Path.GetRelativePath(fullWorkspace, full);
            if (string.IsNullOrWhiteSpace(relative)) {
                return null;
            }
            var normalized = relative.Replace('\\', '/');
            if (normalized.Equals(".", StringComparison.Ordinal)) {
                return full;
            }
            if (normalized.Equals("..", StringComparison.Ordinal) || normalized.StartsWith("../", StringComparison.Ordinal)) {
                return null;
            }
            if (Path.IsPathRooted(relative)) {
                return null;
            }

            return full;
        } catch {
            return null;
        }
    }

    private static GateOptions ParseArgs(string[] args, out string? error) {
        error = null;
        var options = new GateOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelpToken(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = "Missing value for --workspace.";
                    return options;
                }
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = "Missing value for --config.";
                    return options;
                }
                options.ConfigPath = args[++i];
                continue;
            }
            if (arg.Equals("--changed-files", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = "Missing value for --changed-files.";
                    return options;
                }
                options.ChangedFilesPath = args[++i];
                continue;
            }
            error = $"Unknown option '{arg}' for gate.";
            return options;
        }
        return options;
    }

    private static bool IsHelpToken(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GateOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? ChangedFilesPath { get; set; }
    }
}
