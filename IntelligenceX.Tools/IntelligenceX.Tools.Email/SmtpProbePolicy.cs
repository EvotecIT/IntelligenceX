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

        var validation = ToolAuthenticationProbeValidator.Validate(new ToolAuthenticationProbeValidationRequest {
            RequireProbe = options.RequireSuccessfulSmtpProbeForSend,
            ProbeStore = options.AuthenticationProbeStore,
            ProbeId = probeId,
            ExpectedProbeToolName = ProbeToolName,
            ExpectedAuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            ExpectedTargetFingerprint = BuildTargetFingerprint(smtpOptions),
            MaxAge = TimeSpan.FromSeconds(options.SmtpProbeMaxAgeSeconds),
            NowUtc = nowUtc
        });
        if (validation.IsValid) {
            errorCode = string.Empty;
            error = string.Empty;
            return true;
        }

        var normalizedProbeId = probeId?.Trim() ?? string.Empty;
        switch (validation.Failure) {
            case ToolAuthenticationProbeValidationFailure.ProbeRequired:
                errorCode = "smtp_probe_required";
                error = $"auth_probe_id is required when strict SMTP probe gating is enabled. Run {ProbeToolName} first.";
                break;
            case ToolAuthenticationProbeValidationFailure.ProbeStoreUnavailable:
                errorCode = "smtp_probe_unavailable";
                error = "SMTP probe validation store is not available.";
                break;
            case ToolAuthenticationProbeValidationFailure.ProbeNotFound:
                errorCode = "smtp_probe_not_found";
                error = $"auth_probe_id '{normalizedProbeId}' was not found. Run {ProbeToolName} first.";
                break;
            case ToolAuthenticationProbeValidationFailure.ProbeNotSuccessful:
                errorCode = "smtp_probe_not_successful";
                error = $"auth_probe_id '{normalizedProbeId}' does not represent a successful SMTP probe.";
                break;
            case ToolAuthenticationProbeValidationFailure.ProbeExpired:
                errorCode = "smtp_probe_expired";
                error = $"auth_probe_id '{normalizedProbeId}' expired. Run {ProbeToolName} again.";
                break;
            default:
                errorCode = "smtp_probe_incompatible";
                error = $"auth_probe_id '{normalizedProbeId}' does not match current SMTP endpoint/auth settings.";
                break;
        }

        return false;
    }
}
