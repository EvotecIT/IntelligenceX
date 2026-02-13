using System;
using System.Collections.Generic;

namespace IntelligenceX.Setup.Onboarding;

/// <summary>
/// Single mismatch entry for onboarding contract verification.
/// </summary>
public sealed class SetupOnboardingContractMismatch {
    /// <summary>
    /// Initializes a mismatch entry.
    /// </summary>
    public SetupOnboardingContractMismatch(string source, string field, string expected, string actual) {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Expected = expected ?? throw new ArgumentNullException(nameof(expected));
        Actual = actual ?? throw new ArgumentNullException(nameof(actual));
    }

    /// <summary>
    /// Source of compared metadata (<c>autodetect</c> or <c>pack_info</c>).
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Name of the compared field.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// Canonical expected value.
    /// </summary>
    public string Expected { get; }

    /// <summary>
    /// Actual value received from caller metadata.
    /// </summary>
    public string Actual { get; }
}

/// <summary>
/// Result payload for onboarding contract metadata verification.
/// </summary>
public sealed class SetupOnboardingContractVerificationResult {
    /// <summary>
    /// Initializes a verification result.
    /// </summary>
    public SetupOnboardingContractVerificationResult(
        bool includeMaintenancePath,
        string expectedContractVersion,
        string expectedContractFingerprint,
        string autodetectContractVersion,
        string autodetectContractFingerprint,
        string? packContractVersion,
        string? packContractFingerprint,
        IReadOnlyList<SetupOnboardingContractMismatch> mismatches) {
        IncludeMaintenancePath = includeMaintenancePath;
        ExpectedContractVersion = expectedContractVersion ?? throw new ArgumentNullException(nameof(expectedContractVersion));
        ExpectedContractFingerprint = expectedContractFingerprint ?? throw new ArgumentNullException(nameof(expectedContractFingerprint));
        AutodetectContractVersion = autodetectContractVersion ?? throw new ArgumentNullException(nameof(autodetectContractVersion));
        AutodetectContractFingerprint = autodetectContractFingerprint ?? throw new ArgumentNullException(nameof(autodetectContractFingerprint));
        PackContractVersion = packContractVersion;
        PackContractFingerprint = packContractFingerprint;
        Mismatches = mismatches ?? throw new ArgumentNullException(nameof(mismatches));
    }

    /// <summary>
    /// Whether maintenance path was included when computing expected fingerprint.
    /// </summary>
    public bool IncludeMaintenancePath { get; }

    /// <summary>
    /// Canonical expected contract version.
    /// </summary>
    public string ExpectedContractVersion { get; }

    /// <summary>
    /// Canonical expected contract fingerprint.
    /// </summary>
    public string ExpectedContractFingerprint { get; }

    /// <summary>
    /// Autodetect contract version provided by caller.
    /// </summary>
    public string AutodetectContractVersion { get; }

    /// <summary>
    /// Autodetect contract fingerprint provided by caller.
    /// </summary>
    public string AutodetectContractFingerprint { get; }

    /// <summary>
    /// Optional pack contract version provided by caller.
    /// </summary>
    public string? PackContractVersion { get; }

    /// <summary>
    /// Optional pack contract fingerprint provided by caller.
    /// </summary>
    public string? PackContractFingerprint { get; }

    /// <summary>
    /// Detected mismatch list.
    /// </summary>
    public IReadOnlyList<SetupOnboardingContractMismatch> Mismatches { get; }

    /// <summary>
    /// Number of detected mismatches.
    /// </summary>
    public int MismatchCount => Mismatches.Count;

    /// <summary>
    /// True when no mismatches were detected.
    /// </summary>
    public bool IsMatch => Mismatches.Count == 0;
}

/// <summary>
/// Verification helper for onboarding contract metadata parity checks.
/// </summary>
public static class SetupOnboardingContractVerification {
    /// <summary>
    /// Verifies autodetect/pack contract metadata against canonical onboarding contract values.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when required autodetect metadata is missing.</exception>
    public static SetupOnboardingContractVerificationResult Verify(
        string autodetectContractVersion,
        string autodetectContractFingerprint,
        string? packContractVersion = null,
        string? packContractFingerprint = null,
        bool includeMaintenancePath = true) {
        if (string.IsNullOrWhiteSpace(autodetectContractVersion)) {
            throw new ArgumentException("autodetectContractVersion is required.", nameof(autodetectContractVersion));
        }
        if (string.IsNullOrWhiteSpace(autodetectContractFingerprint)) {
            throw new ArgumentException("autodetectContractFingerprint is required.", nameof(autodetectContractFingerprint));
        }

        var normalizedAutodetectVersion = autodetectContractVersion.Trim();
        var normalizedAutodetectFingerprint = autodetectContractFingerprint.Trim();
        var normalizedPackVersion = NormalizeOptional(packContractVersion);
        var normalizedPackFingerprint = NormalizeOptional(packContractFingerprint);

        var expectedContractVersion = SetupOnboardingContract.ContractVersion;
        var expectedContractFingerprint = SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath);
        var mismatches = new List<SetupOnboardingContractMismatch>();

        AddMismatchIfDifferent(
            mismatches: mismatches,
            source: "autodetect",
            field: "contract_version",
            expected: expectedContractVersion,
            actual: normalizedAutodetectVersion,
            comparer: StringComparer.Ordinal);
        AddMismatchIfDifferent(
            mismatches: mismatches,
            source: "autodetect",
            field: "contract_fingerprint",
            expected: expectedContractFingerprint,
            actual: normalizedAutodetectFingerprint,
            comparer: StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(normalizedPackVersion)) {
            AddMismatchIfDifferent(
                mismatches: mismatches,
                source: "pack_info",
                field: "contract_version",
                expected: expectedContractVersion,
                actual: normalizedPackVersion!,
                comparer: StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(normalizedPackFingerprint)) {
            AddMismatchIfDifferent(
                mismatches: mismatches,
                source: "pack_info",
                field: "contract_fingerprint",
                expected: expectedContractFingerprint,
                actual: normalizedPackFingerprint!,
                comparer: StringComparer.OrdinalIgnoreCase);
        }

        return new SetupOnboardingContractVerificationResult(
            includeMaintenancePath: includeMaintenancePath,
            expectedContractVersion: expectedContractVersion,
            expectedContractFingerprint: expectedContractFingerprint,
            autodetectContractVersion: normalizedAutodetectVersion,
            autodetectContractFingerprint: normalizedAutodetectFingerprint,
            packContractVersion: normalizedPackVersion,
            packContractFingerprint: normalizedPackFingerprint,
            mismatches: mismatches.ToArray());
    }

    private static void AddMismatchIfDifferent(
        List<SetupOnboardingContractMismatch> mismatches,
        string source,
        string field,
        string expected,
        string actual,
        StringComparer comparer) {
        if (comparer.Equals(expected, actual)) {
            return;
        }

        mismatches.Add(new SetupOnboardingContractMismatch(
            source: source,
            field: field,
            expected: expected,
            actual: actual));
    }

    private static string? NormalizeOptional(string? value) {
        if (value is null) {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
