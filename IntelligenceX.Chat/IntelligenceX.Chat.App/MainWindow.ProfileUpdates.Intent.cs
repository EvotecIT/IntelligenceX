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
    private static readonly Regex StructuredProfileEnvelopeRegex = new(
        @"```(?:ix_profile|ix_profile_update)\s*(\{[\s\S]*?\})\s*```",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StructuredMemoryEnvelopeRegex = new(
        @"```(?:ix_memory|ix_memory_note)\s*(\{[\s\S]*?\})\s*```",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool MightContainProfileUpdateCue(string text) {
        // Keep live profile update guidance language-neutral by default:
        // every non-empty user turn can include structured profile metadata.
        return !string.IsNullOrWhiteSpace(text);
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
            intent.Scope = ProfileUpdateScope.Session;
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

        if (TryExtractStructuredMemoryFact(normalized, out memoryFact)) {
            return true;
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

        if (!TryFinalizeMemoryFactCandidate(group.Value, out var candidate)) {
            return false;
        }

        memoryFact = candidate;
        return true;
    }

    private static bool TryExtractStructuredMemoryFact(string text, out string? memoryFact) {
        memoryFact = null;
        if (!TryExtractStructuredJsonPayload(text, StructuredMemoryEnvelopeRegex, out var root)) {
            return false;
        }

        if (!TryReadStructuredString(root, out var candidate, "memory", "fact", "note", "text", "value")) {
            return false;
        }

        if (!TryFinalizeMemoryFactCandidate(candidate, out var normalizedMemoryFact)) {
            return false;
        }

        memoryFact = normalizedMemoryFact;
        return true;
    }

    private static bool TryFinalizeMemoryFactCandidate(string? candidate, out string memoryFact) {
        memoryFact = string.Empty;
        var normalized = (candidate ?? string.Empty).Trim().Trim('.', '!', '?', ';', ':');
        if (normalized.Length < 6) {
            return false;
        }

        if (LooksLikeImperativeTaskPhrase(normalized)) {
            // Avoid storing imperative tasks accidentally while still allowing preference-style entries.
            return false;
        }

        if (normalized.Length > 220) {
            normalized = normalized[..220].TrimEnd();
        }

        if (normalized.Length == 0) {
            return false;
        }

        memoryFact = normalized;
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

        if (TryExtractStructuredUserProfileIntent(normalized, out var structuredIntent)) {
            intent = structuredIntent;
        }

        if (!intent.HasUserName && TryMatchValue(UserNameIntentRegex, normalized, out var name)) {
            intent.UserName = name;
            intent.HasUserName = true;
        }

        if (!intent.HasThemePreset
            && (TryMatchValue(ThemeIntentRegex, normalized, out var theme) || TryMatchValue(ThemeUseIntentRegex, normalized, out theme))) {
            intent.ThemePreset = theme;
            intent.HasThemePreset = true;
        }

        if (!intent.HasAssistantPersona) {
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
        }

        if (intent.Scope == ProfileUpdateScope.Unspecified) {
            intent.Scope = DetectProfileUpdateScope(normalized);
        }
        return intent;
    }

    private static bool TryExtractStructuredUserProfileIntent(string text, out UserProfileIntent intent) {
        intent = new UserProfileIntent();
        if (!TryExtractStructuredJsonPayload(text, StructuredProfileEnvelopeRegex, out var root)) {
            return false;
        }

        var hasAny = false;
        if (TryReadStructuredString(root, out var userName, "userName", "user_name", "name")
            && !string.IsNullOrWhiteSpace(userName)) {
            intent.UserName = userName.Trim();
            intent.HasUserName = true;
            hasAny = true;
        }

        if (TryReadStructuredString(root, out var persona, "assistantPersona", "assistant_persona", "persona")
            && !string.IsNullOrWhiteSpace(persona)) {
            intent.AssistantPersona = persona.Trim();
            intent.HasAssistantPersona = true;
            hasAny = true;
        }

        if (TryReadStructuredString(root, out var theme, "themePreset", "theme_preset", "theme")
            && !string.IsNullOrWhiteSpace(theme)) {
            intent.ThemePreset = theme.Trim();
            intent.HasThemePreset = true;
            hasAny = true;
        }

        if (TryReadStructuredString(root, out var scopeValue, "scope")) {
            var parsedScope = ParseProfileUpdateScope(scopeValue);
            if (parsedScope != ProfileUpdateScope.Unspecified) {
                intent.Scope = parsedScope;
            }
        }

        return hasAny;
    }

    private static bool TryExtractStructuredJsonPayload(string text, Regex envelopeRegex, out JsonElement root) {
        root = default;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        string payload;
        var matches = envelopeRegex.Matches(normalized);
        if (matches.Count > 0) {
            var last = matches[matches.Count - 1];
            if (last.Groups.Count < 2) {
                return false;
            }
            payload = (last.Groups[1].Value ?? string.Empty).Trim();
        } else {
            if (normalized.Length < 2 || normalized[0] != '{' || normalized[^1] != '}') {
                return false;
            }
            payload = normalized;
        }

        if (payload.Length == 0) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }
            root = doc.RootElement.Clone();
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryReadStructuredString(JsonElement root, out string value, params string[] names) {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object || names is null || names.Length == 0) {
            return false;
        }

        for (var i = 0; i < names.Length; i++) {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            if (!TryGetJsonPropertyCaseInsensitive(root, name, out var element)) {
                continue;
            }

            value = element.ValueKind switch {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
                _ => element.GetRawText().Trim()
            };
            return true;
        }

        return false;
    }

    private static bool TryGetJsonPropertyCaseInsensitive(JsonElement root, string propertyName, out JsonElement value) {
        value = default;
        if (root.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        foreach (var property in root.EnumerateObject()) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        return false;
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

        if (TryExtractStructuredJsonPayload(normalized, StructuredProfileEnvelopeRegex, out var root)
            && TryReadStructuredString(root, out var structuredScope, "scope")) {
            return ParseProfileUpdateScope(structuredScope);
        }

        if (TryExtractStructuredScopeValue(normalized, out var scopeValue)) {
            return ParseProfileUpdateScope(scopeValue);
        }

        // Safe default for ambiguous free text.
        return ProfileUpdateScope.Session;
    }

    private static bool TryExtractStructuredScopeValue(string text, out string scopeValue) {
        scopeValue = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Length >= 2 && normalized[0] == '{' && normalized[^1] == '}') {
            try {
                using var doc = JsonDocument.Parse(normalized);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("scope", out var scopeElement)
                    && scopeElement.ValueKind == JsonValueKind.String) {
                    var value = (scopeElement.GetString() ?? string.Empty).Trim();
                    if (value.Length > 0) {
                        scopeValue = value;
                        return true;
                    }
                }
            } catch (JsonException) {
                // Best effort only.
            }
        }

        var lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var candidate = lines[i].Trim();
            if (!candidate.StartsWith("scope", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var remainder = candidate.Length > 5 ? candidate.Substring(5).TrimStart() : string.Empty;
            if (remainder.Length == 0) {
                continue;
            }

            if (remainder[0] is not (':' or '=')) {
                continue;
            }

            remainder = remainder.Substring(1).Trim();
            if (remainder.Length == 0) {
                continue;
            }

            var end = 0;
            while (end < remainder.Length && !char.IsWhiteSpace(remainder[end]) && remainder[end] != ',' && remainder[end] != ';') {
                end++;
            }

            var token = (end > 0 ? remainder.Substring(0, end) : remainder).Trim().Trim('"', '\'');
            if (token.Length == 0) {
                continue;
            }

            scopeValue = token;
            return true;
        }

        return false;
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
            ? ProfileUpdateScope.Session
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
