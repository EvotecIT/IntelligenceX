using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private static bool MightContainProfileUpdateCue(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (UserNameIntentRegex.IsMatch(normalized)
            || ThemeIntentRegex.IsMatch(normalized)
            || ThemeUseIntentRegex.IsMatch(normalized)
            || PersonaIntentRegex.IsMatch(normalized)
            || PersonaUseIntentRegex.IsMatch(normalized)) {
            return true;
        }

        return normalized.Contains("you can be", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("be more", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("analyst", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("concise", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("optimistic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("funny", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("humor", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyUserProfileIntentAsync(string userText) {
        if (TryExtractMemoryIntent(userText, out var memoryFact)) {
            await AddMemoryFactAsync(memoryFact, weight: 3, tags: new[] { "user-intent" }).ConfigureAwait(false);
        }

        var intent = ParseUserProfileIntent(userText);
        if (!intent.HasUserName && !intent.HasAssistantPersona && !intent.HasThemePreset) {
            return;
        }

        if (intent.Scope == ProfileUpdateScope.Unspecified) {
            return;
        }

        var update = new OnboardingProfileUpdate {
            Scope = intent.Scope,
            HasUserName = intent.HasUserName,
            UserName = intent.UserName,
            HasAssistantPersona = intent.HasAssistantPersona,
            AssistantPersona = intent.AssistantPersona,
            HasThemePreset = intent.HasThemePreset,
            ThemePreset = intent.ThemePreset
        };

        _ = await ApplyProfileUpdateAsync(update, autoCompleteOnboardingForProfileScope: true).ConfigureAwait(false);
    }

    private static bool TryExtractMemoryIntent(string userText, out string? memoryFact) {
        memoryFact = null;
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryExtractMemoryFactFromRegex(MemoryRememberIntentRegex, normalized, out memoryFact)) {
            return true;
        }

        if (TryExtractMemoryFactFromRegex(MemoryFutureIntentRegex, normalized, out memoryFact)) {
            return true;
        }

        return false;
    }

    private static bool TryExtractMemoryFactFromRegex(Regex regex, string text, out string? memoryFact) {
        memoryFact = null;
        var match = regex.Match(text);
        if (!match.Success) {
            return false;
        }

        var group = match.Groups["value"];
        if (!group.Success) {
            return false;
        }

        var candidate = group.Value.Trim().Trim('.', '!', '?', ';', ':');
        if (candidate.Length < 6) {
            return false;
        }

        if (LooksLikeImperativeTaskPhrase(candidate)) {
            // Avoid storing imperative tasks accidentally while still allowing preference-style entries.
            return false;
        }

        if (candidate.Length > 220) {
            candidate = candidate[..220].TrimEnd();
        }

        memoryFact = candidate;
        return true;
    }

    private static bool LooksLikeImperativeTaskPhrase(string candidate) {
        if (!candidate.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return candidate.StartsWith("to do ", StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith("to run ", StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith("to check ", StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith("to investigate ", StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith("to troubleshoot ", StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith("to fix ", StringComparison.OrdinalIgnoreCase);
    }

    private UserProfileIntent ParseUserProfileIntent(string userText) {
        var intent = new UserProfileIntent();
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return intent;
        }

        if (TryMatchValue(UserNameIntentRegex, normalized, out var name)) {
            intent.UserName = name;
            intent.HasUserName = true;
        }

        if (TryMatchValue(ThemeIntentRegex, normalized, out var theme) || TryMatchValue(ThemeUseIntentRegex, normalized, out theme)) {
            intent.ThemePreset = theme;
            intent.HasThemePreset = true;
        }

        string? persona = null;
        if (TryMatchValue(PersonaIntentRegex, normalized, out var explicitPersona)) {
            persona = explicitPersona;
        } else if (TryMatchValue(PersonaUseIntentRegex, normalized, out var usePersona)) {
            persona = usePersona;
        } else {
            persona = TryBuildPersonaFromToneHints(normalized);
        }

        if (!string.IsNullOrWhiteSpace(persona)) {
            intent.AssistantPersona = persona;
            intent.HasAssistantPersona = true;
        }

        intent.Scope = DetectProfileUpdateScope(normalized);
        return intent;
    }

    private static bool TryMatchValue(Regex regex, string input, out string? value) {
        value = null;
        var match = regex.Match(input);
        if (!match.Success) {
            return false;
        }

        var group = match.Groups["value"];
        if (!group.Success) {
            return false;
        }

        value = group.Value.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private string? TryBuildPersonaFromToneHints(string text) {
        if (!LooksLikePersonaPreferenceText(text)) {
            return null;
        }

        var role = "assistant";
        if (text.Contains("security analyst", StringComparison.OrdinalIgnoreCase)) {
            role = "security analyst";
        } else if (text.Contains("analyst", StringComparison.OrdinalIgnoreCase)) {
            role = "analyst";
        } else if (text.Contains("ad engineer", StringComparison.OrdinalIgnoreCase)) {
            role = "AD engineer";
        } else if (text.Contains("engineer", StringComparison.OrdinalIgnoreCase)) {
            role = "engineer";
        } else if (!string.IsNullOrWhiteSpace(GetEffectiveAssistantPersona())) {
            role = NormalizePersonaRole(GetEffectiveAssistantPersona()!, GetEffectiveAssistantPersona()!);
        }

        var traits = CollectPersonaTraits(text);
        if (traits.Count == 0) {
            return null;
        }

        return role + " with " + JoinTraits(traits) + ".";
    }

    private string? GetEffectiveUserName() {
        return !string.IsNullOrWhiteSpace(_sessionUserNameOverride) ? _sessionUserNameOverride : _appState.UserName;
    }

    private string? GetEffectiveAssistantPersona() {
        return !string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride) ? _sessionAssistantPersonaOverride : _appState.AssistantPersona;
    }

    private string GetEffectiveThemePreset() {
        return NormalizeTheme(_sessionThemeOverride) ?? _themePreset;
    }

    private static ProfileUpdateScope DetectProfileUpdateScope(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ProfileUpdateScope.Unspecified;
        }

        var hasSession = SessionScopeIntentRegex.IsMatch(normalized);
        var hasProfile = ProfileScopeIntentRegex.IsMatch(normalized);
        if (hasSession && !hasProfile) {
            return ProfileUpdateScope.Session;
        }

        if (hasProfile && !hasSession) {
            return ProfileUpdateScope.Profile;
        }

        if (hasSession && hasProfile) {
            var sessionIndex = normalized.IndexOf("session", StringComparison.OrdinalIgnoreCase);
            var profileIndex = normalized.IndexOf("default", StringComparison.OrdinalIgnoreCase);
            if (profileIndex < 0) {
                profileIndex = normalized.IndexOf("save", StringComparison.OrdinalIgnoreCase);
            }
            if (profileIndex >= 0 && (sessionIndex < 0 || profileIndex > sessionIndex)) {
                return ProfileUpdateScope.Profile;
            }
            return ProfileUpdateScope.Session;
        }

        return ProfileUpdateScope.Unspecified;
    }

    private static ProfileUpdateScope ParseProfileUpdateScope(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ProfileUpdateScope.Unspecified;
        }

        if (normalized.Equals("session", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temporary", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temp", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Session;
        }

        if (normalized.Equals("profile", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("saved", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("persistent", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("permanent", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Profile;
        }

        return ProfileUpdateScope.Unspecified;
    }

    private async Task<bool> ApplyProfileUpdateAsync(OnboardingProfileUpdate update, bool autoCompleteOnboardingForProfileScope) {
        var scope = update.Scope == ProfileUpdateScope.Unspecified
            ? ProfileUpdateScope.Profile
            : update.Scope;
        var persistProfile = scope == ProfileUpdateScope.Profile;
        var changed = false;
        var effectiveThemeBefore = GetEffectiveThemePreset();

        if (update.HasUserName) {
            var nextName = NormalizeUserNameValue(update.UserName);
            if (persistProfile) {
                if (!string.Equals(_appState.UserName, nextName, StringComparison.Ordinal)) {
                    _appState.UserName = nextName;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(_sessionUserNameOverride)) {
                    _sessionUserNameOverride = null;
                    changed = true;
                }
            } else if (!string.Equals(_sessionUserNameOverride, nextName, StringComparison.Ordinal)) {
                _sessionUserNameOverride = nextName;
                changed = true;
            }
        }

        if (update.HasAssistantPersona) {
            var nextPersona = NormalizeAssistantPersonaValue(update.AssistantPersona);
            if (persistProfile) {
                if (!string.Equals(_appState.AssistantPersona, nextPersona, StringComparison.Ordinal)) {
                    _appState.AssistantPersona = nextPersona;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride)) {
                    _sessionAssistantPersonaOverride = null;
                    changed = true;
                }
            } else if (!string.Equals(_sessionAssistantPersonaOverride, nextPersona, StringComparison.Ordinal)) {
                _sessionAssistantPersonaOverride = nextPersona;
                changed = true;
            }
        }

        if (update.HasThemePreset) {
            var normalizedTheme = NormalizeTheme(update.ThemePreset);
            if (!string.IsNullOrWhiteSpace(normalizedTheme)) {
                if (persistProfile) {
                    if (!string.Equals(_appState.ThemePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                        _appState.ThemePreset = normalizedTheme;
                        changed = true;
                    }
                    if (!string.Equals(_themePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                        _themePreset = normalizedTheme;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
                        _sessionThemeOverride = null;
                        changed = true;
                    }
                } else if (!string.Equals(_sessionThemeOverride, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                    _sessionThemeOverride = normalizedTheme;
                    changed = true;
                }
            }
        }

        if (persistProfile && update.HasOnboardingCompleted && _appState.OnboardingCompleted != update.OnboardingCompleted) {
            _appState.OnboardingCompleted = update.OnboardingCompleted;
            changed = true;
        }

        if (persistProfile && autoCompleteOnboardingForProfileScope && !_appState.OnboardingCompleted && BuildMissingOnboardingFields().Count == 0) {
            _appState.OnboardingCompleted = true;
            changed = true;
        }

        if (!changed) {
            return false;
        }

        var effectiveThemeAfter = GetEffectiveThemePreset();
        if (!string.Equals(effectiveThemeBefore, effectiveThemeAfter, StringComparison.OrdinalIgnoreCase)) {
            await ApplyThemeFromStateAsync().ConfigureAwait(false);
        }

        await PublishOptionsStateAsync().ConfigureAwait(false);
        if (persistProfile) {
            await PersistAppStateAsync().ConfigureAwait(false);
        }

        return true;
    }

}
