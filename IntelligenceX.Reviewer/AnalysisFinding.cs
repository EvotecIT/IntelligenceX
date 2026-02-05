namespace IntelligenceX.Reviewer;

internal sealed record AnalysisFinding(
    string Path,
    int Line,
    string Message,
    string? Severity,
    string? RuleId = null,
    string? Tool = null,
    string? Fingerprint = null
);

internal static class AnalysisSeverity {
    public static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "unknown";
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "critical" => "error",
            "fatal" => "error",
            "error" => "error",
            "high" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "medium" => "warning",
            "note" => "info",
            "info" => "info",
            "information" => "info",
            "low" => "info",
            "none" => "none",
            _ => normalized
        };
    }

    public static int Rank(string? value) {
        var normalized = Normalize(value);
        return normalized switch {
            "error" => 3,
            "warning" => 2,
            "info" => 1,
            "none" => 0,
            _ => 0
        };
    }
}
