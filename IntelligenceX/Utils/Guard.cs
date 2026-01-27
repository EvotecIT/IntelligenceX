using System;

namespace IntelligenceX.Utils;

internal static class Guard {
    public static string NotNullOrWhiteSpace(string? value, string paramName) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
        return value!;
    }

    public static T NotNull<T>(T? value, string paramName) where T : class {
        if (value is null) {
            throw new ArgumentNullException(paramName);
        }
        return value!;
    }
}
