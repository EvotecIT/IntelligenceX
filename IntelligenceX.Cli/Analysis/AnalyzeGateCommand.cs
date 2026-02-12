using System;
using System.Collections.Generic;
using System.Globalization;
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
    private const string DuplicationOverallPath = ".intelligencex/duplication-overall";

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
        var workspaceBoundConfig = ResolveWorkspaceBoundFilePath(workspace, configPath);
        if (workspaceBoundConfig is null) {
            Console.WriteLine($"Config path resolves outside workspace: {configPath}");
            return Task.FromResult(ExitError);
        }
        configPath = workspaceBoundConfig;

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
            Console.WriteLine($"Static analysis gate: unavailable (could not load analysis catalog: {ex.Message}).");
            return Task.FromResult(analysisSettings.Gate.FailOnUnavailable ? ExitGateFailed : ExitSuccess);
        }

        AnalysisPolicy policy;
        try {
            policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(analysisSettings, catalog);
        } catch (Exception ex) {
            Console.WriteLine($"Static analysis gate: unavailable (could not build analysis policy: {ex.Message}).");
            return Task.FromResult(analysisSettings.Gate.FailOnUnavailable ? ExitGateFailed : ExitSuccess);
        }

        if (policy.Rules.Count == 0 && analysisSettings.Gate.FailOnNoEnabledRules) {
            Console.WriteLine("Static analysis gate failed: no enabled rules configured (analysis.packs empty or packs contain no rules).");
            return Task.FromResult(ExitGateFailed);
        }

        var minSeverity = string.IsNullOrWhiteSpace(analysisSettings.Gate.MinSeverity)
            ? analysisSettings.Results.MinSeverity
            : analysisSettings.Gate.MinSeverity;
        var minRank = AnalysisSeverity.Rank(minSeverity);

        var prFiles = LoadChangedFiles(options.ChangedFilesPath, workspace, out var changedFilesError);
        if (changedFilesError is not null) {
            Console.WriteLine(changedFilesError);
            return Task.FromResult(ExitError);
        }
        var changedPathSet = BuildChangedPathSet(prFiles);
        var reviewSettings = new ReviewSettings();
        reviewSettings.Analysis.Enabled = true;
        reviewSettings.Analysis.Results.Inputs = analysisSettings.Results.Inputs;
        reviewSettings.Analysis.Results.MinSeverity = minSeverity;
        reviewSettings.Analysis.DisabledRules = analysisSettings.DisabledRules;
        reviewSettings.Analysis.SeverityOverrides = analysisSettings.SeverityOverrides;

        AnalysisLoadResult load;
        try {
            load = AnalysisFindingsLoader.LoadWithReport(reviewSettings, prFiles, workspace);
        } catch (Exception ex) {
            var msg = (ex.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(msg)) {
                msg = ex.GetType().Name;
            } else {
                msg = $"{ex.GetType().Name}: {msg}";
            }
            Console.WriteLine($"Static analysis gate: unavailable ({msg}).");
            return Task.FromResult(analysisSettings.Gate.FailOnUnavailable ? ExitGateFailed : ExitSuccess);
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
        var useNewIssuesOnly = options.NewIssuesOnly || analysisSettings.Gate.NewIssuesOnly;
        var duplicationEnabled = analysisSettings.Gate.Duplication.Enabled;
        var duplicationUseNewIssuesOnly = duplicationEnabled &&
            (useNewIssuesOnly || analysisSettings.Gate.Duplication.NewIssuesOnly);
        var configuredBaselinePath = !string.IsNullOrWhiteSpace(options.BaselinePath)
            ? options.BaselinePath
            : analysisSettings.Gate.BaselinePath;
        var baselineRequired = useNewIssuesOnly || duplicationUseNewIssuesOnly;
        var baselineSuppressed = 0;
        var duplicationBaselineSuppressed = 0;
        var baselineLoadedCount = 0;
        var baselinePathResolved = string.Empty;
        var duplicationEvaluation = duplicationEnabled
            ? EvaluateDuplicationGate(analysisSettings, workspace, changedPathSet)
            : DuplicationGateEvaluation.Disabled;
        var duplicationViolations = duplicationEvaluation.Violations?.ToList() ?? new List<AnalysisFinding>();

        if (duplicationEnabled && !duplicationEvaluation.Available) {
            Console.WriteLine($"Static analysis duplication gate: unavailable ({duplicationEvaluation.UnavailableReason}).");
            if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                return Task.FromResult(ExitGateFailed);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.WriteBaselinePath)) {
            var writePath = ResolveWorkspaceBoundPath(workspace, options.WriteBaselinePath!);
            if (writePath is null) {
                Console.WriteLine($"Invalid baseline output path outside workspace: {options.WriteBaselinePath}");
                return Task.FromResult(ExitError);
            }
            var baselineItems = new List<AnalysisFinding>(violations);
            if (duplicationEvaluation.Available) {
                baselineItems.AddRange(duplicationViolations);
            }
            var writeResult = AnalyzeGateBaseline.TryWriteBaselineFile(writePath, baselineItems, out var writeError);
            if (!writeResult) {
                Console.WriteLine($"Failed to write baseline: {writeError}");
                return Task.FromResult(ExitError);
            }
            Console.WriteLine($"Static analysis baseline updated: {writePath} ({baselineItems.Count} item(s) considered).");
        }

        if (baselineRequired) {
            if (string.IsNullOrWhiteSpace(configuredBaselinePath)) {
                Console.WriteLine("Static analysis gate: unavailable (new-issues mode requires analysis.gate.baselinePath).");
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationUseNewIssuesOnly));
            }

            var baselinePath = ResolveWorkspaceBoundPath(workspace, configuredBaselinePath);
            if (baselinePath is null) {
                Console.WriteLine($"Static analysis gate: unavailable (baseline path resolves outside workspace: {configuredBaselinePath}).");
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationUseNewIssuesOnly));
            }
            baselinePathResolved = baselinePath;

            var baselineResult = AnalyzeGateBaseline.TryLoadBaselineKeys(
                baselinePath,
                out var baselineKeys,
                out var baselineSchema,
                out var baselineSchemaInferred,
                out var baselineError);
            if (!baselineResult) {
                Console.WriteLine($"Static analysis gate: unavailable ({baselineError}).");
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationUseNewIssuesOnly));
            }
            if (baselineSchemaInferred) {
                Console.WriteLine($"Static analysis baseline schema inferred as '{baselineSchema}' (schema property missing).");
            }
            baselineLoadedCount = baselineKeys.Count;
            Console.WriteLine($"Static analysis baseline loaded: {baselinePath} (schema={baselineSchema}, items={baselineLoadedCount}).");

            if (useNewIssuesOnly) {
                var newViolations = new List<AnalysisFinding>();
                foreach (var violation in violations) {
                    var key = AnalyzeGateBaseline.BuildBaselineKey(violation);
                    if (baselineKeys.Contains(key)) {
                        baselineSuppressed++;
                        continue;
                    }
                    newViolations.Add(violation);
                }
                violations = newViolations;
            }

            if (duplicationUseNewIssuesOnly && duplicationEvaluation.Available) {
                var newDuplicationViolations = new List<AnalysisFinding>();
                foreach (var violation in duplicationViolations) {
                    var key = AnalyzeGateBaseline.BuildBaselineKey(violation);
                    if (baselineKeys.Contains(key)) {
                        duplicationBaselineSuppressed++;
                        continue;
                    }
                    newDuplicationViolations.Add(violation);
                }
                duplicationViolations = newDuplicationViolations;
            }
        }

        if (violations.Count == 0 && !hasHotspotFailures && duplicationViolations.Count == 0) {
            Console.WriteLine("Static analysis gate: pass");
            Console.WriteLine($"- Findings considered: {allFindings.Count}");
            Console.WriteLine($"- Violations: 0");
            if (duplicationEnabled) {
                if (duplicationEvaluation.Available) {
                    Console.WriteLine($"- Duplication rules evaluated: {duplicationEvaluation.RulesEvaluated}");
                    Console.WriteLine($"- Duplication scope: {duplicationEvaluation.Scope}");
                    Console.WriteLine("- Duplication violations: 0");
                } else {
                    Console.WriteLine("- Duplication checks: unavailable (skipped)");
                }
            }
            if (baselineRequired) {
                Console.WriteLine("- Baseline mode: new-only");
                Console.WriteLine($"- Baseline items loaded: {baselineLoadedCount}");
                if (useNewIssuesOnly) {
                    Console.WriteLine($"- Existing findings suppressed by baseline: {baselineSuppressed}");
                }
                if (duplicationUseNewIssuesOnly) {
                    Console.WriteLine($"- Existing duplication violations suppressed by baseline: {duplicationBaselineSuppressed}");
                }
                if (!string.IsNullOrWhiteSpace(baselinePathResolved)) {
                    Console.WriteLine($"- Baseline file: {baselinePathResolved}");
                }
            }
            if (outsidePack > 0) {
                Console.WriteLine($"- Outside-pack findings: {outsidePack} (ignored)");
            }
            return Task.FromResult(ExitSuccess);
        }

        Console.WriteLine("Static analysis gate: fail");
        Console.WriteLine($"- Findings considered: {allFindings.Count}");
        Console.WriteLine($"- Violations: {violations.Count}" +
                          (duplicationEnabled ? $", duplication: {duplicationViolations.Count}" : string.Empty) +
                          (hasHotspotFailures ? $", hotspots to-review: {hotspotFailures.Count}" : string.Empty));
        if (duplicationEnabled) {
            if (duplicationEvaluation.Available) {
                Console.WriteLine($"- Duplication rules evaluated: {duplicationEvaluation.RulesEvaluated}");
                Console.WriteLine($"- Duplication scope: {duplicationEvaluation.Scope}");
            } else {
                Console.WriteLine("- Duplication checks: unavailable (skipped)");
            }
        }
        if (baselineRequired) {
            Console.WriteLine("- Baseline mode: new-only");
            Console.WriteLine($"- Baseline items loaded: {baselineLoadedCount}");
            if (useNewIssuesOnly) {
                Console.WriteLine($"- Existing findings suppressed by baseline: {baselineSuppressed}");
            }
            if (duplicationUseNewIssuesOnly) {
                Console.WriteLine($"- Existing duplication violations suppressed by baseline: {duplicationBaselineSuppressed}");
            }
            if (!string.IsNullOrWhiteSpace(baselinePathResolved)) {
                Console.WriteLine($"- Baseline file: {baselinePathResolved}");
            }
        }
        if (outsidePack > 0) {
            Console.WriteLine($"- Outside-pack findings: {outsidePack}" + (analysisSettings.Gate.IncludeOutsidePackRules ? " (included)" : " (ignored)"));
        }

        PrintViolationSummary(violations, catalog, maxRules: 10, maxItems: 20);
        PrintDuplicationViolationSummary(duplicationViolations, maxItems: 20);
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
        foreach (var rule in selectedRules) {
            var ruleId = rule.RuleId.Trim();
            var tool = string.IsNullOrWhiteSpace(rule.Tool) ? "IntelligenceX.Maintainability" : rule.Tool.Trim();
            var files = (rule.Files ?? new List<DuplicationFileMetrics>())
                .Where(static file => !string.IsNullOrWhiteSpace(file.Path))
                .Where(file => !useChangedFileScope || changedPaths.Contains(NormalizeChangedPath(file.Path)))
                .ToList();
            foreach (var file in files) {
                var fileThreshold = duplication.MaxFilePercent ??
                                    file.ConfiguredMaxPercent ??
                                    rule.ConfiguredMaxPercent;
                if (file.SignificantLines <= 0 || file.DuplicatedLines <= 0) {
                    continue;
                }
                if (file.DuplicatedPercent - fileThreshold <= double.Epsilon) {
                    continue;
                }

                var path = file.Path.Replace('\\', '/');
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
            if (duplication.MaxOverallPercent.HasValue &&
                scopedTotalSignificantLines > 0 &&
                scopedDuplicatedSignificantLines > 0 &&
                scopedOverallPercent - duplication.MaxOverallPercent.Value > double.Epsilon) {
                var overallThreshold = duplication.MaxOverallPercent.Value;
                var overallScopeSuffix = useChangedFileScope ? ":scope:changed-files" : string.Empty;
                var overallFingerprint =
                    $"{ruleId}:overall:{scopedDuplicatedSignificantLines}:{scopedTotalSignificantLines}:{rule.WindowLines}{overallScopeSuffix}";
                var scopeLabel = useChangedFileScope ? " (scope=changed-files)" : string.Empty;
                var overallMessage =
                    $"Overall duplicated significant lines{scopeLabel}: {scopedDuplicatedSignificantLines}/{scopedTotalSignificantLines} ({FormatPercent(scopedOverallPercent)}%) exceeds limit {FormatPercent(overallThreshold)}%.";
                violations.Add(new AnalysisFinding(DuplicationOverallPath, 0, overallMessage, "warning", ruleId, tool,
                    overallFingerprint));
            }
        }

        return DuplicationGateEvaluation.WithData(metricsPath, selectedRules.Count, violations, scope);
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
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        return normalized;
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
            if (Path.IsPathRooted(file)) {
                string relative;
                try {
                    var full = Path.GetFullPath(file);
                    relative = Path.GetRelativePath(fullWorkspace, full);
                } catch (Exception ex) {
                    error = $"Invalid changed file entry '{file}': {ex.Message}";
                    return Array.Empty<PullRequestFile>();
                }
                var normalizedRel = relative.Replace('\\', '/');
                if (Path.IsPathRooted(relative) ||
                    normalizedRel.Equals("..", StringComparison.Ordinal) ||
                    normalizedRel.StartsWith("../", StringComparison.Ordinal)) {
                    error = $"Invalid changed file entry outside workspace: {file}";
                    return Array.Empty<PullRequestFile>();
                }
                file = normalizedRel;
            } else {
                file = file.Replace('\\', '/');
            }
            if (file.StartsWith("./", StringComparison.Ordinal)) {
                file = file.Substring(2);
            }
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
        string Scope,
        string? UnavailableReason) {
        public static DuplicationGateEvaluation Disabled => new(
            Available: true,
            MetricsPath: null,
            RulesEvaluated: 0,
            Violations: Array.Empty<AnalysisFinding>(),
            Scope: "changed-files",
            UnavailableReason: null);

        public static DuplicationGateEvaluation WithData(string metricsPath, int rulesEvaluated,
            IReadOnlyList<AnalysisFinding> violations, string scope) {
            return new DuplicationGateEvaluation(
                Available: true,
                MetricsPath: metricsPath,
                RulesEvaluated: rulesEvaluated,
                Violations: violations ?? Array.Empty<AnalysisFinding>(),
                Scope: scope,
                UnavailableReason: null);
        }

        public static DuplicationGateEvaluation Unavailable(string reason) {
            return new DuplicationGateEvaluation(
                Available: false,
                MetricsPath: null,
                RulesEvaluated: 0,
                Violations: Array.Empty<AnalysisFinding>(),
                Scope: "changed-files",
                UnavailableReason: reason);
        }
    }

    private sealed class GateOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? ChangedFilesPath { get; set; }
        public bool NewIssuesOnly { get; set; }
        public string? BaselinePath { get; set; }
        public string? WriteBaselinePath { get; set; }
    }
}
