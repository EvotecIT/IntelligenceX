using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Formats compact runtime readiness text for the native shell header.
/// </summary>
internal static class NativeRuntimeStatusFormatter {
    public static string FormatReady(SessionPolicyDto? policy) {
        var capability = policy?.CapabilitySnapshot;
        if (capability == null) {
            return "Ready";
        }

        if (!capability.ToolingAvailable || capability.RegisteredTools <= 0) {
            return "No tools loaded";
        }

        return capability.EnabledPackCount == 1
            ? "Ready · 1 tool pack"
            : $"Ready · {capability.EnabledPackCount} tool packs";
    }
}
