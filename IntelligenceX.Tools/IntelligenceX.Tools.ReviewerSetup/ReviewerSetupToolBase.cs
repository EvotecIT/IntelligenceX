using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Base class for reviewer setup tools with shared option validation.
/// </summary>
public abstract class ReviewerSetupToolBase : ToolBase {
    /// <summary>
    /// Shared options for reviewer setup tools.
    /// </summary>
    protected readonly ReviewerSetupToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReviewerSetupToolBase"/> class.
    /// </summary>
    protected ReviewerSetupToolBase(ReviewerSetupToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }
}
