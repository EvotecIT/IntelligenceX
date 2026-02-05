namespace IntelligenceX.Reviewer;

internal static class NumericGuards {
    internal static bool IsFinite(double value) {
#if NET5_0_OR_GREATER
        return double.IsFinite(value);
#else
        return !double.IsNaN(value) && !double.IsInfinity(value);
#endif
    }
}
