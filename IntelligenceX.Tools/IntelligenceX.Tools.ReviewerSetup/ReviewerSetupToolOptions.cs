using System;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Runtime options for reviewer setup guidance tools.
/// </summary>
public sealed class ReviewerSetupToolOptions {
    /// <summary>
    /// Enable maintenance path in guidance output.
    /// </summary>
    public bool IncludeMaintenancePath { get; set; } = true;

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        // Reserved for future limits/flags.
    }
}
