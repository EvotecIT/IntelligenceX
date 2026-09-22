using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Labels service defaults separately from authentication evidence and the selected desktop profile.
/// Per-conversation model overrides are not represented as service defaults.
/// </summary>
internal static class NativeRuntimeContextFormatter {
    internal static string Format(SessionRuntimeIdentityDto? runtime, string profile, string? accountId, bool signedIn) {
        var model = signedIn && !string.IsNullOrWhiteSpace(runtime?.Model)
            ? "Service model: " + runtime.Model.Trim()
            : "Service model not confirmed";
        var account = signedIn
            ? string.IsNullOrWhiteSpace(accountId) ? "Signed in · account not reported" : "Account: " + accountId.Trim()
            : "Account not confirmed";
        return model + " · " + account + " · Profile: " + profile;
    }
}
