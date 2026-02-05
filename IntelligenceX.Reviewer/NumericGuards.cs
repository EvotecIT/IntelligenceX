namespace IntelligenceX.Reviewer;

internal static class NumericGuards {
    /// <summary>
    /// Returns true when the value is finite (not NaN or Infinity).
    /// </summary>
    /// <param name="value">Value to validate.</param>
    /// <returns>true when the value is finite; otherwise false.</returns>
    internal static bool IsFinite(double value) {
#if NET5_0_OR_GREATER
        return double.IsFinite(value);
#else
        return !double.IsNaN(value) && !double.IsInfinity(value);
#endif
    }
}
