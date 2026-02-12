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
}
