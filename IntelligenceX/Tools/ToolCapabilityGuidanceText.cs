using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared human-facing capability guidance phrasing for live tooling/autonomy summaries.
/// </summary>
public static class ToolCapabilityGuidanceText {
    /// <summary>
    /// Builds the top-level tooling availability line for capability self-knowledge.
    /// </summary>
    public static string BuildToolingAvailabilityLine(bool toolingAvailable) {
        return toolingAvailable
            ? "You can actively use live session tools when the user wants checks, investigation, or data gathering."
            : "Tooling is not currently available in this session, so answers should stay conversational and reasoning-based.";
    }

    /// <summary>
    /// Builds the remote-ready pack summary line when remote-capable capability areas are available.
    /// </summary>
    public static string BuildRemoteReadyAreasLine(IReadOnlyList<string> displayNames) {
        if (displayNames is null) {
            throw new ArgumentNullException(nameof(displayNames));
        }

        return "Remote-ready capability areas currently include " + string.Join(", ", displayNames) + ".";
    }

    /// <summary>
    /// Builds the guidance line that favors live contract-backed autonomy flows.
    /// </summary>
    public static string BuildContractGuidedAutonomyLine() {
        return "Prefer live contract-guided setup, handoff, and recovery flows when available instead of narrating unsupported manual steps.";
    }
}
