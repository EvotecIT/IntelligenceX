using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Base class for system tools with shared option validation.
/// </summary>
public abstract class SystemToolBase : ToolBase {
    /// <summary>
    /// Shared options for system tools.
    /// </summary>
    protected readonly SystemToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    protected SystemToolBase(SystemToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Maps typed ComputerX failures to stable tool error envelopes.
    /// </summary>
    protected static string ErrorFromFailure<TFailure, TCode>(
        TFailure? failure,
        Func<TFailure, TCode> codeSelector,
        Func<TFailure, string?> messageSelector,
        string defaultMessage,
        string fallbackErrorCode = "query_failed")
        where TFailure : class {
        return ToolFailureMapper.ErrorFromFailure(
            failure,
            codeSelector,
            messageSelector,
            defaultMessage,
            fallbackErrorCode);
    }
}
