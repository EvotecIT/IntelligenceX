using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Tools.Common;
using TestimoX.Definitions;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Base class for TestimoX tools with shared option validation.
/// </summary>
public abstract class TestimoXToolBase : ToolBase {
    /// <summary>
    /// Shared options for TestimoX tools.
    /// </summary>
    protected readonly TestimoXToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXToolBase"/> class.
    /// </summary>
    protected TestimoXToolBase(TestimoXToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Maps common runtime exceptions for TestimoX tools to stable tool error envelopes.
    /// </summary>
    protected static string ErrorFromException(
        Exception exception,
        string defaultMessage = "TestimoX operation failed.",
        string fallbackErrorCode = "query_failed") {
        return ToolExceptionMapper.ErrorFromException(
            exception,
            defaultMessage: defaultMessage,
            unauthorizedMessage: "Access denied while executing TestimoX operations.",
            timeoutMessage: "TestimoX operation timed out.",
            fallbackErrorCode: fallbackErrorCode,
            invalidOperationErrorCode: "invalid_argument");
    }

    /// <summary>
    /// Discovers TestimoX rules with consistent cancellation and error-envelope behavior.
    /// </summary>
    protected static async Task<(List<Rule>? Rules, string? ErrorResponse)> TryDiscoverRulesAsync(
        TestimoRunner runner,
        string? powerShellRulesDirectory,
        CancellationToken cancellationToken,
        string defaultErrorMessage = "TestimoX rule discovery failed.") {
        if (runner is null) {
            throw new ArgumentNullException(nameof(runner));
        }

        try {
            var discovered = await runner.DiscoverRulesAsync(
                includeDisabled: true,
                ct: cancellationToken,
                powerShellRulesDirectory: powerShellRulesDirectory).ConfigureAwait(false);
            return (discovered, null);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return (null, ErrorFromException(ex, defaultErrorMessage, fallbackErrorCode: "query_failed"));
        }
    }

    /// <summary>
    /// Discovers builtin TestimoX rule names with consistent cancellation and error-envelope behavior.
    /// </summary>
    protected static async Task<(HashSet<string>? RuleNames, string? ErrorResponse)> TryDiscoverBuiltinRuleNamesAsync(
        CancellationToken cancellationToken,
        string defaultErrorMessage = "TestimoX builtin rule discovery failed.",
        Func<CancellationToken, Task<HashSet<string>>>? discoverFunc = null) {
        var discover = discoverFunc ?? TestimoXRuleSelectionHelper.DiscoverBuiltinRuleNamesAsync;
        try {
            var names = await discover(cancellationToken).ConfigureAwait(false);
            return (names, null);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return (null, ErrorFromException(ex, defaultErrorMessage, fallbackErrorCode: "query_failed"));
        }
    }

    /// <summary>
    /// Converts lowercase tool source-type filters into TestimoX tooling enums.
    /// </summary>
    protected static IReadOnlyList<RuleSourceType> ToToolingSourceTypes(HashSet<string>? sourceTypeFilter) {
        if (sourceTypeFilter is not { Count: > 0 }) {
            return Array.Empty<RuleSourceType>();
        }

        var values = new List<RuleSourceType>(sourceTypeFilter.Count);
        if (sourceTypeFilter.Contains("powershell")) {
            values.Add(RuleSourceType.PowerShell);
        }
        if (sourceTypeFilter.Contains("csharp")) {
            values.Add(RuleSourceType.CSharp);
        }

        return values;
    }

    /// <summary>
    /// Converts rule-origin identifiers into TestimoX tooling enums.
    /// </summary>
    protected static ToolingRuleOrigin ToToolingRuleOrigin(string ruleOrigin) {
        return string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginExternal, StringComparison.OrdinalIgnoreCase)
            ? ToolingRuleOrigin.External
            : string.Equals(ruleOrigin, TestimoXRuleSelectionHelper.RuleOriginBuiltin, StringComparison.OrdinalIgnoreCase)
                ? ToolingRuleOrigin.Builtin
                : ToolingRuleOrigin.Any;
    }

    /// <summary>
    /// Normalizes TestimoX enum source-type names to the stable lowercase tool contract.
    /// </summary>
    protected static string NormalizeSourceTypeName(string sourceType) {
        return string.Equals(sourceType, nameof(RuleSourceType.CSharp), StringComparison.OrdinalIgnoreCase)
            ? "csharp"
            : "powershell";
    }
}
