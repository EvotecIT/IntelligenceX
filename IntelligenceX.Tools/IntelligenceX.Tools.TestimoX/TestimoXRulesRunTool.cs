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
using TestimoX.Definitions;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Executes a selected subset of TestimoX rules and returns typed run outcomes.
/// </summary>
public sealed class TestimoXRulesRunTool : TestimoXToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_rules_run",
        "Run selected TestimoX rules and return typed per-rule outcomes.",
        ToolSchema.Object(
                ("rule_names", ToolSchema.Array(ToolSchema.String(), "Rule names to execute (required).")),
                ("concurrency", ToolSchema.Integer("Execution concurrency (capped).")),
                ("preflight", ToolSchema.String("Preflight mode. Default soft.").Enum("soft", "strict", "enforce", "off")),
                ("include_superseded_rules", ToolSchema.Boolean("Include rules marked superseded. Default false.")),
                ("powershell_rules_directory", ToolSchema.String("Optional path to user PowerShell rule scripts.")),
                ("include_test_results", ToolSchema.Boolean("Include per-test rows for each executed rule. Default true.")),
                ("include_rule_results", ToolSchema.Boolean("Include capped raw rule result rows. Default false.")),
                ("max_result_rows_per_rule", ToolSchema.Integer("Maximum raw result rows per rule when include_rule_results=true (capped).")))
            .Required("rule_names")
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
        if (requestedRuleNames.Count == 0) {
            return ToolResponse.Error("invalid_argument", "rule_names must contain at least one rule name.");
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
        try {
            runner = new TestimoRunner();
            discoveredRules = await runner.DiscoverRulesAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: powerShellRulesDirectory).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"TestimoX rule discovery failed: {ex.Message}");
        }

        var available = new HashSet<string>(
            discoveredRules.Select(static x => x.Name),
            StringComparer.OrdinalIgnoreCase);
        var unknown = requestedRuleNames
            .Where(name => !available.Contains(name))
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

            run = await runner.RunAsync(
                ruleNames: requestedRuleNames,
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
                maxResultRowsPerRule: maxResultRowsPerRule))
            .ToList();

        var model = new TestimoRunResultModel(
            RequestedRuleNames: requestedRuleNames,
            RequestedRuleCount: requestedRuleNames.Count,
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
            RuleName: row.RuleName,
            DisplayName: row.Rule?.DisplayName ?? string.Empty,
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
        int RequestedRuleCount,
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
