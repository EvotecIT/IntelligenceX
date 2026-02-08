using System;

namespace IntelligenceX.OpenAI.Native;

internal static class OpenAINativeTrace {
    public static bool IsEnabled() {
        var value = Environment.GetEnvironmentVariable("INTELLIGENCEX_NATIVE_TRACE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static void TryWriteLine(string message) {
        if (!IsEnabled()) {
            return;
        }
        try {
            Console.Error.WriteLine(message);
        } catch {
            // Ignore trace failures.
        }
    }
}

