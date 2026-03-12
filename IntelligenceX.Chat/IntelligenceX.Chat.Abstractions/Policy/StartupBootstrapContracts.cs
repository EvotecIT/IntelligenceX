using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared ids, labels, and cache-mode helpers for startup bootstrap telemetry.
/// </summary>
public static class StartupBootstrapContracts {
    /// <summary>
    /// Canonical phase id for runtime policy bootstrap timing.
    /// </summary>
    public const string PhaseRuntimePolicyId = "runtime_policy";

    /// <summary>
    /// Canonical phase id for bootstrap-option resolution timing.
    /// </summary>
    public const string PhaseBootstrapOptionsId = "bootstrap_options";

    /// <summary>
    /// Canonical phase id for tool-pack loading timing.
    /// </summary>
    public const string PhasePackLoadId = "pack_load";

    /// <summary>
    /// Canonical phase id for tool-pack registration timing.
    /// </summary>
    public const string PhasePackRegisterId = "pack_register";

    /// <summary>
    /// Canonical phase id for registry finalization timing.
    /// </summary>
    public const string PhaseRegistryFinalizeId = "registry_finalize";

    /// <summary>
    /// Canonical phase id for persisted tooling bootstrap cache hits.
    /// </summary>
    public const string PhaseCacheHitId = "cache_hit";

    /// <summary>
    /// Canonical label for runtime policy bootstrap timing.
    /// </summary>
    public const string PhaseRuntimePolicyLabel = "runtime policy";

    /// <summary>
    /// Canonical label for bootstrap-option resolution timing.
    /// </summary>
    public const string PhaseBootstrapOptionsLabel = "bootstrap options";

    /// <summary>
    /// Canonical label for tool-pack loading timing.
    /// </summary>
    public const string PhasePackLoadLabel = "pack load";

    /// <summary>
    /// Canonical label for tool-pack registration timing.
    /// </summary>
    public const string PhasePackRegisterLabel = "pack register";

    /// <summary>
    /// Canonical label for registry finalization timing.
    /// </summary>
    public const string PhaseRegistryFinalizeLabel = "registry finalize";

    /// <summary>
    /// Canonical label for persisted tooling bootstrap cache hits.
    /// </summary>
    public const string PhaseCacheHitLabel = "cache hit";

    /// <summary>
    /// Cache-mode token used when no bootstrap state is available.
    /// </summary>
    public const string CacheModeUnknown = "unknown";

    /// <summary>
    /// Cache-mode token used when bootstrap telemetry was restored from a cache hit.
    /// </summary>
    public const string CacheModeHit = "hit";

    /// <summary>
    /// Cache-mode token used when bootstrap telemetry reflects a live rebuild.
    /// </summary>
    public const string CacheModeMiss = "miss";

    /// <summary>
    /// Cache-mode token used when a persisted preview is shown while live rebuild continues.
    /// </summary>
    public const string CacheModePersistedPreview = "persisted_preview";

    /// <summary>
    /// Creates startup bootstrap phase telemetry using the canonical label for the supplied id.
    /// </summary>
    public static SessionStartupBootstrapPhaseTelemetryDto CreatePhase(string phaseId, long durationMs, int order) {
        return new SessionStartupBootstrapPhaseTelemetryDto {
            Id = phaseId,
            Label = ResolvePhaseLabel(phaseId),
            DurationMs = durationMs,
            Order = order
        };
    }

    /// <summary>
    /// Resolves the canonical human-facing label for a startup bootstrap phase id.
    /// </summary>
    public static string ResolvePhaseLabel(string? phaseId) {
        var normalized = (phaseId ?? string.Empty).Trim();
        return normalized switch {
            PhaseRuntimePolicyId => PhaseRuntimePolicyLabel,
            PhaseBootstrapOptionsId => PhaseBootstrapOptionsLabel,
            PhasePackLoadId => PhasePackLoadLabel,
            PhasePackRegisterId => PhasePackRegisterLabel,
            PhaseRegistryFinalizeId => PhaseRegistryFinalizeLabel,
            PhaseCacheHitId => PhaseCacheHitLabel,
            _ => normalized
        };
    }

    /// <summary>
    /// Determines whether the supplied phase id represents a bootstrap cache hit.
    /// </summary>
    public static bool IsCacheHitPhaseId(string? phaseId) {
        return string.Equals((phaseId ?? string.Empty).Trim(), PhaseCacheHitId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the bootstrap cache mode token from telemetry and startup warnings.
    /// </summary>
    public static string ResolveCacheModeToken(
        SessionStartupBootstrapTelemetryDto? bootstrap,
        string[]? startupWarnings) {
        if (bootstrap?.Phases is { Length: > 0 } phases) {
            for (var i = 0; i < phases.Length; i++) {
                if (IsCacheHitPhaseId(phases[i].Id)) {
                    return CacheModeHit;
                }
            }
        }

        if (startupWarnings is { Length: > 0 }) {
            for (var i = 0; i < startupWarnings.Length; i++) {
                if (StartupBootstrapWarningFormatter.IsPersistedPreviewRestoredWarning(startupWarnings[i])) {
                    return CacheModePersistedPreview;
                }
            }
        }

        return bootstrap is null ? CacheModeUnknown : CacheModeMiss;
    }
}
