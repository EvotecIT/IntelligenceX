using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private static string NormalizeRequestId(string? requestId) {
        return (requestId ?? string.Empty).Trim();
    }

    internal static bool IsRequestIdMatch(string? requestId, string? expectedRequestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        var expected = NormalizeRequestId(expectedRequestId);
        if (expected.Length == 0) {
            return false;
        }

        return string.Equals(id, expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldProcessLiveRequestMessage(
        string? requestId,
        string? activeTurnRequestId,
        string? activeKickoffRequestId,
        bool isSending,
        bool modelKickoffInProgress) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return isSending || modelKickoffInProgress;
        }

        return IsRequestIdMatch(id, activeTurnRequestId)
               || IsRequestIdMatch(id, activeKickoffRequestId);
    }

    private bool IsActiveTurnRequest(string? requestId) {
        return IsRequestIdMatch(requestId, _activeTurnRequestId);
    }

    private bool IsLatestTurnRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_latestTurnRequestId)
               && string.Equals(id, _latestTurnRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsActiveKickoffRequest(string? requestId) {
        return IsRequestIdMatch(requestId, _activeKickoffRequestId);
    }

    private bool ShouldProcessLiveRequestMessage(string? requestId) {
        return ShouldProcessLiveRequestMessage(
            requestId,
            _activeTurnRequestId,
            _activeKickoffRequestId,
            _isSending,
            _modelKickoffInProgress);
    }

    private static bool IsTerminalChatStatus(string? status) {
        var normalized = (status ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return string.Equals(normalized, "completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "done", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "finished", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendUnavailablePacksFromPolicy() {
        var packs = _sessionPolicy?.Packs;
        if (packs is not { Length: > 0 }) {
            return;
        }

        var unavailable = packs
            .Where(static pack => !pack.Enabled && !string.IsNullOrWhiteSpace(pack.DisabledReason))
            .Select(static pack => new {
                Id = (pack.Id ?? string.Empty).Trim(),
                Name = (pack.Name ?? string.Empty).Trim(),
                Reason = (pack.DisabledReason ?? string.Empty).Trim()
            })
            .Where(static pack => pack.Reason.Length > 0)
            .DistinctBy(static pack => pack.Id + "|" + pack.Reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unavailable.Length == 0) {
            return;
        }

        var signaturePayload = unavailable
            .OrderBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Reason, StringComparer.OrdinalIgnoreCase)
            .Select(static pack => new { pack.Id, pack.Reason })
            .ToArray();
        var signature = JsonSerializer.Serialize(signaturePayload);
        if (!_startupUnavailablePackSignatures.Add(signature)) {
            return;
        }

        const int maxShown = 4;
        var shown = unavailable.Length <= maxShown
            ? unavailable
            : unavailable.Take(maxShown).ToArray();

        var lines = new List<string>(shown.Length + 6) {
            "[warning] Some tool packs are unavailable",
            string.Empty,
            $"Found {unavailable.Length} unavailable pack(s):"
        };

        for (var i = 0; i < shown.Length; i++) {
            var pack = shown[i];
            var label = string.IsNullOrWhiteSpace(pack.Name) ? pack.Id : pack.Name;
            lines.Add("- " + label + ": " + pack.Reason);
        }

        if (unavailable.Length > shown.Length) {
            lines.Add($"- +{unavailable.Length - shown.Length} more");
        }

        lines.Add(string.Empty);
        lines.Add("Open Options > Tools to see pack availability details.");

        AppendSystem(string.Join(Environment.NewLine, lines));
    }

    private void AppendStartupToolHealthWarningsFromPolicy() {
        var warnings = _sessionPolicy?.StartupWarnings;
        if (warnings is not { Length: > 0 }) {
            return;
        }

        var toolHealthWarnings = warnings
            .Where(static warning => warning.Contains("[tool health]", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (toolHealthWarnings.Length == 0) {
            return;
        }

        var signature = string.Join("|", toolHealthWarnings.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        if (!_startupToolHealthWarningSignatures.Add(signature)) {
            return;
        }

        const int maxShown = 4;
        var shown = toolHealthWarnings.Length <= maxShown
            ? toolHealthWarnings
            : toolHealthWarnings.Take(maxShown).ToArray();

        var lines = new List<string>(shown.Length + 5) {
            "[warning] Tool health checks need attention",
            string.Empty,
            $"Found {toolHealthWarnings.Length} startup tool health warning(s):"
        };
        for (var i = 0; i < shown.Length; i++) {
            lines.Add("- " + shown[i].Trim());
        }
        if (toolHealthWarnings.Length > shown.Length) {
            lines.Add($"- +{toolHealthWarnings.Length - shown.Length} more");
        }
        lines.Add(string.Empty);
        lines.Add("Check the runtime policy panel for the full startup warning list.");

        AppendSystem(string.Join(Environment.NewLine, lines));
    }

}
