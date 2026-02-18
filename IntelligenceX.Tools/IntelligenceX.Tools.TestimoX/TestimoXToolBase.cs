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
}
