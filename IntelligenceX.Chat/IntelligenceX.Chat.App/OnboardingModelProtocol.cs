using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App;

internal sealed class OnboardingProfileUpdate {
    public bool HasUserName { get; set; }
    public string? UserName { get; set; }
    public bool HasAssistantPersona { get; set; }
    public string? AssistantPersona { get; set; }
    public bool HasThemePreset { get; set; }
    public string? ThemePreset { get; set; }
    public ProfileUpdateScope Scope { get; set; } = ProfileUpdateScope.Profile;
    public bool HasOnboardingCompleted { get; set; }
    public bool OnboardingCompleted { get; set; }
}

internal static class OnboardingModelProtocol {
    private static readonly Regex ProfileEnvelopeRegex =
        new(@"```ix_profile\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string BuildGuidanceText(IReadOnlyList<string> missingFields) {
        return PromptAssets.GetOnboardingGuidancePrompt(missingFields, ThemeContract.ThemePresetSchema);
    }

    public static string BuildLiveUpdateGuidanceText() {
        return PromptAssets.GetLiveProfileUpdatesPrompt(ThemeContract.ThemePresetSchema);
    }

    public static bool TryExtractLastProfileUpdate(string? assistantText, out OnboardingProfileUpdate update, out string cleanedText) {
        update = new OnboardingProfileUpdate();
        var input = (assistantText ?? string.Empty).Trim();
        cleanedText = input;
        if (input.Length == 0) {
            return false;
        }

        var matches = ProfileEnvelopeRegex.Matches(input);
        if (matches.Count == 0) {
            return false;
        }

        var match = matches[matches.Count - 1];
        if (match.Groups.Count < 2) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("userName", out var userName)) {
                update.HasUserName = true;
                update.UserName = ReadNullableString(userName);
            }

            if (root.TryGetProperty("assistantPersona", out var persona)) {
                update.HasAssistantPersona = true;
                update.AssistantPersona = ReadNullableString(persona);
            }

            if (root.TryGetProperty("themePreset", out var theme)) {
                update.HasThemePreset = true;
                update.ThemePreset = ReadNullableString(theme);
            }

            if (root.TryGetProperty("scope", out var scope)) {
                update.Scope = ParseScope(scope);
            }

            if (root.TryGetProperty("onboardingComplete", out var complete) && (complete.ValueKind == JsonValueKind.True || complete.ValueKind == JsonValueKind.False)) {
                update.HasOnboardingCompleted = true;
                update.OnboardingCompleted = complete.GetBoolean();
            }
        } catch {
            return false;
        }

        cleanedText = ProfileEnvelopeRegex.Replace(input, string.Empty).Trim();
        return update.HasUserName || update.HasAssistantPersona || update.HasThemePreset || update.HasOnboardingCompleted;
    }

    private static string? ReadNullableString(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText()
        };
    }

    private static ProfileUpdateScope ParseScope(JsonElement element) {
        if (element.ValueKind != JsonValueKind.String) {
            return ProfileUpdateScope.Profile;
        }

        var normalized = (element.GetString() ?? string.Empty).Trim();
        if (normalized.Equals("session", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temporary", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Session;
        }

        if (normalized.Equals("profile", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("persistent", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("permanent", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Profile;
        }

        return ProfileUpdateScope.Profile;
    }
}
