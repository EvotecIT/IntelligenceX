using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Configuration;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Executes a selected subset of TestimoX rules and returns typed run outcomes.
/// </summary>
public sealed class TestimoXRulesRunTool : TestimoXToolBase, ITool {
    private const int MaxDomainFilters = 32;
    private const int MaxDomainControllerFilters = 64;
    private sealed record RulesRunRequest(
        IReadOnlyList<string> RequestedRuleNames,
        IReadOnlyList<string> RuleNamePatterns,
        string? SearchText,
        IReadOnlyList<string> RequestedCategories,
        IReadOnlyList<string> RequestedTags,
        HashSet<string>? SourceTypeFilter,
        string RuleOrigin,
        bool RunAllEnabledWhenNoSelection,
        bool IncludeDisabledForSelection,
        bool IncludeHiddenForSelection,
        bool IncludeDeprecatedForSelection,
        int MaxSelectedRules,
        bool IncludeTrustedDomains,
        IReadOnlyList<string> IncludeDomains,
        IReadOnlyList<string> ExcludeDomains,
        IReadOnlyList<string> IncludeDomainControllers,
        IReadOnlyList<string> ExcludeDomainControllers,
        int Concurrency,
        bool IncludeSupersededRules,
        bool IncludeTestResults,
        bool IncludeRuleResults,
        int MaxResultRowsPerRule,
        string? PowerShellRulesDirectory,
        PreflightMode PreflightMode);

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
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "rules",
            "run"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRulesRunTool"/> class.
    /// </summary>
    public TestimoXRulesRunTool(TestimoXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<RulesRunRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var requestedRuleNames = reader.DistinctStringArray("rule_names");
            var ruleNamePatterns = reader.DistinctStringArray("rule_name_patterns");
            if (ruleNamePatterns.Count > TestimoXRuleSelectionHelper.MaxRuleNamePatterns) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(
                    $"rule_name_patterns supports at most {TestimoXRuleSelectionHelper.MaxRuleNamePatterns} values.");
            }

            var searchText = reader.OptionalString("search_text");
            var requestedCategories = reader.DistinctStringArray("categories");
            var requestedTags = reader.DistinctStringArray("tags");
            var requestedSourceTypes = reader.DistinctStringArray("source_types");
            if (!TestimoXRuleSelectionHelper.TryParseSourceTypes(
                    requestedSourceTypes,
                    out var sourceTypeFilter,
                    out var sourceTypeError)) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(sourceTypeError ?? "Invalid source_types argument.");
            }

            if (!TestimoXRuleSelectionHelper.TryParseRuleOrigin(
                    reader.OptionalString("rule_origin"),
                    out var ruleOrigin,
                    out var ruleOriginError)) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(ruleOriginError ?? "Invalid rule_origin argument.");
            }

            var runAllEnabledWhenNoSelection = reader.Boolean("run_all_enabled_when_no_selection", defaultValue: false);
            var includeDisabledForSelection = reader.Boolean("include_disabled_for_selection", defaultValue: false);
            var includeHiddenForSelection = reader.Boolean("include_hidden_for_selection", defaultValue: false);
            var includeDeprecatedForSelection = reader.Boolean("include_deprecated_for_selection", defaultValue: true);
            var maxSelectedRules = reader.CappedInt32(
                "max_selected_rules",
                Options.MaxRulesPerRun,
                1,
                Options.MaxRulesPerRun);
            var includeTrustedDomains = reader.Boolean("include_trusted_domains", defaultValue: false);

            var includeDomains = BuildScopeList(
                reader.OptionalString("domain_name"),
                reader.DistinctStringArray("include_domains"),
                MaxDomainFilters,
                "include_domains/domain_name",
                out var includeDomainError);
            if (!string.IsNullOrWhiteSpace(includeDomainError)) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(includeDomainError);
            }

            var excludeDomains = reader.DistinctStringArray("exclude_domains");
            if (excludeDomains.Count > MaxDomainFilters) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(
                    $"exclude_domains supports at most {MaxDomainFilters} values.");
            }

            var includeDomainControllers = BuildScopeList(
                reader.OptionalString("domain_controller"),
                reader.DistinctStringArray("include_domain_controllers"),
                MaxDomainControllerFilters,
                "include_domain_controllers/domain_controller",
                out var includeDomainControllerError);
            if (!string.IsNullOrWhiteSpace(includeDomainControllerError)) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(includeDomainControllerError);
            }

            var excludeDomainControllers = reader.DistinctStringArray("exclude_domain_controllers");
            if (excludeDomainControllers.Count > MaxDomainControllerFilters) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(
                    $"exclude_domain_controllers supports at most {MaxDomainControllerFilters} values.");
            }

            var hasSelectorFilters =
                ruleNamePatterns.Count > 0 ||
                !string.IsNullOrWhiteSpace(searchText) ||
                requestedCategories.Count > 0 ||
                requestedTags.Count > 0 ||
                sourceTypeFilter is { Count: > 0 } ||
                !string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase);
            if (!runAllEnabledWhenNoSelection && requestedRuleNames.Count == 0 && !hasSelectorFilters) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(
                    "Provide at least one selection input: rule_names, rule_name_patterns, search_text, categories, tags, source_types, rule_origin, or set run_all_enabled_when_no_selection=true.");
            }
            if (requestedRuleNames.Count > Options.MaxRulesPerRun) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(
                    $"rule_names exceeds the pack cap ({Options.MaxRulesPerRun}). Reduce the rule set and run in batches.");
            }

            var concurrency = reader.CappedInt32(
                "concurrency",
                Options.DefaultConcurrency,
                1,
                Options.MaxConcurrency);
            var includeSupersededRules = reader.Boolean(
                "include_superseded_rules",
                defaultValue: Options.DefaultIncludeSupersededRules);
            var includeTestResults = reader.Boolean("include_test_results", defaultValue: true);
            var includeRuleResults = reader.Boolean("include_rule_results", defaultValue: false);
            var maxResultRowsPerRule = reader.CappedInt32(
                "max_result_rows_per_rule",
                50,
                1,
                Options.MaxResultRowsPerRule);
            var powerShellRulesDirectory = reader.OptionalString("powershell_rules_directory");

            if (!TryParsePreflightMode(reader.OptionalString("preflight"), out var preflightMode, out var preflightError)) {
                return ToolRequestBindingResult<RulesRunRequest>.Failure(preflightError ?? "Invalid preflight value.");
            }

            return ToolRequestBindingResult<RulesRunRequest>.Success(new RulesRunRequest(
                RequestedRuleNames: requestedRuleNames,
                RuleNamePatterns: ruleNamePatterns,
                SearchText: searchText,
                RequestedCategories: requestedCategories,
                RequestedTags: requestedTags,
                SourceTypeFilter: sourceTypeFilter,
                RuleOrigin: ruleOrigin,
                RunAllEnabledWhenNoSelection: runAllEnabledWhenNoSelection,
                IncludeDisabledForSelection: includeDisabledForSelection,
                IncludeHiddenForSelection: includeHiddenForSelection,
                IncludeDeprecatedForSelection: includeDeprecatedForSelection,
                MaxSelectedRules: maxSelectedRules,
                IncludeTrustedDomains: includeTrustedDomains,
                IncludeDomains: includeDomains,
                ExcludeDomains: excludeDomains,
                IncludeDomainControllers: includeDomainControllers,
                ExcludeDomainControllers: excludeDomainControllers,
                Concurrency: concurrency,
                IncludeSupersededRules: includeSupersededRules,
                IncludeTestResults: includeTestResults,
                IncludeRuleResults: includeRuleResults,
                MaxResultRowsPerRule: maxResultRowsPerRule,
                PowerShellRulesDirectory: powerShellRulesDirectory,
                PreflightMode: preflightMode));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<RulesRunRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_rules_run." },
                isTransient: false);
        }

        var requestedRuleNames = context.Request.RequestedRuleNames;
        var ruleNamePatterns = context.Request.RuleNamePatterns;
        var searchText = context.Request.SearchText;
        var requestedCategories = context.Request.RequestedCategories;
        var requestedTags = context.Request.RequestedTags;
        var sourceTypeFilter = context.Request.SourceTypeFilter;
        var ruleOrigin = context.Request.RuleOrigin;
        var runAllEnabledWhenNoSelection = context.Request.RunAllEnabledWhenNoSelection;
        var includeDisabledForSelection = context.Request.IncludeDisabledForSelection;
        var includeHiddenForSelection = context.Request.IncludeHiddenForSelection;
        var includeDeprecatedForSelection = context.Request.IncludeDeprecatedForSelection;
        var maxSelectedRules = context.Request.MaxSelectedRules;
        var includeTrustedDomains = context.Request.IncludeTrustedDomains;
        var includeDomains = context.Request.IncludeDomains;
        var excludeDomains = context.Request.ExcludeDomains;
        var includeDomainControllers = context.Request.IncludeDomainControllers;
        var excludeDomainControllers = context.Request.ExcludeDomainControllers;
        var concurrency = context.Request.Concurrency;
        var includeSupersededRules = context.Request.IncludeSupersededRules;
        var includeTestResults = context.Request.IncludeTestResults;
        var includeRuleResults = context.Request.IncludeRuleResults;
        var maxResultRowsPerRule = context.Request.MaxResultRowsPerRule;
        var powerShellRulesDirectory = context.Request.PowerShellRulesDirectory;
        var preflightMode = context.Request.PreflightMode;

        ToolingRuleDiscoveryResult allDiscovery;
        try {
            allDiscovery = await ToolingRuleService.DiscoverRulesAsync(new ToolingRuleDiscoveryRequest {
                IncludeDisabled = true,
                IncludeHidden = true,
                IncludeDeprecated = true,
                PowerShellRulesDirectory = powerShellRulesDirectory
            }, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "TestimoX rule discovery failed.");
        }

        var availableNames = allDiscovery.Rules
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(static row => row.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = requestedRuleNames
            .Where(name => !availableNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unknown TestimoX rule name(s): {string.Join(", ", unknown)}.",
                hints: new[] { "Call testimox_rules_list first to discover valid rule names." },
                isTransient: false);
        }

        var selectedRuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedRuleName in requestedRuleNames) {
            selectedRuleNames.Add(requestedRuleName);
        }

        var hasSelectorFilters =
            ruleNamePatterns.Count > 0 ||
            !string.IsNullOrWhiteSpace(searchText) ||
            requestedCategories.Count > 0 ||
            requestedTags.Count > 0 ||
            sourceTypeFilter is { Count: > 0 } ||
            !string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginAny, StringComparison.OrdinalIgnoreCase);
        if (hasSelectorFilters || runAllEnabledWhenNoSelection) {
            ToolingRuleDiscoveryResult selectionDiscovery;
            try {
                selectionDiscovery = await ToolingRuleService.DiscoverRulesAsync(new ToolingRuleDiscoveryRequest {
                    IncludeDisabled = includeDisabledForSelection,
                    IncludeHidden = includeHiddenForSelection,
                    IncludeDeprecated = includeDeprecatedForSelection,
                    NamePatterns = ruleNamePatterns,
                    Query = searchText,
                    Categories = requestedCategories,
                    Tags = requestedTags,
                    SourceTypes = ToToolingSourceTypes(sourceTypeFilter),
                    RuleOrigin = ToToolingRuleOrigin(ruleOrigin),
                    PowerShellRulesDirectory = powerShellRulesDirectory
                }, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                return ErrorFromException(ex, defaultMessage: "TestimoX rule discovery failed.");
            }

            foreach (var row in selectionDiscovery.Rules) {
                if (!string.IsNullOrWhiteSpace(row.Name)) {
                    selectedRuleNames.Add(row.Name);
                }
            }
        }

        if (selectedRuleNames.Count == 0) {
            return ToolResultV2.Error(
                "invalid_argument",
                "Selection resolved to zero rules. Broaden filters or call testimox_rules_list to discover available rules.");
        }

        if (selectedRuleNames.Count > maxSelectedRules) {
            return ToolResultV2.Error(
                "invalid_argument",
                $"Selected rules exceed max_selected_rules ({maxSelectedRules}). Narrow filters or run in batches.");
        }
        if (selectedRuleNames.Count > Options.MaxRulesPerRun) {
            return ToolResultV2.Error(
                "invalid_argument",
                $"Selected rules exceed the pack cap ({Options.MaxRulesPerRun}). Narrow filters or run in batches.");
        }

        var orderedSelectedRuleNames = selectedRuleNames
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ToolingRuleRunResult run;
        try {
            run = await ToolingRuleService.RunRulesAsync(new ToolingRuleRunRequest {
                RuleNames = orderedSelectedRuleNames,
                FailWhenMissingRuleNames = true,
                IncludeRuleData = includeRuleResults,
                IncludeFilteredRuleData = includeRuleResults,
                IncludeExcludedRuleData = false,
                IncludeStreams = true,
                IncludeTestResults = includeTestResults,
                IncludeExecutionParameters = false,
                PowerShellRulesDirectory = powerShellRulesDirectory,
                Configuration = new ExecutionConfiguration {
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
                },
                IncludeDomains = includeDomains,
                ExcludeDomains = excludeDomains,
                IncludeDomainControllers = includeDomainControllers,
                ExcludeDomainControllers = excludeDomainControllers,
                IncludeTrustedDomains = includeTrustedDomains,
                IncludeSupersededRules = includeSupersededRules
            }, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "TestimoX execution failed.", fallbackErrorCode: "execution_failed");
        }

        var rows = run.Rules
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => MapRuleRow(
                row,
                includeTestResults: includeTestResults,
                includeRuleResults: includeRuleResults,
                maxResultRowsPerRule: maxResultRowsPerRule))
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
            SelectedRuleNames: orderedSelectedRuleNames,
            RequestedRuleCount: requestedRuleNames.Count,
            SelectedRuleCount: orderedSelectedRuleNames.Length,
            ExecutedRuleCount: run.ExecutedRuleCount,
            FailedRuleCount: run.FailedRules,
            SkippedRuleCount: run.SkippedRules,
            ErrorCount: run.Errors,
            WarningCount: run.Warnings,
            ElapsedMs: (long)run.Elapsed.TotalMilliseconds,
            Rules: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rules_view",
            title: "TestimoX run outcomes (preview)",
            maxTop: Options.MaxRulesPerRun,
            baseTruncated: false,
            scanned: rows.Count);
    }

    private static TestimoRuleRunRow MapRuleRow(
        ToolingRuleExecutionResult row,
        bool includeTestResults,
        bool includeRuleResults,
        int maxResultRowsPerRule) {
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

        return new TestimoRuleRunRow(
            RuleName: row.Name,
            DisplayName: row.DisplayName,
            SourceType: NormalizeSourceTypeName(row.SourceType),
            RuleOrigin: row.RuleOrigin,
            Scope: row.Scope,
            OverallStatus: row.OverallStatus.ToString(),
            OverallStatusText: row.OverallStatusText,
            Success: row.Success,
            TotalTests: row.TotalTests,
            TotalSuccess: row.TotalSuccess,
            TotalFailures: row.TotalFailures,
            ExecutionMs: (long)row.ExecutionTime.TotalMilliseconds,
            CpuMs: (long)row.CpuTime.TotalMilliseconds,
            MemoryBytes: row.MemoryUsage,
            ErrorCount: row.Errors.Count,
            WarningCount: row.Warnings.Count,
            Categories: row.Categories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Tags: row.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            TestResults: testRows,
            ResultRows: resultRows);
    }

    private static IReadOnlyList<object?> ReadRawRows(ToolingRuleExecutionResult row, int maxResultRowsPerRule) {
        var source = row.FilteredResults.Items;
        if (source is null || source.Count == 0) {
            source = row.Results.Items;
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
