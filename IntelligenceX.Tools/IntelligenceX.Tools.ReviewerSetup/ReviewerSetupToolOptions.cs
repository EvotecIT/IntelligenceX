using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Runtime options for reviewer setup guidance tools.
/// </summary>
public sealed class ReviewerSetupToolOptions : IToolPackRuntimeConfigurable, IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "reviewer_setup",
        "reviewersetup"
    };

    /// <summary>
    /// Enable maintenance path in guidance output.
    /// </summary>
    public bool IncludeMaintenancePath { get; set; } = true;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <inheritdoc />
    public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
        ArgumentNullException.ThrowIfNull(context);
        IncludeMaintenancePath = context.ReviewerSetupIncludeMaintenancePath;
    }

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        // Reserved for future limits/flags.
    }
}
