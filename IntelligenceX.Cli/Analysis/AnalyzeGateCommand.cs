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

internal static partial class AnalyzeGateCommand {
    private const int ExitSuccess = 0;
    private const int ExitError = 1;
    private const int ExitGateFailed = 2;
    private const string DuplicationOverallPath = ".intelligencex/duplication-overall";
    private const string DuplicationOverallDeltaPath = ".intelligencex/duplication-overall-delta";
    private const string DuplicationFilePath = ".intelligencex/duplication-file";
    private const string DuplicationFileDeltaPath = ".intelligencex/duplication-file-delta";

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
        var gateRuleIds = NormalizeRuleIds(analysisSettings.Gate.RuleIds);
        var hasTypeFilter = allowedTypes.Count > 0;
        var hasRuleIdFilter = gateRuleIds.Count > 0;
        var includeAllFilters = !hasTypeFilter && !hasRuleIdFilter;

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
            var matchesType = hasTypeFilter && allowedTypes.Contains(type);
            var matchesRuleId = hasRuleIdFilter && gateRuleIds.Contains(ruleId);
            var includedByFilter = includeAllFilters || matchesType || matchesRuleId;
            if (!includedByFilter) {
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
            gateRuleIds,
            hasTypeFilter,
            hasRuleIdFilter,
            includeAllFilters);
        var hasHotspotFailures = hotspotFailures.Count > 0;
        var useNewIssuesOnly = options.NewIssuesOnly || analysisSettings.Gate.NewIssuesOnly;
        var duplicationEnabled = analysisSettings.Gate.Duplication.Enabled;
        var duplicationUseNewIssuesOnly = duplicationEnabled &&
            (useNewIssuesOnly || analysisSettings.Gate.Duplication.NewIssuesOnly);
        var duplicationUseFileDelta = duplicationEnabled &&
                                      analysisSettings.Gate.Duplication.MaxFilePercentIncrease.HasValue;
        var duplicationUseOverallDelta = duplicationEnabled &&
                                        analysisSettings.Gate.Duplication.MaxOverallPercentIncrease.HasValue;
        var duplicationRequiresBaseline = duplicationUseNewIssuesOnly || duplicationUseFileDelta || duplicationUseOverallDelta;
        var configuredBaselinePath = !string.IsNullOrWhiteSpace(options.BaselinePath)
            ? options.BaselinePath
            : analysisSettings.Gate.BaselinePath;
        var baselineRequired = useNewIssuesOnly || duplicationUseNewIssuesOnly || duplicationUseFileDelta || duplicationUseOverallDelta;
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
                foreach (var snapshot in duplicationEvaluation.OverallSnapshots ?? Array.Empty<DuplicationOverallSnapshot>()) {
                    if (snapshot.SignificantLines <= 0) {
                        continue;
                    }
                    var message =
                        $"Overall duplication snapshot (scope={snapshot.Scope}): {snapshot.DuplicatedLines}/{snapshot.SignificantLines} ({FormatPercent(snapshot.DuplicatedPercent)}%).";
                    baselineItems.Add(new AnalysisFinding(DuplicationOverallPath, 0, message, "info", snapshot.RuleId, snapshot.Tool,
                        snapshot.Fingerprint));
                }
                if (analysisSettings.Gate.Duplication.MaxFilePercentIncrease.HasValue) {
                    foreach (var snapshot in duplicationEvaluation.FileSnapshots ?? Array.Empty<DuplicationFileSnapshot>()) {
                        if (snapshot.SignificantLines <= 0) {
                            continue;
                        }
                        var message =
                            $"File duplication snapshot (scope={snapshot.Scope}): {snapshot.Path} {snapshot.DuplicatedLines}/{snapshot.SignificantLines} ({FormatPercent(snapshot.DuplicatedPercent)}%).";
                        baselineItems.Add(new AnalysisFinding(DuplicationFilePath, 0, message, "info", snapshot.RuleId, snapshot.Tool,
                            snapshot.Fingerprint));
                    }
                }
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
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationRequiresBaseline));
            }

            var baselinePath = ResolveWorkspaceBoundPath(workspace, configuredBaselinePath);
            if (baselinePath is null) {
                Console.WriteLine($"Static analysis gate: unavailable (baseline path resolves outside workspace: {configuredBaselinePath}).");
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationRequiresBaseline));
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
                return Task.FromResult(ResolveUnavailableExit(analysisSettings, useNewIssuesOnly, duplicationRequiresBaseline));
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

        if (duplicationUseOverallDelta && duplicationEvaluation.Available) {
            var deltaResult = AnalyzeGateBaseline.TryLoadDuplicationOverallBaselines(
                baselinePathResolved,
                out var baselineOverallByRule,
                out var deltaError);
            if (!deltaResult) {
                Console.WriteLine($"Static analysis duplication delta gate: unavailable ({deltaError}).");
                if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                    return Task.FromResult(ExitGateFailed);
                }
            } else {
                var allowedIncrease = ClampPercentIncrease(analysisSettings.Gate.Duplication.MaxOverallPercentIncrease!.Value, "analysis.gate.duplication.maxOverallPercentIncrease");
                var windowMismatch = 0;
                foreach (var current in duplicationEvaluation.OverallSnapshots ?? Array.Empty<DuplicationOverallSnapshot>()) {
                    if (current.SignificantLines <= 0) {
                        continue;
                    }

                    if (!baselineOverallByRule.TryGetValue($"{current.RuleId}|{current.Scope}", out var baseline)) {
                        Console.WriteLine(
                            $"Static analysis duplication delta gate: unavailable (baseline missing overall snapshot for {current.RuleId} scope={current.Scope}).");
                        if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                            return Task.FromResult(ExitGateFailed);
                        }
                        continue;
                    }
                    var isWindowMismatch =
                        (baseline.WindowLines > 0 && current.WindowLines > 0 && baseline.WindowLines != current.WindowLines) ||
                        (baseline.WindowLines == 0 && current.WindowLines > 0) ||
                        (baseline.WindowLines > 0 && current.WindowLines == 0);
                    if (isWindowMismatch) {
                        windowMismatch++;
                        Console.WriteLine(
                            $"Static analysis duplication delta gate: unavailable (baseline overall snapshot window mismatch for {current.RuleId} scope={current.Scope}: baseline={baseline.WindowLines} current={current.WindowLines}).");
                        if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                            return Task.FromResult(ExitGateFailed);
                        }
                        continue;
                    }
                    if (baseline.SignificantLines <= 0) {
                        continue;
                    }

                    var delta = Math.Round(current.DuplicatedPercent - baseline.DuplicatedPercent, 2, MidpointRounding.AwayFromZero);
                    if (delta <= allowedIncrease) {
                        continue;
                    }

                    var message =
                        $"Overall duplication increased (scope={current.Scope}): baseline {FormatPercent(baseline.DuplicatedPercent)}% -> current {FormatPercent(current.DuplicatedPercent)}% (+{FormatPercent(delta)}pp) exceeds allowed +{FormatPercent(allowedIncrease)}pp.";
                    var fingerprint = $"{current.RuleId}:overall-delta:{FormatPercent(baseline.DuplicatedPercent)}->{FormatPercent(current.DuplicatedPercent)}:allow:+{FormatPercent(allowedIncrease)}:scope:{current.Scope}";
                    duplicationViolations.Add(new AnalysisFinding(DuplicationOverallDeltaPath, 0, message, "warning", current.RuleId, current.Tool,
                        fingerprint));
                }
                if (windowMismatch > 0) {
                    Console.WriteLine(
                        $"Static analysis duplication delta gate: baseline window mismatch for {windowMismatch} overall snapshot(s) (skipped).");
                }
            }
        }

        if (duplicationUseFileDelta && duplicationEvaluation.Available) {
            var deltaResult = AnalyzeGateBaseline.TryLoadDuplicationFileBaselines(
                baselinePathResolved,
                out var baselineFilesByKey,
                out var deltaError);
            if (!deltaResult) {
                Console.WriteLine($"Static analysis duplication file delta gate: unavailable ({deltaError}).");
                if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                    return Task.FromResult(ExitGateFailed);
                }
            } else {
                var allowedIncrease = ClampPercentIncrease(analysisSettings.Gate.Duplication.MaxFilePercentIncrease!.Value, "analysis.gate.duplication.maxFilePercentIncrease");
                var missingBaseline = 0;
                var windowMismatch = 0;
                foreach (var current in duplicationEvaluation.FileSnapshots ?? Array.Empty<DuplicationFileSnapshot>()) {
                    if (current.SignificantLines <= 0) {
                        continue;
                    }
                    var currentPathKey = AnalyzeGateBaseline.NormalizeDuplicationPathForKey(current.Path);
                    if (!baselineFilesByKey.TryGetValue($"{current.RuleId}|{current.Scope}|{currentPathKey}", out var baseline)) {
                        missingBaseline++;
                        continue;
                    }
                    var isWindowMismatch =
                        (baseline.WindowLines > 0 && current.WindowLines > 0 && baseline.WindowLines != current.WindowLines) ||
                        (baseline.WindowLines == 0 && current.WindowLines > 0) ||
                        (baseline.WindowLines > 0 && current.WindowLines == 0);
                    if (isWindowMismatch) {
                        windowMismatch++;
                        Console.WriteLine(
                            $"Static analysis duplication file delta gate: unavailable (baseline window mismatch for {current.RuleId} scope={current.Scope} file={current.Path}: baseline={baseline.WindowLines} current={current.WindowLines}).");
                        if (analysisSettings.Gate.Duplication.FailOnUnavailable) {
                            return Task.FromResult(ExitGateFailed);
                        }
                        continue;
                    }
                    if (baseline.SignificantLines <= 0) {
                        continue;
                    }

                    var delta = Math.Round(current.DuplicatedPercent - baseline.DuplicatedPercent, 2, MidpointRounding.AwayFromZero);
                    if (delta <= allowedIncrease) {
                        continue;
                    }

                    var message =
                        $"File duplication increased (scope={current.Scope}): {current.Path} baseline {FormatPercent(baseline.DuplicatedPercent)}% -> current {FormatPercent(current.DuplicatedPercent)}% (+{FormatPercent(delta)}pp) exceeds allowed +{FormatPercent(allowedIncrease)}pp.";
                    var fingerprintPath = Uri.EscapeDataString((currentPathKey ?? string.Empty).Trim());
                    var fingerprint =
                        $"{current.RuleId}:file-delta-uri:{fingerprintPath}:{FormatPercent(baseline.DuplicatedPercent)}->{FormatPercent(current.DuplicatedPercent)}:allow:+{FormatPercent(allowedIncrease)}:scope:{current.Scope}";
                    var findingPath = current.Path ?? string.Empty;
                    duplicationViolations.Add(new AnalysisFinding(findingPath, 1, message, "warning", current.RuleId, current.Tool,
                        fingerprint));
                }

                if (missingBaseline > 0) {
                    Console.WriteLine(
                        $"Static analysis duplication file delta gate: baseline missing snapshots for {missingBaseline} file(s) (skipped).");
                }
                if (windowMismatch > 0) {
                    Console.WriteLine(
                        $"Static analysis duplication file delta gate: baseline window mismatch for {windowMismatch} file(s) (skipped).");
                }
            }
        }

        if (violations.Count == 0 && !hasHotspotFailures && duplicationViolations.Count == 0) {
            Console.WriteLine("Static analysis gate: pass");
            Console.WriteLine($"- Findings considered: {allFindings.Count}");
            Console.WriteLine($"- Violations: 0");
            Console.WriteLine($"- Gate type filter: {FormatFilterSummary(allowedTypes, includeAllWhenEmpty: true)}");
            Console.WriteLine($"- Gate ruleIds filter: {FormatFilterSummary(gateRuleIds, includeAllWhenEmpty: false)}");
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
        Console.WriteLine($"- Gate type filter: {FormatFilterSummary(allowedTypes, includeAllWhenEmpty: true)}");
        Console.WriteLine($"- Gate ruleIds filter: {FormatFilterSummary(gateRuleIds, includeAllWhenEmpty: false)}");
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
}
