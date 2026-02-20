using System;

namespace IntelligenceX.Tools;

/// <summary>
/// Failure reasons returned by <see cref="ToolAuthenticationProbeValidator"/>.
/// </summary>
public enum ToolAuthenticationProbeValidationFailure {
    /// <summary>
    /// Probe validation succeeded.
    /// </summary>
    None = 0,

    /// <summary>
    /// Probe validation is required but no probe id was provided.
    /// </summary>
    ProbeRequired,

    /// <summary>
    /// Probe validation is required but store is unavailable.
    /// </summary>
    ProbeStoreUnavailable,

    /// <summary>
    /// Probe id does not exist in the configured store.
    /// </summary>
    ProbeNotFound,

    /// <summary>
    /// Probe exists but did not complete successfully.
    /// </summary>
    ProbeNotSuccessful,

    /// <summary>
    /// Probe exists but is incompatible with expected tool/auth/target context.
    /// </summary>
    ProbeIncompatible,

    /// <summary>
    /// Probe exists but is older than the allowed max age.
    /// </summary>
    ProbeExpired
}

/// <summary>
/// Probe validation request used by <see cref="ToolAuthenticationProbeValidator"/>.
/// </summary>
public sealed class ToolAuthenticationProbeValidationRequest {
    /// <summary>
    /// When true, probe validation is required.
    /// </summary>
    public bool RequireProbe { get; set; }

    /// <summary>
    /// Probe store used for lookup.
    /// </summary>
    public IToolAuthenticationProbeStore? ProbeStore { get; set; }

    /// <summary>
    /// Probe id provided by caller.
    /// </summary>
    public string? ProbeId { get; set; }

    /// <summary>
    /// Expected probe tool name. Empty value disables this compatibility check.
    /// </summary>
    public string? ExpectedProbeToolName { get; set; }

    /// <summary>
    /// Expected authentication contract id. Empty value disables this compatibility check.
    /// </summary>
    public string? ExpectedAuthenticationContractId { get; set; }

    /// <summary>
    /// Expected target fingerprint. Empty value disables this compatibility check.
    /// </summary>
    public string? ExpectedTargetFingerprint { get; set; }

    /// <summary>
    /// Maximum accepted probe age.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Current reference time in UTC.
    /// </summary>
    public DateTimeOffset NowUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result produced by <see cref="ToolAuthenticationProbeValidator"/>.
/// </summary>
public sealed class ToolAuthenticationProbeValidationResult {
    /// <summary>
    /// True when probe validation succeeded.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Failure reason when <see cref="IsValid"/> is false.
    /// </summary>
    public ToolAuthenticationProbeValidationFailure Failure { get; set; }

    /// <summary>
    /// Resolved probe record when available.
    /// </summary>
    public ToolAuthenticationProbeRecord? ProbeRecord { get; set; }
}

/// <summary>
/// Shared helper that validates probe identifiers against expected authentication context.
/// </summary>
public static class ToolAuthenticationProbeValidator {
    /// <summary>
    /// Validates an authentication probe reference.
    /// </summary>
    /// <param name="request">Validation request.</param>
    /// <returns>Validation result.</returns>
    public static ToolAuthenticationProbeValidationResult Validate(ToolAuthenticationProbeValidationRequest request) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        if (!request.RequireProbe) {
            return Success();
        }

        var probeId = request.ProbeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(probeId)) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeRequired);
        }

        var store = request.ProbeStore;
        if (store is null) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeStoreUnavailable);
        }

        if (!store.TryGet(probeId, out var record)) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeNotFound);
        }

        if (!record.IsSuccessful) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeNotSuccessful, record);
        }

        if (!MatchesExpected(record.ToolName, request.ExpectedProbeToolName)) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeIncompatible, record);
        }

        if (!MatchesExpected(record.AuthenticationContractId, request.ExpectedAuthenticationContractId)) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeIncompatible, record);
        }

        if (!MatchesExpectedOrdinal(record.TargetFingerprint, request.ExpectedTargetFingerprint)) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeIncompatible, record);
        }

        if (request.MaxAge > TimeSpan.Zero && record.ProbedAtUtc + request.MaxAge < request.NowUtc) {
            return Deny(ToolAuthenticationProbeValidationFailure.ProbeExpired, record);
        }

        return Success(record);
    }

    private static bool MatchesExpected(string? actual, string? expected) {
        var normalizedExpected = (expected ?? string.Empty).Trim();
        if (normalizedExpected.Length == 0) {
            return true;
        }

        var normalizedActual = actual?.Trim() ?? string.Empty;
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesExpectedOrdinal(string? actual, string? expected) {
        var normalizedExpected = (expected ?? string.Empty).Trim();
        if (normalizedExpected.Length == 0) {
            return true;
        }

        var normalizedActual = actual?.Trim() ?? string.Empty;
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal);
    }

    private static ToolAuthenticationProbeValidationResult Success(ToolAuthenticationProbeRecord? record = null) {
        return new ToolAuthenticationProbeValidationResult {
            IsValid = true,
            Failure = ToolAuthenticationProbeValidationFailure.None,
            ProbeRecord = record
        };
    }

    private static ToolAuthenticationProbeValidationResult Deny(
        ToolAuthenticationProbeValidationFailure failure,
        ToolAuthenticationProbeRecord? record = null) {
        return new ToolAuthenticationProbeValidationResult {
            IsValid = false,
            Failure = failure,
            ProbeRecord = record
        };
    }
}
