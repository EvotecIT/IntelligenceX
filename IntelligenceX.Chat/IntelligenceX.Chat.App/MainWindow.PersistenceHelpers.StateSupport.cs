using System;
using System.Globalization;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Theming;
using Microsoft.UI.Input;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static DateTime EnsureUtc(DateTime value) {
        if (value == default) {
            return DateTime.UtcNow;
        }

        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root) {
        root = default;
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        } catch {
            return false;
        }
    }

    private static string? TryGetString(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static bool? TryGetBoolean(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.True) {
            return true;
        }

        if (el.ValueKind == JsonValueKind.False) {
            return false;
        }

        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) {
            return parsed;
        }

        return null;
    }

    private static int? TryGetInt32(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsedNumber)) {
            return parsedNumber;
        }

        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString)) {
            return parsedString;
        }

        return null;
    }

    private static long? TryGetInt64(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var parsedNumber)) {
            return parsedNumber;
        }

        if (el.ValueKind == JsonValueKind.String
            && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString)) {
            return parsedString;
        }

        return null;
    }

    private static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var v = value.Trim();
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAppProfileName(string? value) {
        var name = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }

    private void CaptureAutonomyOverridesIntoAppState() {
        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _appState.AutonomyPlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop;
        _appState.AutonomyMaxReviewPasses = _autonomyMaxReviewPasses;
        _appState.AutonomyModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds;
    }

    private void RestoreAutonomyOverridesFromAppState() {
        _autonomyMaxToolRounds = NormalizeAutonomyInt(
            _appState.AutonomyMaxToolRounds,
            min: ChatRequestOptionLimits.MinToolRounds,
            max: ChatRequestOptionLimits.MaxToolRounds);
        _autonomyParallelTools = _appState.AutonomyParallelTools;
        _autonomyTurnTimeoutSeconds = NormalizeAutonomyInt(
            _appState.AutonomyTurnTimeoutSeconds,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyToolTimeoutSeconds = NormalizeAutonomyInt(
            _appState.AutonomyToolTimeoutSeconds,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyWeightedToolRouting = _appState.AutonomyWeightedToolRouting;
        _autonomyMaxCandidateTools = NormalizeAutonomyInt(
            _appState.AutonomyMaxCandidateTools,
            min: ChatRequestOptionLimits.MinCandidateTools,
            max: ChatRequestOptionLimits.MaxCandidateTools);
        _autonomyPlanExecuteReviewLoop = _appState.AutonomyPlanExecuteReviewLoop;
        _autonomyMaxReviewPasses = NormalizeAutonomyInt(
            _appState.AutonomyMaxReviewPasses,
            min: 0,
            max: ChatRequestOptionLimits.MaxReviewPasses);
        _autonomyModelHeartbeatSeconds = NormalizeAutonomyInt(
            _appState.AutonomyModelHeartbeatSeconds,
            min: 0,
            max: ChatRequestOptionLimits.MaxModelHeartbeatSeconds);

        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _appState.AutonomyPlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop;
        _appState.AutonomyMaxReviewPasses = _autonomyMaxReviewPasses;
        _appState.AutonomyModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds;
    }

    private static string? NormalizeTheme(string? value) {
        return ThemeContract.Normalize(value);
    }

    private static string ResolveTimestampMode(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "seconds";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "date-minutes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_minutes", StringComparison.OrdinalIgnoreCase)) {
            return "date-minutes";
        }

        if (string.Equals(normalized, "date-seconds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_seconds", StringComparison.OrdinalIgnoreCase)) {
            return "date-seconds";
        }

        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "minutes";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "seconds";
        }

        return "custom";
    }

    private static string ResolveTimestampFormat(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "HH:mm:ss";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "date-minutes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_minutes", StringComparison.OrdinalIgnoreCase)) {
            return "yyyy-MM-dd HH:mm";
        }

        if (string.Equals(normalized, "date-seconds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_seconds", StringComparison.OrdinalIgnoreCase)) {
            return "yyyy-MM-dd HH:mm:ss";
        }

        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm:ss";
        }

        try {
            _ = DateTime.Now.ToString(normalized, CultureInfo.InvariantCulture);
            return normalized;
        } catch {
            return "HH:mm:ss";
        }
    }

    private static GlobalWheelHookMode ResolveGlobalWheelHookMode(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return GlobalWheelHookMode.Auto;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "always", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)) {
            return GlobalWheelHookMode.Always;
        }

        if (string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)) {
            return GlobalWheelHookMode.Off;
        }

        return GlobalWheelHookMode.Auto;
    }

    private static int? NormalizeAutonomyInt(int? value, int min, int max) {
        if (!value.HasValue) {
            return null;
        }

        var v = value.Value;
        if (v < min || v > max) {
            return null;
        }

        return v;
    }
}
