using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeGateCommand {
    public static void PrintHelp() {
        Console.WriteLine("  intelligencex analyze gate [--workspace <path>] [--config <path>] [--changed-files <path>]");
        Console.WriteLine("                             [--new-only] [--baseline <path>] [--write-baseline <path>]");
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

    private static void PrintDuplicationViolationSummary(IReadOnlyList<AnalysisFinding> violations, int maxItems) {
        if (violations is null || violations.Count == 0) {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Duplication gate violations:");
        foreach (var finding in violations
                     .OrderBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.Line)
                     .Take(maxItems)) {
            Console.WriteLine($"- {finding.RuleId} {finding.Path}:{finding.Line} {finding.Message}");
        }
        if (violations.Count > maxItems) {
            Console.WriteLine($"- (truncated, {violations.Count - maxItems} more)");
        }
    }

    private static DuplicationGateEvaluation EvaluateDuplicationGate(AnalysisSettings settings, string workspace,
        IReadOnlySet<string> changedPaths) {
        if (settings is null || settings.Gate?.Duplication?.Enabled != true) {
            return DuplicationGateEvaluation.Disabled;
        }

        var duplication = settings.Gate.Duplication;
        var scope = NormalizeDuplicationScope(duplication.Scope);
        var useChangedFileScope = scope == "changed-files" && changedPaths is { Count: > 0 };
        var effectiveScope = useChangedFileScope ? "changed-files" : "all";
        var metricsPath = ResolveWorkspaceBoundFilePath(workspace, duplication.MetricsPath);
        if (metricsPath is null) {
            return DuplicationGateEvaluation.Unavailable($"metrics path resolves outside workspace: {duplication.MetricsPath}");
        }

        if (!DuplicationMetricsStore.TryRead(metricsPath, out var metrics, out var readError) || metrics is null) {
            return DuplicationGateEvaluation.Unavailable(readError ?? "could not load duplication metrics");
        }

        var configuredRuleIds = NormalizeRuleIds(duplication.RuleIds);
        var availableRules = metrics.Rules ?? new List<DuplicationRuleMetrics>();
        var selectedRules = availableRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.RuleId))
            .Where(rule => configuredRuleIds.Count == 0 || configuredRuleIds.Contains(rule.RuleId.Trim()))
            .ToList();
        if (selectedRules.Count == 0) {
            var reason = configuredRuleIds.Count == 0
                ? $"no duplication rule metrics found in {metricsPath}"
                : $"no duplication metrics matched configured ruleIds ({string.Join(", ", configuredRuleIds)})";
            return DuplicationGateEvaluation.Unavailable(reason);
        }

        var violations = new List<AnalysisFinding>();
        var overallSnapshots = new List<DuplicationOverallSnapshot>();
        var fileSnapshots = new List<DuplicationFileSnapshot>();
        foreach (var rule in selectedRules) {
            var ruleId = rule.RuleId.Trim();
            var tool = string.IsNullOrWhiteSpace(rule.Tool) ? "IntelligenceX.Maintainability" : rule.Tool.Trim();
            var files = (rule.Files ?? new List<DuplicationFileMetrics>())
                .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
                .Where(file => !useChangedFileScope || changedPaths.Contains(NormalizeChangedPath(file.Path)))
                .ToList();
            foreach (var file in files) {
                // Normalize deterministically so baseline snapshot matching and file-delta keys don't depend on how the analyzer emits paths.
                var normalizedPath = AnalyzeGateBaseline.NormalizeDuplicationPathForKey(file.Path);
                if (string.IsNullOrWhiteSpace(normalizedPath)) {
                    normalizedPath = (file.Path ?? string.Empty).Trim().Replace('\\', '/');
                }

                if (file.SignificantLines > 0) {
                    var snapshotFingerprint = BuildDuplicationFileSnapshotFingerprint(
                        ruleId,
                        normalizedPath,
                        file.DuplicatedLines,
                        file.SignificantLines,
                        rule.WindowLines,
                        effectiveScope);
                    fileSnapshots.Add(new DuplicationFileSnapshot(
                        RuleId: ruleId,
                        Tool: tool,
                        Scope: effectiveScope,
                        Path: normalizedPath,
                        SignificantLines: file.SignificantLines,
                        DuplicatedLines: file.DuplicatedLines,
                        DuplicatedPercent: file.DuplicatedPercent,
                        WindowLines: rule.WindowLines,
                        Fingerprint: snapshotFingerprint));
                }

                var fileThreshold = duplication.MaxFilePercent ??
                                    file.ConfiguredMaxPercent ??
                                    rule.ConfiguredMaxPercent;
                if (file.SignificantLines <= 0 || file.DuplicatedLines <= 0) {
                    continue;
                }
                if (file.DuplicatedPercent - fileThreshold <= double.Epsilon) {
                    continue;
                }

                var path = normalizedPath;
                var line = file.FirstDuplicatedLine > 0 ? file.FirstDuplicatedLine : 1;
                var fingerprint = !string.IsNullOrWhiteSpace(file.Fingerprint)
                    ? file.Fingerprint
                    : $"{ruleId}:{path}:{file.DuplicatedLines}:{file.SignificantLines}:{rule.WindowLines}";
                var message =
                    $"Duplicated significant lines: {file.DuplicatedLines}/{file.SignificantLines} ({FormatPercent(file.DuplicatedPercent)}%) exceeds limit {FormatPercent(fileThreshold)}%.";
                violations.Add(new AnalysisFinding(path, line, message, "warning", ruleId, tool, fingerprint));
            }

            var scopedTotalSignificantLines = files.Sum(file => Math.Max(file.SignificantLines, 0));
            var scopedDuplicatedSignificantLines = files.Sum(file => Math.Max(file.DuplicatedLines, 0));
            var scopedOverallPercent = scopedTotalSignificantLines <= 0
                ? 0
                : Math.Round((scopedDuplicatedSignificantLines * 100.0) / scopedTotalSignificantLines, 2,
                    MidpointRounding.AwayFromZero);
            var overallScopeSuffix = useChangedFileScope ? ":scope:changed-files" : string.Empty;
            var overallFingerprint =
                $"{ruleId}:overall:{scopedDuplicatedSignificantLines}:{scopedTotalSignificantLines}:{rule.WindowLines}{overallScopeSuffix}";
            overallSnapshots.Add(new DuplicationOverallSnapshot(
                RuleId: ruleId,
                Tool: tool,
                Scope: effectiveScope,
                SignificantLines: scopedTotalSignificantLines,
                DuplicatedLines: scopedDuplicatedSignificantLines,
                DuplicatedPercent: scopedOverallPercent,
                WindowLines: rule.WindowLines,
                Fingerprint: overallFingerprint));
            if (duplication.MaxOverallPercent.HasValue &&
                scopedTotalSignificantLines > 0 &&
                scopedDuplicatedSignificantLines > 0 &&
                scopedOverallPercent - duplication.MaxOverallPercent.Value > double.Epsilon) {
                var overallThreshold = duplication.MaxOverallPercent.Value;
                var scopeLabel = useChangedFileScope ? " (scope=changed-files)" : string.Empty;
                var overallMessage =
                    $"Overall duplicated significant lines{scopeLabel}: {scopedDuplicatedSignificantLines}/{scopedTotalSignificantLines} ({FormatPercent(scopedOverallPercent)}%) exceeds limit {FormatPercent(overallThreshold)}%.";
                violations.Add(new AnalysisFinding(DuplicationOverallPath, 0, overallMessage, "warning", ruleId, tool,
                    overallFingerprint));
            }
        }

        return DuplicationGateEvaluation.WithData(metricsPath, selectedRules.Count, violations, overallSnapshots, fileSnapshots,
            effectiveScope);
    }

    private static int ResolveUnavailableExit(AnalysisSettings settings, bool findingsRequireBaseline,
        bool duplicationRequiresBaseline) {
        var failOnUnavailable = (findingsRequireBaseline && settings.Gate.FailOnUnavailable) ||
                                (duplicationRequiresBaseline && settings.Gate.Duplication.FailOnUnavailable);
        return failOnUnavailable ? ExitGateFailed : ExitSuccess;
    }

    private static HashSet<string> NormalizeRuleIds(IReadOnlyList<string>? ruleIds) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ruleId in ruleIds ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            set.Add(ruleId.Trim());
        }
        return set;
    }

    private static string NormalizeDuplicationScope(string? scope) {
        if (string.IsNullOrWhiteSpace(scope)) {
            return "changed-files";
        }
        var normalized = scope.Trim().ToLowerInvariant();
        return normalized switch {
            "all" => "all",
            "changedfiles" => "changed-files",
            "changed-files" => "changed-files",
            "changed" => "changed-files",
            _ => "changed-files"
        };
    }

    private static string BuildDuplicationFileSnapshotFingerprint(string ruleId, string path, int duplicatedLines, int significantLines,
        int windowLines, string scope) {
        // Encode the path so the fingerprint remains unambiguous when ':' appears in file names.
        var normalizedPath = AnalyzeGateBaseline.NormalizeDuplicationPathForKey(path);
        if (string.IsNullOrWhiteSpace(normalizedPath)) {
            normalizedPath = (path ?? string.Empty).Trim().Replace('\\', '/');
        }
        var encodedPath = Uri.EscapeDataString(normalizedPath);
        var scopeSuffix = scope == "changed-files" ? ":scope:changed-files" : string.Empty;
        return $"{ruleId}:file-uri:{encodedPath}:{duplicatedLines}:{significantLines}:{windowLines}{scopeSuffix}";
    }

    private static HashSet<string> BuildChangedPathSet(IReadOnlyList<PullRequestFile> files) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files ?? Array.Empty<PullRequestFile>()) {
            if (string.IsNullOrWhiteSpace(file.Filename)) {
                continue;
            }
            set.Add(NormalizeChangedPath(file.Filename));
        }
        return set;
    }

    private static string NormalizeChangedPath(string path) {
        var normalized = AnalyzeGateBaseline.NormalizeDuplicationPathForKey(path);
        if (!string.IsNullOrWhiteSpace(normalized)) {
            return normalized;
        }
        normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        return normalized;
    }

    private static double ClampPercentIncrease(double value, string settingName) {
        if (double.IsNaN(value) || double.IsInfinity(value)) {
            Console.WriteLine($"Static analysis duplication delta gate: invalid {settingName}={value}; clamped to 0.");
            return 0;
        }
        if (value < 0) {
            Console.WriteLine($"Static analysis duplication delta gate: invalid {settingName}={value}; clamped to 0.");
            return 0;
        }
        if (value > 100) {
            Console.WriteLine($"Static analysis duplication delta gate: invalid {settingName}={value}; clamped to 100.");
            return 100;
        }
        return value;
    }

    private static string FormatPercent(double value) {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
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

    private static GateFindingFilters CreateGateFindingFilters(
        IReadOnlySet<string> allowedTypes,
        IReadOnlySet<string> gateRuleIds) {
        var hasTypeFilter = allowedTypes is { Count: > 0 };
        var hasRuleIdFilter = gateRuleIds is { Count: > 0 };
        return new GateFindingFilters(
            allowedTypes,
            gateRuleIds,
            hasTypeFilter,
            hasRuleIdFilter,
            IncludeAllFilters: !hasTypeFilter && !hasRuleIdFilter);
    }

    private static string FormatFilterSummary(IReadOnlyCollection<string> values, bool includeAllWhenEmpty) {
        if (values is null || values.Count == 0) {
            return includeAllWhenEmpty ? "all" : "none";
        }
        return string.Join(", ", values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<PullRequestFile> LoadChangedFiles(string? path, string workspace, out string? error) {
        error = null;
        if (string.IsNullOrWhiteSpace(path)) {
            return Array.Empty<PullRequestFile>();
        }
        var resolved = ResolveWorkspaceBoundFilePath(workspace, path);
        if (resolved is null) {
            error = $"Invalid changed files list path outside workspace: {path}";
            return Array.Empty<PullRequestFile>();
        }
        if (!File.Exists(resolved)) {
            Console.WriteLine($"Warning: changed files list not found: {resolved}");
            return Array.Empty<PullRequestFile>();
        }
        var list = new List<PullRequestFile>();
        var fullWorkspace = Path.GetFullPath(workspace);
        foreach (var line in File.ReadAllLines(resolved)) {
            var file = (line ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(file) || file.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            var relativePath = TryNormalizeChangedFilePath(fullWorkspace, file, out var entryError);
            if (relativePath is null) {
                error = entryError;
                return Array.Empty<PullRequestFile>();
            }
            if (string.IsNullOrWhiteSpace(relativePath)) {
                continue;
            }
            list.Add(new PullRequestFile(relativePath, "modified", patch: null));
        }
        return list;
    }

    private static string? TryNormalizeChangedFilePath(string fullWorkspace, string file, out string? error) {
        error = null;
        try {
            var candidateFullPath = Path.IsPathRooted(file)
                ? Path.GetFullPath(file)
                : Path.GetFullPath(Path.Combine(fullWorkspace, file));
            var relative = Path.GetRelativePath(fullWorkspace, candidateFullPath).Replace('\\', '/');
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith("../", StringComparison.Ordinal)) {
                error = $"Invalid changed file entry outside workspace: {file}";
                return null;
            }

            var normalized = relative.Trim('/');
            if (normalized.StartsWith("./", StringComparison.Ordinal)) {
                normalized = normalized.Substring(2);
            }
            if (normalized.Equals(".", StringComparison.Ordinal)) {
                return string.Empty;
            }
            return normalized;
        } catch (Exception ex) {
            error = $"Invalid changed file entry '{file}': {ex.Message}";
            return null;
        }
    }

    private static List<string> EvaluateHotspotsToReview(
        AnalysisSettings settings,
        string workspace,
        AnalysisCatalog catalog,
        IReadOnlyList<AnalysisFinding> findings,
        HashSet<string> enabledRuleIds,
        int minRank,
        GateFindingFilters gateFilters) {
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

            if (!gateFilters.Matches(ruleId, resolvedType)) {
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

    private static string? ResolveWorkspaceBoundFilePath(string workspace, string configuredPath) {
        if (string.IsNullOrWhiteSpace(configuredPath)) {
            return null;
        }
        var resolved = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(workspace, configuredPath);
        try {
            var fullWorkspace = Path.GetFullPath(workspace);
            var full = Path.GetFullPath(resolved);
            var relative = Path.GetRelativePath(fullWorkspace, full);
            if (string.IsNullOrWhiteSpace(relative)) {
                relative = ".";
            }
            var normalized = relative.Replace('\\', '/');
            if (normalized.Equals(".", StringComparison.Ordinal)) {
                return full;
            }
            if (Path.IsPathRooted(relative) ||
                normalized.Equals("..", StringComparison.Ordinal) ||
                normalized.StartsWith("../", StringComparison.Ordinal)) {
                return null;
            }
            return full;
        } catch {
            return null;
        }
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
                relative = ".";
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
            if (arg.Equals("--new-only", StringComparison.OrdinalIgnoreCase)) {
                options.NewIssuesOnly = true;
                continue;
            }
            if (arg.Equals("--baseline", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = "Missing value for --baseline.";
                    return options;
                }
                options.BaselinePath = args[++i];
                continue;
            }
            if (arg.Equals("--write-baseline", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    error = "Missing value for --write-baseline.";
                    return options;
                }
                options.WriteBaselinePath = args[++i];
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

    private sealed record DuplicationGateEvaluation(
        bool Available,
        string? MetricsPath,
        int RulesEvaluated,
        IReadOnlyList<AnalysisFinding> Violations,
        IReadOnlyList<DuplicationOverallSnapshot> OverallSnapshots,
        IReadOnlyList<DuplicationFileSnapshot> FileSnapshots,
        string Scope,
        string? UnavailableReason) {
        public static DuplicationGateEvaluation Disabled => new(
            Available: true,
            MetricsPath: null,
            RulesEvaluated: 0,
            Violations: Array.Empty<AnalysisFinding>(),
            OverallSnapshots: Array.Empty<DuplicationOverallSnapshot>(),
            FileSnapshots: Array.Empty<DuplicationFileSnapshot>(),
            Scope: "changed-files",
            UnavailableReason: null);

        public static DuplicationGateEvaluation WithData(string metricsPath, int rulesEvaluated,
            IReadOnlyList<AnalysisFinding> violations,
            IReadOnlyList<DuplicationOverallSnapshot> overallSnapshots,
            IReadOnlyList<DuplicationFileSnapshot> fileSnapshots,
            string scope) {
            return new DuplicationGateEvaluation(
                Available: true,
                MetricsPath: metricsPath,
                RulesEvaluated: rulesEvaluated,
                Violations: violations ?? Array.Empty<AnalysisFinding>(),
                OverallSnapshots: overallSnapshots ?? Array.Empty<DuplicationOverallSnapshot>(),
                FileSnapshots: fileSnapshots ?? Array.Empty<DuplicationFileSnapshot>(),
                Scope: scope,
                UnavailableReason: null);
        }

        public static DuplicationGateEvaluation Unavailable(string reason) {
            return new DuplicationGateEvaluation(
                Available: false,
                MetricsPath: null,
                RulesEvaluated: 0,
                Violations: Array.Empty<AnalysisFinding>(),
                OverallSnapshots: Array.Empty<DuplicationOverallSnapshot>(),
                FileSnapshots: Array.Empty<DuplicationFileSnapshot>(),
                Scope: "changed-files",
                UnavailableReason: reason);
        }
    }

    private sealed record DuplicationOverallSnapshot(
        string RuleId,
        string Tool,
        string Scope,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);

    private sealed record DuplicationFileSnapshot(
        string RuleId,
        string Tool,
        string Scope,
        string Path,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);

    private sealed class GateOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? ChangedFilesPath { get; set; }
        public bool NewIssuesOnly { get; set; }
        public string? BaselinePath { get; set; }
        public string? WriteBaselinePath { get; set; }
    }

    private readonly record struct GateFindingFilters(
        IReadOnlySet<string> AllowedTypes,
        IReadOnlySet<string> RuleIds,
        bool HasTypeFilter,
        bool HasRuleIdFilter,
        bool IncludeAllFilters) {
        public bool Matches(string ruleId, string? ruleType) {
            if (IncludeAllFilters) {
                return true;
            }

            if (HasTypeFilter &&
                !string.IsNullOrWhiteSpace(ruleType) &&
                AllowedTypes.Contains(ruleType.Trim())) {
                return true;
            }

            return HasRuleIdFilter &&
                   !string.IsNullOrWhiteSpace(ruleId) &&
                   RuleIds.Contains(ruleId.Trim());
        }
    }
}
