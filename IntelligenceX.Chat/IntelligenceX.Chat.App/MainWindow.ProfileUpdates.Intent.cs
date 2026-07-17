using System;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private string? GetEffectiveUserName() =>
        !string.IsNullOrWhiteSpace(_sessionUserNameOverride) ? _sessionUserNameOverride : _appState.UserName;

    private string? GetEffectiveAssistantPersona() =>
        !string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride)
            ? _sessionAssistantPersonaOverride
            : _appState.AssistantPersona;

    private string GetEffectiveThemePreset() =>
        NormalizeTheme(_sessionThemeOverride) ?? _themePreset;

    private static ProfileUpdateScope ParseProfileUpdateScope(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ProfileUpdateScope.Unspecified;
        }

        if (normalized.Equals("session", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temporary", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("0", StringComparison.Ordinal)
            || normalized.Equals("false", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Session;
        }

        if (normalized.Equals("profile", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("saved", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("persistent", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("permanent", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("1", StringComparison.Ordinal)
            || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Profile;
        }

        return ProfileUpdateScope.Unspecified;
    }

    private async Task<bool> ApplyProfileUpdateAsync(
        OnboardingProfileUpdate update,
        bool autoCompleteOnboardingForProfileScope) {
        var scope = ResolveEffectiveProfileUpdateScope(update);
        var persistProfile = scope == ProfileUpdateScope.Profile;
        var changed = false;
        var effectiveThemeBefore = GetEffectiveThemePreset();
        var invalidThemeRequested = false;

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
            invalidThemeRequested = string.IsNullOrWhiteSpace(normalizedTheme)
                                    && !string.IsNullOrWhiteSpace(update.ThemePreset);
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

        var completionEligibleMissingFields = BuildMissingOnboardingFields(
            GetEffectiveUserName(),
            GetEffectiveAssistantPersona(),
            GetEffectiveThemePreset(),
            onboardingCompleted: false);

        if (persistProfile && update.HasOnboardingCompleted) {
            var nextOnboardingCompleted = update.OnboardingCompleted && completionEligibleMissingFields.Count == 0;
            if (_appState.OnboardingCompleted != nextOnboardingCompleted) {
                _appState.OnboardingCompleted = nextOnboardingCompleted;
                changed = true;
            }
        }

        if (persistProfile
            && autoCompleteOnboardingForProfileScope
            && !_appState.OnboardingCompleted
            && completionEligibleMissingFields.Count == 0) {
            _appState.OnboardingCompleted = true;
            changed = true;
        }

        if (invalidThemeRequested) {
            AppendSystem("Ignored unsupported theme preset in profile update: " + update.ThemePreset!.Trim());
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

    internal static ProfileUpdateScope ResolveEffectiveProfileUpdateScope(OnboardingProfileUpdate update) {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Scope != ProfileUpdateScope.Unspecified) {
            return update.Scope;
        }

        var completesOnboardingWithProfileFields = update.HasOnboardingCompleted
                                                  && update.OnboardingCompleted
                                                  && (update.HasUserName
                                                      || update.HasAssistantPersona
                                                      || update.HasThemePreset);
        return completesOnboardingWithProfileFields
            ? ProfileUpdateScope.Profile
            : ProfileUpdateScope.Session;
    }
}
