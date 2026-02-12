using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Small helpers for tool implementations to safely read from APIs that may throw (for example EventLogRecord properties).
/// </summary>
public static class ToolSafe {
    /// <summary>
    /// Executes a function and returns its value, suppressing any exception and returning default.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="func">Function to execute.</param>
    /// <returns>Function result; or default when an exception is thrown.</returns>
    public static T? Try<T>(Func<T> func) {
        try {
            return func();
        } catch {
            return default;
        }
    }
}
