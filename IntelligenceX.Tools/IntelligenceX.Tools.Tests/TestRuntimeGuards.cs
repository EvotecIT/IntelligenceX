using Xunit.Sdk;

namespace IntelligenceX.Tools.Tests;

internal static class TestRuntimeGuards {
    internal static void RequireWindows(string reason) {
        if (!OperatingSystem.IsWindows()) {
            throw SkipException.ForSkip(reason);
        }
    }

    internal static void Require(bool condition, string reason) {
        if (!condition) {
            throw SkipException.ForSkip(reason);
        }
    }
}
