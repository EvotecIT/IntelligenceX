using System;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Email;

internal static class SmtpProbePolicy {
    internal const string ProbeToolName = "email_smtp_probe";

    internal static string CreateProbeId() {
        return $"smtp_probe_{Guid.NewGuid():N}";
    }

    internal static string BuildTargetFingerprint(SmtpAccountOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        static string Normalize(string? value) {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        return string.Join("|", new[] {
            Normalize(options.Server),
            options.Port.ToString(),
            Normalize(options.UserName),
            Normalize(options.SecureSocketOptions),
            options.UseSsl ? "use_ssl=1" : "use_ssl=0"
        });
    }

    internal static ToolAuthenticationProbeRecord CreateSuccessRecord(
        SmtpAccountOptions smtpOptions,
        DateTimeOffset probedAtUtc,
        string? probeId = null) {
        return new ToolAuthenticationProbeRecord {
            ProbeId = string.IsNullOrWhiteSpace(probeId) ? CreateProbeId() : probeId.Trim(),
            ToolName = ProbeToolName,
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = BuildTargetFingerprint(smtpOptions),
            ProbedAtUtc = probedAtUtc,
            IsSuccessful = true
        };
    }

    internal static bool TryValidateForStrictSend(
        EmailToolOptions options,
        SmtpAccountOptions smtpOptions,
        string? probeId,
        DateTimeOffset nowUtc,
        out string errorCode,
        out string error) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        if (smtpOptions is null) {
            throw new ArgumentNullException(nameof(smtpOptions));
        }

        if (!options.RequireSuccessfulSmtpProbeForSend) {
            errorCode = string.Empty;
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(probeId)) {
            errorCode = "smtp_probe_required";
            error = $"auth_probe_id is required when strict SMTP probe gating is enabled. Run {ProbeToolName} first.";
            return false;
        }

        if (!options.AuthenticationProbeStore.TryGet(probeId, out var probeRecord)) {
            errorCode = "smtp_probe_not_found";
            error = $"auth_probe_id '{probeId.Trim()}' was not found. Run {ProbeToolName} first.";
            return false;
        }

        if (!probeRecord.IsSuccessful) {
            errorCode = "smtp_probe_not_successful";
            error = $"auth_probe_id '{probeId.Trim()}' does not represent a successful SMTP probe.";
            return false;
        }

        if (!string.Equals(probeRecord.ToolName, ProbeToolName, StringComparison.OrdinalIgnoreCase)) {
            errorCode = "smtp_probe_incompatible";
            error = $"auth_probe_id '{probeId.Trim()}' is not an SMTP probe.";
            return false;
        }
        if (!string.Equals(
                probeRecord.AuthenticationContractId,
                ToolAuthenticationContract.DefaultContractId,
                StringComparison.OrdinalIgnoreCase)) {
            errorCode = "smtp_probe_incompatible";
            error = $"auth_probe_id '{probeId.Trim()}' does not match expected authentication contract.";
            return false;
        }

        var expectedFingerprint = BuildTargetFingerprint(smtpOptions);
        if (!string.Equals(probeRecord.TargetFingerprint, expectedFingerprint, StringComparison.Ordinal)) {
            errorCode = "smtp_probe_incompatible";
            error = $"auth_probe_id '{probeId.Trim()}' does not match current SMTP endpoint/auth settings.";
            return false;
        }

        var maxAge = TimeSpan.FromSeconds(options.SmtpProbeMaxAgeSeconds);
        if (probeRecord.ProbedAtUtc + maxAge < nowUtc) {
            errorCode = "smtp_probe_expired";
            error = $"auth_probe_id '{probeId.Trim()}' expired. Run {ProbeToolName} again.";
            return false;
        }

        errorCode = string.Empty;
        error = string.Empty;
        return true;
    }
}
