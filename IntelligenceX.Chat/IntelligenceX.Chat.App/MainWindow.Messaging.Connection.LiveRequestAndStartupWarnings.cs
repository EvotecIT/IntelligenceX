using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
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
        var effectivePacks = RuntimeToolingMetadataResolver.ResolveEffectivePacks(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogCapabilitySnapshot);
        var unavailable = BuildUnavailablePackWarningEntries(effectivePacks);
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

        AppendSystem(string.Join(Environment.NewLine, BuildUnavailablePackWarningLines(unavailable)));
    }

    internal static string[] BuildUnavailablePackWarningLines(ToolPackInfoDto[]? packs) {
        return BuildUnavailablePackWarningLines(BuildUnavailablePackWarningEntries(packs));
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

        var effectivePacks = RuntimeToolingMetadataResolver.ResolveEffectivePacks(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogCapabilitySnapshot);
        AppendSystem(string.Join(Environment.NewLine, BuildStartupToolHealthWarningLines(toolHealthWarnings, effectivePacks)));
    }

    internal static string FormatStartupToolHealthWarningForDisplay(string? warning, ToolPackInfoDto[]? packs = null) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Startup tool health probe failed.";
        }

        var displayParts = StartupToolHealthWarningFormatter.BuildDisplayParts(
            normalized,
            normalizedPackId => ResolveStartupToolHealthPackName(normalizedPackId, packs));
        if (displayParts is null) {
            return normalized;
        }

        return "**" + displayParts.Value.Title + "** " + displayParts.Value.Summary;
    }

    internal static string[] BuildStartupToolHealthWarningLines(string[]? warnings, ToolPackInfoDto[]? packs = null) {
        var toolHealthWarnings = warnings ?? Array.Empty<string>();
        if (toolHealthWarnings.Length == 0) {
            return Array.Empty<string>();
        }

        return StartupWarningPreviewFormatter.BuildLines(
            toolHealthWarnings,
            warning => FormatStartupToolHealthWarningForDisplay(warning, packs),
            "[warning] Tool health checks need attention",
            "Found {0} startup tool health warning(s):",
            "Check the runtime policy panel for the full startup warning list.");
    }

    private static StartupUnavailablePackWarningEntry[] BuildUnavailablePackWarningEntries(ToolPackInfoDto[]? packs) {
        return StartupUnavailablePackWarningFormatter.BuildEntries(
            packs,
            static pack => pack.Id,
            static pack => pack.Name,
            static pack => pack.Enabled,
            static pack => pack.DisabledReason);
    }

    private static string[] BuildUnavailablePackWarningLines(StartupUnavailablePackWarningEntry[] unavailable) {
        if (unavailable.Length == 0) {
            return Array.Empty<string>();
        }

        return StartupWarningPreviewFormatter.BuildLines(
            unavailable,
            static pack => pack.Label + ": " + pack.Reason,
            "[warning] Some tool packs are unavailable",
            "Found {0} unavailable pack(s):",
            "Open Options > Tools to see pack availability details.");
    }

    private static string? ResolveStartupToolHealthPackName(string normalizedPackId, ToolPackInfoDto[]? packs) {
        if (packs is not { Length: > 0 } || normalizedPackId.Length == 0) {
            return null;
        }

        for (var i = 0; i < packs.Length; i++) {
            var candidate = packs[i];
            if (!string.Equals(StartupToolHealthWarningFormatter.NormalizePackId(candidate.Id), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var candidateName = (candidate.Name ?? string.Empty).Trim();
            if (candidateName.Length > 0) {
                return candidateName;
            }
        }

        return null;
    }

    private void AppendStartupBootstrapSummaryFromPolicy() {
        if (!ShouldAppendStartupBootstrapSummary(_sessionPolicy)) {
            return;
        }

        var telemetry = _sessionPolicy!.StartupBootstrap!;
        var phases = telemetry.Phases ?? Array.Empty<SessionStartupBootstrapPhaseTelemetryDto>();
        var phaseSignature = phases.Length == 0
            ? string.Empty
            : string.Join(",",
                phases
                    .OrderBy(static phase => phase.Order)
                    .ThenBy(static phase => phase.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(static phase => $"{phase.Id}:{phase.DurationMs}"));
        var signature = string.Join("|", new[] {
            telemetry.TotalMs.ToString(),
            telemetry.PackLoadMs.ToString(),
            telemetry.PackRegisterMs.ToString(),
            telemetry.RegistryFinalizeMs.ToString(),
            telemetry.RegistryMs.ToString(),
            telemetry.SlowPackCount.ToString(),
            telemetry.PackProgressProcessed.ToString(),
            telemetry.PackProgressTotal.ToString(),
            telemetry.SlowPackRegistrationCount.ToString(),
            telemetry.PackRegistrationProgressProcessed.ToString(),
            telemetry.PackRegistrationProgressTotal.ToString(),
            telemetry.SlowPluginCount.ToString(),
            telemetry.PluginProgressProcessed.ToString(),
            telemetry.PluginProgressTotal.ToString(),
            telemetry.PacksLoaded.ToString(),
            telemetry.PacksDisabled.ToString(),
            telemetry.Tools.ToString(),
            telemetry.SlowestPhaseId ?? string.Empty,
            telemetry.SlowestPhaseMs.ToString(),
            phaseSignature
        });
        if (!_startupBootstrapSummarySignatures.Add(signature)) {
            return;
        }

        AppendSystem(string.Join(Environment.NewLine, BuildStartupBootstrapSummaryLines(telemetry)));
    }

    internal static bool ShouldAppendStartupBootstrapSummary(SessionPolicyDto? policy) {
        var telemetry = policy?.StartupBootstrap;
        if (telemetry is null) {
            return false;
        }

        if (!IsStartupBootstrapSignalWorthy(telemetry)) {
            return false;
        }

        return !string.Equals(
            ResolveStartupBootstrapCacheModeTokenFromPolicy(policy),
            StartupBootstrapContracts.CacheModePersistedPreview,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsStartupBootstrapSignalWorthy(SessionStartupBootstrapTelemetryDto telemetry) {
        ArgumentNullException.ThrowIfNull(telemetry);
        var descriptorDiscoveryMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhaseDescriptorDiscoveryId);
        var packActivationMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhasePackActivationId);
        var activationFinalizeMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhaseRegistryActivationFinalizeId);
        return telemetry.TotalMs >= 1000
               || descriptorDiscoveryMs >= 800
               || packActivationMs >= 500
               || activationFinalizeMs >= 500
               || telemetry.SlowPackCount > 0
               || telemetry.SlowPackRegistrationCount > 0
               || telemetry.SlowPluginCount > 0;
    }

    internal static string[] BuildStartupBootstrapSummaryLines(SessionStartupBootstrapTelemetryDto telemetry) {
        ArgumentNullException.ThrowIfNull(telemetry);
        var descriptorDiscoveryMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhaseDescriptorDiscoveryId);
        var packActivationMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhasePackActivationId);
        var activationFinalizeMs = StartupBootstrapContracts.ResolvePhaseDuration(
            telemetry,
            StartupBootstrapContracts.PhaseRegistryActivationFinalizeId);

        var lines = new List<string> {
            "[startup] Runtime tool bootstrap summary",
            string.Empty,
            $"- Total: {FormatBootstrapDuration(telemetry.TotalMs)} " +
            $"(descriptor discovery {FormatBootstrapDuration(descriptorDiscoveryMs)}, " +
            $"pack activation {FormatBootstrapDuration(packActivationMs)}, " +
            $"activation finalize {FormatBootstrapDuration(activationFinalizeMs)}, " +
            $"registry total {FormatBootstrapDuration(telemetry.RegistryMs)})",
            $"- Packs loaded: {telemetry.PacksLoaded}, disabled: {telemetry.PacksDisabled}, tools: {telemetry.Tools}"
        };

        var phases = telemetry.Phases ?? Array.Empty<SessionStartupBootstrapPhaseTelemetryDto>();
        if (phases.Length > 0) {
            var phaseSummary = string.Join(", ",
                phases
                    .OrderBy(static phase => phase.Order)
                    .ThenBy(static phase => phase.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(static phase => $"{FormatBootstrapPhaseLabel(phase)} {FormatBootstrapDuration(phase.DurationMs)}"));
            lines.Add($"- Startup phases: {phaseSummary}");
        }

        var slowestPhaseLabel = ResolveSlowestPhaseLabel(telemetry);
        if (slowestPhaseLabel is not null && telemetry.SlowestPhaseMs > 0) {
            var totalForRatio = Math.Max(1, telemetry.TotalMs);
            var ratio = (double)Math.Max(0, telemetry.SlowestPhaseMs) / totalForRatio * 100d;
            lines.Add($"- Slowest phase: {slowestPhaseLabel} ({FormatBootstrapDuration(telemetry.SlowestPhaseMs)}, {ratio:0.#}%)");
        }

        if (telemetry.PackProgressTotal > 0 || telemetry.PackProgressProcessed > 0) {
            var processed = Math.Max(telemetry.PackProgressProcessed, 0);
            var total = Math.Max(processed, telemetry.PackProgressTotal);
            lines.Add($"- Pack bootstrap progress: {processed}/{total} steps");
        }

        if (telemetry.PackRegistrationProgressTotal > 0 || telemetry.PackRegistrationProgressProcessed > 0) {
            var processed = Math.Max(telemetry.PackRegistrationProgressProcessed, 0);
            var total = Math.Max(processed, telemetry.PackRegistrationProgressTotal);
            lines.Add($"- Pack registration progress: {processed}/{total} packs");
        }

        if (telemetry.SlowPackCount > 0) {
            lines.Add($"- Slow pack loads detected: {telemetry.SlowPackCount} (top {telemetry.SlowPackTopCount} captured in startup warnings)");
        }

        if (telemetry.SlowPackRegistrationCount > 0) {
            lines.Add($"- Slow pack registrations detected: {telemetry.SlowPackRegistrationCount} (top {telemetry.SlowPackRegistrationTopCount} captured in startup warnings)");
        }

        if (telemetry.PluginRoots > 0 || telemetry.PluginProgressTotal > 0 || telemetry.PluginProgressProcessed > 0) {
            var processed = Math.Max(telemetry.PluginProgressProcessed, 0);
            var total = Math.Max(processed, telemetry.PluginProgressTotal);
            lines.Add($"- Plugin roots: {telemetry.PluginRoots}, folders processed: {processed}/{total}");
        }

        if (telemetry.SlowPluginCount > 0) {
            lines.Add($"- Slow plugin loads detected: {telemetry.SlowPluginCount} (top {telemetry.SlowPluginTopCount} captured in startup warnings)");
        }

        lines.Add(string.Empty);
        lines.Add("Open the runtime policy panel for detailed startup warnings.");
        return lines.ToArray();
    }

    internal static string BuildStartupBootstrapStatusDetail(SessionStartupBootstrapTelemetryDto? telemetry) {
        if (telemetry is null || telemetry.TotalMs <= 0) {
            return string.Empty;
        }

        if (telemetry.Phases is { Length: > 0 } phases
            && phases.Any(static phase => StartupBootstrapContracts.IsPersistedPreviewPhaseId(phase.Id))) {
            var packCount = Math.Max(0, telemetry.PacksLoaded);
            var toolCount = Math.Max(0, telemetry.Tools);
            if (packCount > 0 || toolCount > 0) {
                return $"descriptor preview ({packCount} packs, {toolCount} tools)";
            }

            return "descriptor preview";
        }

        var detail = "tool bootstrap " + FormatBootstrapDuration(telemetry.TotalMs);
        var slowestPhaseLabel = ResolveSlowestPhaseLabel(telemetry);
        if (slowestPhaseLabel is null || telemetry.SlowestPhaseMs <= 0) {
            return detail;
        }

        return detail + $" (slowest: {slowestPhaseLabel} {FormatBootstrapDuration(telemetry.SlowestPhaseMs)})";
    }

    private static string FormatBootstrapDuration(long milliseconds) {
        var bounded = Math.Max(0, milliseconds);
        var elapsed = TimeSpan.FromMilliseconds(bounded);
        if (elapsed.TotalSeconds >= 1) {
            return $"{elapsed.TotalSeconds:0.0}s";
        }

        return $"{Math.Max(1, bounded)}ms";
    }

    private static string FormatBootstrapPhaseLabel(SessionStartupBootstrapPhaseTelemetryDto phase) {
        var label = (phase.Label ?? string.Empty).Trim();
        if (label.Length > 0) {
            return label;
        }

        var id = (phase.Id ?? string.Empty).Trim();
        if (id.Length == 0) {
            return "phase";
        }

        return id.Replace('_', ' ');
    }

    private static string? ResolveSlowestPhaseLabel(SessionStartupBootstrapTelemetryDto telemetry) {
        var label = (telemetry.SlowestPhaseLabel ?? string.Empty).Trim();
        if (label.Length > 0) {
            return label;
        }

        var slowestId = (telemetry.SlowestPhaseId ?? string.Empty).Trim();
        if (slowestId.Length == 0) {
            return null;
        }

        var phases = telemetry.Phases ?? Array.Empty<SessionStartupBootstrapPhaseTelemetryDto>();
        var match = phases
            .FirstOrDefault(phase => string.Equals((phase.Id ?? string.Empty).Trim(), slowestId, StringComparison.OrdinalIgnoreCase));
        if (match is not null) {
            return FormatBootstrapPhaseLabel(match);
        }

        return slowestId.Replace('_', ' ');
    }
}
