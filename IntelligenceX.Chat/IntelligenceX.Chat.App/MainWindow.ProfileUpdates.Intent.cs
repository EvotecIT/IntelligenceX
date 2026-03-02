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
        @"```(?:ix_profile|ix_profile_update)\s*([\s\S]*?)\s*```",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StructuredMemoryEnvelopeRegex = new(
        @"```(?:ix_memory|ix_memory_note)\s*([\s\S]*?)\s*```",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly char[] StructuredFieldLineSeparators = new[] { '\r', '\n' };
    private static readonly char[] StructuredFieldSegmentSeparators = new[] { ';', '\uFF1B' };
    private static readonly char[] StructuredFieldDelimiters = new[] { ':', '=', '\uFF1A', '\uFF1D' };
    private static readonly char[] StructuredFieldTrimChars =
        new[] { '"', '\'', '`', '\u201c', '\u201d', '\u201e', '\u201f', '\u00ab', '\u00bb', '\u2039', '\u203a' };
    private static readonly char[] MemoryFactTrailingTrimChars =
        new[] { '.', '!', '?', ';', ':', '\u3002', '\uFF01', '\uFF1F', '\u061B', '\uFF1A', '\uFF61', '\uFE12', '\uFE56', '\uFE57' };

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
        if (!TryExtractStructuredFieldMap(text, StructuredMemoryEnvelopeRegex, out var fields)) {
            return false;
        }

        if (!TryReadStructuredField(
                fields,
                out var candidate,
                "memory",
                "memory_fact",
                "memory_note",
                "fact",
                "note",
                "text",
                "value",
                "ix_memory")) {
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
        var normalized = (candidate ?? string.Empty).Trim().Trim(MemoryFactTrailingTrimChars);
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
        if (!TryExtractStructuredFieldMap(text, StructuredProfileEnvelopeRegex, out var fields)) {
            return false;
        }

        var hasAny = false;
        if (TryReadStructuredField(
                fields,
                out var userName,
                "userName",
                "user_name",
                "username",
                "user",
                "name",
                "display_name",
                "displayName",
                "ix_user_name")
            && !string.IsNullOrWhiteSpace(userName)) {
            intent.UserName = userName.Trim();
            intent.HasUserName = true;
            hasAny = true;
        }

        if (TryReadStructuredField(
                fields,
                out var persona,
                "assistantPersona",
                "assistant_persona",
                "persona",
                "style",
                "tone",
                "mode",
                "ix_assistant_persona")
            && !string.IsNullOrWhiteSpace(persona)) {
            intent.AssistantPersona = persona.Trim();
            intent.HasAssistantPersona = true;
            hasAny = true;
        }

        if (TryReadStructuredField(
                fields,
                out var theme,
                "themePreset",
                "theme_preset",
                "theme",
                "ix_theme")
            && !string.IsNullOrWhiteSpace(theme)) {
            intent.ThemePreset = theme.Trim();
            intent.HasThemePreset = true;
            hasAny = true;
        }

        if (TryReadStructuredField(fields, out var scopeValue, "scope", "profile_scope", "ix_scope")) {
            var parsedScope = ParseProfileUpdateScope(scopeValue);
            if (parsedScope != ProfileUpdateScope.Unspecified) {
                intent.Scope = parsedScope;
            }
        }

        return hasAny;
    }

    private static bool TryExtractStructuredFieldMap(
        string text,
        Regex envelopeRegex,
        out Dictionary<string, string> fields) {
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryExtractStructuredPayloadText(text, envelopeRegex, out var payload, out var fromEnvelope)) {
            return false;
        }

        if (TryParseStructuredJsonObject(payload, fields)) {
            return fields.Count > 0;
        }

        if (!fromEnvelope && !LooksLikeStructuredFieldPayload(payload)) {
            return false;
        }

        return TryParseStructuredKeyValuePayload(payload, fields) && fields.Count > 0;
    }

    private static bool TryExtractStructuredPayloadText(
        string text,
        Regex envelopeRegex,
        out string payload,
        out bool fromEnvelope) {
        payload = string.Empty;
        fromEnvelope = false;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var matches = envelopeRegex.Matches(normalized);
        if (matches.Count > 0) {
            var last = matches[matches.Count - 1];
            if (last.Groups.Count < 2) {
                return false;
            }

            fromEnvelope = true;
            payload = (last.Groups[1].Value ?? string.Empty).Trim();
        } else {
            payload = normalized;
        }

        return payload.Length > 0;
    }

    private static bool TryParseStructuredJsonObject(string payload, Dictionary<string, string> fields) {
        var normalized = (payload ?? string.Empty).Trim();
        if (normalized.Length < 2 || normalized[0] != '{' || normalized[^1] != '}') {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            foreach (var property in doc.RootElement.EnumerateObject()) {
                var key = (property.Name ?? string.Empty).Trim();
                if (!IsStructuredFieldKeyCandidate(key)) {
                    continue;
                }

                var value = ReadJsonElementAsStructuredFieldValue(property.Value);
                if (value.Length == 0) {
                    continue;
                }

                fields[key] = value;
            }

            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryParseStructuredKeyValuePayload(string payload, Dictionary<string, string> fields) {
        var normalized = (payload ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var lines = normalized.Split(StructuredFieldLineSeparators, StringSplitOptions.RemoveEmptyEntries);
        var parsedAny = false;
        for (var i = 0; i < lines.Length; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }

            var segments = line.Split(StructuredFieldSegmentSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                if (!TryParseStructuredKeyValueSegment(segments[segmentIndex], out var key, out var value)) {
                    continue;
                }

                fields[key] = value;
                parsedAny = true;
            }
        }

        return parsedAny;
    }

    private static bool TryParseStructuredKeyValueSegment(string segment, out string key, out string value) {
        key = string.Empty;
        value = string.Empty;
        var normalized = NormalizeStructuredDelimiterCharacters((segment ?? string.Empty).Trim());
        if (normalized.Length == 0) {
            return false;
        }

        var delimiterIndex = normalized.IndexOfAny(StructuredFieldDelimiters);
        if (delimiterIndex <= 0 || delimiterIndex >= normalized.Length - 1) {
            return false;
        }

        var candidateKey = normalized[..delimiterIndex].Trim();
        if (!IsStructuredFieldKeyCandidate(candidateKey)) {
            return false;
        }

        var candidateValue = normalized[(delimiterIndex + 1)..].Trim();
        if (candidateValue.Length == 0) {
            return false;
        }

        key = candidateKey;
        value = TrimStructuredFieldValue(candidateValue);
        return value.Length > 0;
    }

    private static string NormalizeStructuredDelimiterCharacters(string text) {
        return (text ?? string.Empty)
            .Replace('\uFF1A', ':')
            .Replace('\uFE13', ':')
            .Replace('\uFE55', ':')
            .Replace('\uFF1D', '=')
            .Replace('\uFE66', '=');
    }

    private static bool LooksLikeStructuredFieldPayload(string payload) {
        var normalized = (payload ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var lines = normalized.Split(StructuredFieldLineSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            var segments = line.Split(StructuredFieldSegmentSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                if (TryParseStructuredKeyValueSegment(segments[segmentIndex], out _, out _)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStructuredFieldKeyCandidate(string key) {
        var normalized = (key ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > 64) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.') {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string ReadJsonElementAsStructuredFieldValue(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => TrimStructuredFieldValue(element.GetString() ?? string.Empty),
            _ => TrimStructuredFieldValue(element.GetRawText())
        };
    }

    private static string TrimStructuredFieldValue(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.Trim(StructuredFieldTrimChars).Trim();
        return normalized;
    }

    private static bool TryReadStructuredField(IReadOnlyDictionary<string, string> fields, out string value, params string[] names) {
        value = string.Empty;
        if (fields is null || fields.Count == 0 || names is null || names.Length == 0) {
            return false;
        }

        for (var i = 0; i < names.Length; i++) {
            var name = (names[i] ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            if (fields.TryGetValue(name, out var directValue)) {
                value = (directValue ?? string.Empty).Trim();
                return true;
            }

            foreach (var pair in fields) {
                if (!StructuredFieldNamesMatch(pair.Key, name)) {
                    continue;
                }

                value = (pair.Value ?? string.Empty).Trim();
                return true;
            }
        }

        return false;
    }

    private static bool StructuredFieldNamesMatch(string candidate, string expected) {
        return string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase)
               || string.Equals(NormalizeStructuredFieldName(candidate), NormalizeStructuredFieldName(expected), StringComparison.Ordinal);
    }

    private static string NormalizeStructuredFieldName(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var source = value.Trim();
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++) {
            var ch = source[i];
            if (char.IsLetterOrDigit(ch)) {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
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

        if (TryExtractStructuredFieldMap(normalized, StructuredProfileEnvelopeRegex, out var fields)
            && TryReadStructuredField(fields, out var structuredScope, "scope", "profile_scope", "ix_scope")) {
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

        if (TryExtractStructuredFieldMap(normalized, StructuredProfileEnvelopeRegex, out var fields)
            && TryReadStructuredField(fields, out var mappedScopeValue, "scope", "profile_scope", "ix_scope")
            && mappedScopeValue.Length > 0) {
            scopeValue = mappedScopeValue;
            return true;
        }

        var lines = normalized.Split(StructuredFieldLineSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var segments = lines[i].Split(StructuredFieldSegmentSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                if (!TryParseStructuredKeyValueSegment(segments[segmentIndex], out var key, out var value)
                    || !StructuredFieldNamesMatch(key, "scope")) {
                    continue;
                }

                var token = value.Trim();
                if (token.Length == 0) {
                    continue;
                }

                scopeValue = token;
                return true;
            }

            if (!TryParseStructuredKeyValueSegment(lines[i], out var directKey, out var directValue)
                || !StructuredFieldNamesMatch(directKey, "scope")) {
                continue;
            }

            scopeValue = directValue.Trim();
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
