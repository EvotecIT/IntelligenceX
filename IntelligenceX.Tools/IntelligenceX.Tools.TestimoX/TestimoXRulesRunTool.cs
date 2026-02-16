using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX;
using TestimoX.Configuration;
using TestimoX.Definitions;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Executes a selected subset of TestimoX rules and returns typed run outcomes.
/// </summary>
public sealed class TestimoXRulesRunTool : TestimoXToolBase, ITool {
    private const int MaxDomainFilters = 32;
    private const int MaxDomainControllerFilters = 64;

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_rules_run",
        "Run selected TestimoX rules and return typed per-rule outcomes.",
        ToolSchema.Object(
                ("rule_names", ToolSchema.Array(ToolSchema.String(), "Explicit rule names to execute.")),
                ("rule_name_patterns", ToolSchema.Array(ToolSchema.String("Wildcard pattern matched against rule_name/display_name (for example: *kerberos*)."), "Optional wildcard selectors.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across rule name/display/description.")),
                ("categories", ToolSchema.Array(ToolSchema.String(), "Optional category-name filters (any-match).")),
                ("tags", ToolSchema.Array(ToolSchema.String(), "Optional tag filters (any-match).")),
                ("source_types", ToolSchema.Array(ToolSchema.String("Rule source type.").Enum(TestimoXRuleSelectionHelper.SourceTypeNames), "Optional source type filters (any-match).")),
                ("rule_origin", ToolSchema.String("Optional origin filter. 'builtin' means bundled rules, 'external' means rules introduced through powershell_rules_directory.").Enum(TestimoXRuleSelectionHelper.RuleOriginNames)),
                ("run_all_enabled_when_no_selection", ToolSchema.Boolean("When true, auto-select all enabled/visible/non-deprecated rules if no other selectors are provided.")),
                ("include_disabled_for_selection", ToolSchema.Boolean("Include disabled rules when evaluating patterns/search/category/tag/source filters. Default false.")),
                ("include_hidden_for_selection", ToolSchema.Boolean("Include hidden rules when evaluating filters. Default false.")),
                ("include_deprecated_for_selection", ToolSchema.Boolean("Include deprecated rules when evaluating filters. Default true.")),
                ("max_selected_rules", ToolSchema.Integer("Maximum selected rules after all filters (capped).")),
                ("domain_name", ToolSchema.String("Optional domain DNS name shortcut (added to include_domains).")),
                ("domain_controller", ToolSchema.String("Optional domain controller shortcut (added to include_domain_controllers).")),
                ("include_domains", ToolSchema.Array(ToolSchema.String(), "Optional domain DNS names to include for discovery/query scope.")),
                ("exclude_domains", ToolSchema.Array(ToolSchema.String(), "Optional domain DNS names to exclude from discovery/query scope.")),
                ("include_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional domain controllers to include for discovery/query scope.")),
                ("exclude_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional domain controllers to exclude from discovery/query scope.")),
                ("include_trusted_domains", ToolSchema.Boolean("When true, discovery includes trusted-forest domains. Default false.")),
                ("concurrency", ToolSchema.Integer("Execution concurrency (capped).")),
                ("preflight", ToolSchema.String("Preflight mode. Default soft.").Enum("soft", "strict", "enforce", "off")),
                ("include_superseded_rules", ToolSchema.Boolean("Include rules marked superseded. Default false.")),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("include_test_results", ToolSchema.Boolean("Include per-test rows for each executed rule. Default true.")),
                ("include_rule_results", ToolSchema.Boolean("Include capped raw rule result rows. Default false.")),
                ("max_result_rows_per_rule", ToolSchema.Integer("Maximum raw result rows per rule when include_rule_results=true (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRulesRunTool"/> class.
    /// </summary>
    public TestimoXRulesRunTool(TestimoXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResponse.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_rules_run." },
                isTransient: false);
        }

        var requestedRuleNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("rule_names"));
        var ruleNamePatterns = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("rule_name_patterns"));
        if (ruleNamePatterns.Count > TestimoXRuleSelectionHelper.MaxRuleNamePatterns) {
            return ToolResponse.Error(
                "invalid_argument",
                $"rule_name_patterns supports at most {TestimoXRuleSelectionHelper.MaxRuleNamePatterns} values.");
        }

        var searchText = ToolArgs.GetOptionalTrimmed(arguments, "search_text");
        var requestedCategories = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("categories"));
        var requestedTags = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("tags"));
        var requestedSourceTypes = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("source_types"));
        if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(
                requestedSourceTypes,
                out var sourceTypeFilter,
                out var sourceTypeError)) {
            return ToolResponse.Error("invalid_argument", sourceTypeError ?? "Invalid source_types argument.");
        }
        if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                ToolArgs.GetOptionalTrimmed(arguments, "rule_origin"),
                out var ruleOrigin,
                out var ruleOriginError)) {
            return ToolResponse.Error("invalid_argument", ruleOriginError ?? "Invalid rule_origin argument.");
        }

        var runAllEnabledWhenNoSelection = ToolArgs.GetBoolean(arguments, "run_all_enabled_when_no_selection", defaultValue: false);
        var includeDisabledForSelection = ToolArgs.GetBoolean(arguments, "include_disabled_for_selection", defaultValue: false);
        var includeHiddenForSelection = ToolArgs.GetBoolean(arguments, "include_hidden_for_selection", defaultValue: false);
        var includeDeprecatedForSelection = ToolArgs.GetBoolean(arguments, "include_deprecated_for_selection", defaultValue: true);
        var maxSelectedRules = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_selected_rules",
            defaultValue: Options.MaxRulesPerRun,
            minInclusive: 1,
            maxInclusive: Options.MaxRulesPerRun);
        var includeTrustedDomains = ToolArgs.GetBoolean(arguments, "include_trusted_domains", defaultValue: false);

        var includeDomains = BuildScopeList(
            ToolArgs.GetOptionalTrimmed(arguments, "domain_name"),
            ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domains")),
            MaxDomainFilters,
            "include_domains/domain_name",
            out var includeDomainError);
        if (!string.IsNullOrWhiteSpace(includeDomainError)) {
            return ToolResponse.Error("invalid_argument", includeDomainError);
        }

        var excludeDomains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domains"));
        if (excludeDomains.Count > MaxDomainFilters) {
            return ToolResponse.Error("invalid_argument", $"exclude_domains supports at most {MaxDomainFilters} values.");
        }

        var includeDomainControllers = BuildScopeList(
            ToolArgs.GetOptionalTrimmed(arguments, "domain_controller"),
            ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domain_controllers")),
            MaxDomainControllerFilters,
            "include_domain_controllers/domain_controller",
            out var includeDomainControllerError);
        if (!string.IsNullOrWhiteSpace(includeDomainControllerError)) {
            return ToolResponse.Error("invalid_argument", includeDomainControllerError);
        }

        var excludeDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domain_controllers"));
        if (excludeDomainControllers.Count > MaxDomainControllerFilters) {
            return ToolResponse.Error("invalid_argument", $"exclude_domain_controllers supports at most {MaxDomainControllerFilters} values.");
        }

        var hasSelectorFilters =
            ruleNamePatterns.Count > 0 ||
            !string.IsNullOrWhiteSpace(searchText) ||
            requestedCategories.Count > 0 ||
            requestedTags.Count > 0 ||
            sourceTypeFilter is { Count: > 0 } ||
            !string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase);
        if (!runAllEnabledWhenNoSelection && requestedRuleNames.Count == 0 && !hasSelectorFilters) {
            return ToolResponse.Error(
                "invalid_argument",
                "Provide at least one selection input: rule_names, rule_name_patterns, search_text, categories, tags, source_types, rule_origin, or set run_all_enabled_when_no_selection=true.");
        }
        if (requestedRuleNames.Count > Options.MaxRulesPerRun) {
            return ToolResponse.Error(
                "invalid_argument",
                $"rule_names exceeds the pack cap ({Options.MaxRulesPerRun}). Reduce the rule set and run in batches.");
        }

        var concurrency = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "concurrency",
            defaultValue: Options.DefaultConcurrency,
            minInclusive: 1,
            maxInclusive: Options.MaxConcurrency);
        var includeSupersededRules = ToolArgs.GetBoolean(
            arguments: arguments,
            key: "include_superseded_rules",
            defaultValue: Options.DefaultIncludeSupersededRules);
        var includeTestResults = ToolArgs.GetBoolean(arguments, "include_test_results", defaultValue: true);
        var includeRuleResults = ToolArgs.GetBoolean(arguments, "include_rule_results", defaultValue: false);
        var maxResultRowsPerRule = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_result_rows_per_rule",
            defaultValue: 50,
            minInclusive: 1,
            maxInclusive: Options.MaxResultRowsPerRule);
        var powerShellRulesDirectory = ToolArgs.GetOptionalTrimmed(arguments, "powershell_rules_directory");

        if (!TryParsePreflightMode(ToolArgs.GetOptionalTrimmed(arguments, "preflight"), out var preflightMode, out var preflightError)) {
            return ToolResponse.Error("invalid_argument", preflightError ?? "Invalid preflight value.");
        }

        TestimoRunner runner;
        List<Rule> discoveredRules;
        var usingExternalDirectory = !string.IsNullOrWhiteSpace(powerShellRulesDirectory);
        HashSet<string>? builtinRuleNames = null;
        try {
            runner = new TestimoRunner();
            discoveredRules = await runner.DiscoverRulesAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: powerShellRulesDirectory).ConfigureAwait(false);
            if (usingExternalDirectory) {
                builtinRuleNames = await TestimoXRuleSelectionHelper.DiscoverBuiltinRuleNamesAsync(cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"TestimoX rule discovery failed: {ex.Message}");
        }

        var availableByName = discoveredRules
            .Where(static x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(static x => x.Name, StringComparer.OrdinalIgnoreCase);
        var unknown = requestedRuleNames
            .Where(name => !availableByName.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: $"Unknown TestimoX rule name(s): {string.Join(", ", unknown)}.",
                hints: new[] { "Call testimox_rules_list first to discover valid rule names." },
                isTransient: false);
        }

        var selectedRules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedRuleName in requestedRuleNames) {
            selectedRules[requestedRuleName] = availableByName[requestedRuleName];
        }

        if (hasSelectorFilters || runAllEnabledWhenNoSelection) {
            IEnumerable<Rule> candidates = discoveredRules;

            if (!includeDisabledForSelection) {
                candidates = candidates.Where(static x => x.Enable);
            }
            if (!includeHiddenForSelection) {
                candidates = candidates.Where(static x => x.Visibility != RuleVisibility.Hidden);
            }
            if (!includeDeprecatedForSelection) {
                candidates = candidates.Where(static x => !x.IsDeprecated);
            }

            if (ruleNamePatterns.Count > 0) {
                candidates = candidates.Where(rule => TestimoXRuleSelectionHelper.MatchesAnyPattern(rule, ruleNamePatterns));
            }

            if (!string.IsNullOrWhiteSpace(searchText)) {
                var term = searchText.Trim();
                candidates = candidates.Where(rule =>
                    TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.Name, term) ||
                    TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.DisplayName, term) ||
                    TestimoXRuleSelectionHelper.ContainsIgnoreCase(rule.Description, term));
            }

            if (requestedCategories.Count > 0) {
                var requested = new HashSet<string>(requestedCategories, StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Where(rule => rule.Category.Any(cat => requested.Contains(cat.ToString())));
            }

            if (requestedTags.Count > 0) {
                var requested = new HashSet<string>(requestedTags, StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Where(rule => rule.Tags.Any(tag => requested.Contains(tag)));
            }

            if (sourceTypeFilter is { Count: > 0 }) {
                candidates = candidates.Where(rule => TestimoXRuleSelectionHelper.MatchesSourceType(rule, sourceTypeFilter));
            }

            if (!string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase)) {
                candidates = candidates.Where(rule =>
                    string.Equals(
                        TestimoXRuleSelectionHelper.ResolveRuleOrigin(rule, usingExternalDirectory, builtinRuleNames),
                        ruleOrigin,
                        StringComparison.OrdinalIgnoreCase));
            }

            foreach (var rule in candidates.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)) {
                if (!string.IsNullOrWhiteSpace(rule.Name)) {
                    selectedRules[rule.Name] = rule;
                }
            }
        }

        if (selectedRules.Count == 0) {
            return ToolResponse.Error(
                "invalid_argument",
                "Selection resolved to zero rules. Broaden filters or call testimox_rules_list to discover available rules.");
        }

        if (selectedRules.Count > maxSelectedRules) {
            return ToolResponse.Error(
                "invalid_argument",
                $"Selected rules exceed max_selected_rules ({maxSelectedRules}). Narrow filters or run in batches.");
        }
        if (selectedRules.Count > Options.MaxRulesPerRun) {
            return ToolResponse.Error(
                "invalid_argument",
                $"Selected rules exceed the pack cap ({Options.MaxRulesPerRun}). Narrow filters or run in batches.");
        }

        var selectedRuleNames = selectedRules.Keys
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        TestimoRunResult run;
        try {
            var config = new ExecutionConfiguration {
                Concurrency = concurrency,
                GenerateHtml = false,
                OpenReport = false,
                GenerateWord = false,
                GenerateJson = false,
                AutoConfirm = true,
                Verbosity = VerbosityLevel.Normal,
                ConsoleView = ConsoleView.Ansi,
                KeepOpen = false,
                Preflight = preflightMode,
                IncludeSupersededRules = includeSupersededRules,
                PowerShellRulesDirectory = powerShellRulesDirectory
            };

            var engine = new Testimo {
                IncludeDomains = includeDomains.Count == 0 ? null : includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.Count == 0 ? null : excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.Count == 0 ? null : includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.Count == 0 ? null : excludeDomainControllers.ToArray(),
                IncludeTrustedDomains = includeTrustedDomains,
                PowerShellRulesDirectory = powerShellRulesDirectory
            };

            run = await runner.RunAsync(
                engine: engine,
                ruleNames: selectedRuleNames,
                config: config,
                ct: cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("execution_failed", $"TestimoX execution failed: {ex.Message}");
        }

        var completed = (run.Engine.RulesCompleted ?? new List<RuleComplete>())
            .OrderBy(static x => x.RuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = completed
            .Select(row => MapRuleRow(
                row,
                includeTestResults: includeTestResults,
                includeRuleResults: includeRuleResults,
                maxResultRowsPerRule: maxResultRowsPerRule,
                usingExternalDirectory: usingExternalDirectory,
                builtinRuleNames: builtinRuleNames))
            .ToList();

        var model = new TestimoRunResultModel(
            RequestedRuleNames: requestedRuleNames,
            RequestedRuleNamePatterns: ruleNamePatterns,
            SearchText: searchText,
            Categories: requestedCategories,
            Tags: requestedTags,
            SourceTypes: sourceTypeFilter is null
                ? Array.Empty<string>()
                : sourceTypeFilter.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            RuleOrigin: ruleOrigin,
            RunAllEnabledWhenNoSelection: runAllEnabledWhenNoSelection,
            IncludeTrustedDomains: includeTrustedDomains,
            IncludeDomains: includeDomains,
            ExcludeDomains: excludeDomains,
            IncludeDomainControllers: includeDomainControllers,
            ExcludeDomainControllers: excludeDomainControllers,
            SelectedRuleNames: selectedRuleNames,
            RequestedRuleCount: requestedRuleNames.Count,
            SelectedRuleCount: selectedRuleNames.Length,
            ExecutedRuleCount: run.RuleCount,
            FailedRuleCount: run.FailedRules,
            SkippedRuleCount: run.SkippedRules,
            ErrorCount: run.Errors,
            WarningCount: run.Warnings,
            ElapsedMs: (long)run.Elapsed.TotalMilliseconds,
            Rules: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX run outcomes (preview)",
            maxTop: Options.MaxRulesPerRun,
            baseTruncated: false,
            response: out var response,
            scanned: completed.Count);
        return response;
    }

    private static TestimoRuleRunRow MapRuleRow(
        RuleComplete row,
        bool includeTestResults,
        bool includeRuleResults,
        int maxResultRowsPerRule,
        bool usingExternalDirectory,
        HashSet<string>? builtinRuleNames) {
        var testRows = includeTestResults
            ? row.TestResults.Select(static test => new TestimoRuleTestRunRow(
                Name: test.Name,
                Success: test.Success,
                Status: test.Status.ToString(),
                Message: test.Message,
                ErrorMessage: test.ErrorMessage)).ToArray()
            : Array.Empty<TestimoRuleTestRunRow>();

        var resultRows = includeRuleResults
            ? ReadRawRows(row, maxResultRowsPerRule)
            : Array.Empty<object?>();

        var sourceType = TestimoXRuleSelectionHelper.GetSourceType(row.Rule);
        var ruleOrigin = row.Rule is null
            ? (usingExternalDirectory ? TestimoXRuleSelectionHelper.RuleOriginExternal : TestimoXRuleSelectionHelper.RuleOriginBuiltin)
            : TestimoXRuleSelectionHelper.ResolveRuleOrigin(row.Rule, usingExternalDirectory, builtinRuleNames);

        return new TestimoRuleRunRow(
            RuleName: row.RuleName,
            DisplayName: row.Rule?.DisplayName ?? string.Empty,
            SourceType: sourceType,
            RuleOrigin: ruleOrigin,
            Scope: row.Rule?.Scope.ToString() ?? string.Empty,
            OverallStatus: row.OverallStatus.ToString(),
            OverallStatusText: row.OverallStatusText,
            Success: row.Success,
            TotalTests: row.TotalTests,
            TotalSuccess: row.TotalSuccess,
            TotalFailures: row.TotalFailures,
            ExecutionMs: (long)row.ExecutionTime.TotalMilliseconds,
            CpuMs: (long)row.CpuTime.TotalMilliseconds,
            MemoryBytes: row.MemoryUsage,
            ErrorCount: row.PowerShellErrors.Count,
            WarningCount: row.PowerShellWarnings.Count,
            Categories: row.Rule?.Category.Select(static x => x.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            Tags: row.Rule?.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            TestResults: testRows,
            ResultRows: resultRows);
    }

    private static IReadOnlyList<object?> ReadRawRows(RuleComplete row, int maxResultRowsPerRule) {
        var source = row.ResultsFilteredRaw;
        if (source is null || !source.Any()) {
            source = row.ResultsRaw;
        }

        return source
            .Take(maxResultRowsPerRule)
            .Select(SanitizeValue)
            .ToArray();
    }

    private static object? SanitizeValue(object? value) {
        if (value is null) {
            return null;
        }

        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal) {
            return value;
        }
        if (value is DateTime dt) {
            return dt.ToUniversalTime().ToString("O");
        }
        if (value is DateTimeOffset dto) {
            return dto.ToUniversalTime().ToString("O");
        }

        try {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<object?>(json);
        } catch {
            return value.ToString() ?? string.Empty;
        }
    }

    private static IReadOnlyList<string> BuildScopeList(
        string? singleValue,
        IReadOnlyList<string> manyValues,
        int maxItems,
        string label,
        out string? error) {
        error = null;
        var list = new List<string>(manyValues.Count + 1);
        if (!string.IsNullOrWhiteSpace(singleValue)) {
            list.Add(singleValue.Trim());
        }

        for (var i = 0; i < manyValues.Count; i++) {
            var value = manyValues[i];
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) {
                list.Add(value.Trim());
            }
        }

        if (list.Count > maxItems) {
            error = $"{label} supports at most {maxItems} values.";
            return Array.Empty<string>();
        }

        return list;
    }

    private static bool TryParsePreflightMode(string? raw, out PreflightMode mode, out string? error) {
        mode = PreflightMode.Soft;
        error = null;

        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "soft", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (string.Equals(raw, "strict", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "enforce", StringComparison.OrdinalIgnoreCase)) {
            mode = PreflightMode.Enforce;
            return true;
        }
        if (string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase)) {
            mode = PreflightMode.Off;
            return true;
        }

        error = "preflight must be one of: soft, strict, enforce, off.";
        return false;
    }

    private sealed record TestimoRunResultModel(
        IReadOnlyList<string> RequestedRuleNames,
        IReadOnlyList<string> RequestedRuleNamePatterns,
        string? SearchText,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> SourceTypes,
        string RuleOrigin,
        bool RunAllEnabledWhenNoSelection,
        bool IncludeTrustedDomains,
        IReadOnlyList<string> IncludeDomains,
        IReadOnlyList<string> ExcludeDomains,
        IReadOnlyList<string> IncludeDomainControllers,
        IReadOnlyList<string> ExcludeDomainControllers,
        IReadOnlyList<string> SelectedRuleNames,
        int RequestedRuleCount,
        int SelectedRuleCount,
        int ExecutedRuleCount,
        int FailedRuleCount,
        int SkippedRuleCount,
        int ErrorCount,
        int WarningCount,
        long ElapsedMs,
        IReadOnlyList<TestimoRuleRunRow> Rules);

    private sealed record TestimoRuleRunRow(
        string RuleName,
        string DisplayName,
        string SourceType,
        string RuleOrigin,
        string Scope,
        string OverallStatus,
        string OverallStatusText,
        bool Success,
        int TotalTests,
        int TotalSuccess,
        int TotalFailures,
        long ExecutionMs,
        long CpuMs,
        long MemoryBytes,
        int ErrorCount,
        int WarningCount,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags,
        IReadOnlyList<TestimoRuleTestRunRow> TestResults,
        IReadOnlyList<object?> ResultRows);

    private sealed record TestimoRuleTestRunRow(
        string Name,
        bool Success,
        string Status,
        string? Message,
        string? ErrorMessage);
}
