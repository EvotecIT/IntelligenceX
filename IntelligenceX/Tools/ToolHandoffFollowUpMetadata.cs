using System;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared follow-up kind tokens used by outbound handoff routes.
/// </summary>
public static class ToolHandoffFollowUpKinds {
    /// <summary>
    /// Follow-up used to confirm the result of a prior action or observed state.
    /// </summary>
    public const string Verification = "verification";

    /// <summary>
    /// Follow-up used to expand investigation depth from current evidence.
    /// </summary>
    public const string Investigation = "investigation";

    /// <summary>
    /// Follow-up used to normalize or canonicalize discovered entities.
    /// </summary>
    public const string Normalization = "normalization";

    /// <summary>
    /// Follow-up used to add non-critical supporting context.
    /// </summary>
    public const string Enrichment = "enrichment";

    /// <summary>
    /// Normalizes an optional follow-up kind to a known token or an empty string.
    /// </summary>
    public static string Normalize(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            Verification => Verification,
            Investigation => Investigation,
            Normalization => Normalization,
            Enrichment => Enrichment,
            _ => string.Empty
        };
    }
}

/// <summary>
/// Shared follow-up priority hints used by outbound handoff routes.
/// Higher values indicate more important follow-up work.
/// </summary>
public static class ToolHandoffFollowUpPriorities {
    /// <summary>
    /// Low-importance follow-up priority.
    /// </summary>
    public const int Low = 25;

    /// <summary>
    /// Normal follow-up priority.
    /// </summary>
    public const int Normal = 50;

    /// <summary>
    /// High-importance follow-up priority.
    /// </summary>
    public const int High = 75;

    /// <summary>
    /// Critical follow-up priority.
    /// </summary>
    public const int Critical = 100;

    /// <summary>
    /// Normalizes a follow-up priority into the supported 0-100 range.
    /// </summary>
    public static int Normalize(int value) {
        if (value <= 0) {
            return 0;
        }

        if (value >= Critical) {
            return Critical;
        }

        return value;
    }
}
