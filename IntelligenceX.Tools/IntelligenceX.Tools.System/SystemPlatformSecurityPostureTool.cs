using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.PlatformSecurity;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns platform security posture for the local or remote Windows host.
/// </summary>
public sealed class SystemPlatformSecurityPostureTool : SystemToolBase, ITool {
    private sealed record PlatformSecurityRequest(
        string? ComputerName,
        string Target);

    private sealed record PlatformSecurityResponse(
        string ComputerName,
        string? FirmwareType,
        bool? SecureBootEnabled,
        bool? SecureBootCapable,
        bool? SecureBootRequiredByPolicy,
        bool TpmPresent,
        bool? TpmReady,
        bool? TpmEnabled,
        bool? TpmActivated,
        bool? TpmOwned,
        string? TpmSpecVersion,
        bool? HvciConfigured,
        bool? VulnerableDriverBlocklistEnabled,
        string? DriverSigningPolicyMode,
        bool BootTestSigningEnabled,
        bool BootDebugEnabled,
        bool BootNoIntegrityChecksEnabled,
        int ConfiguredSecurityServices,
        int RunningSecurityServices,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_platform_security_posture",
        "Return platform security posture (firmware, Secure Boot, TPM, HVCI, driver trust, boot integrity) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPlatformSecurityPostureTool"/> class.
    /// </summary>
    public SystemPlatformSecurityPostureTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<PlatformSecurityRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<PlatformSecurityRequest>.Success(new PlatformSecurityRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<PlatformSecurityRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_platform_security_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await PlatformSecurity.GetAsync(request.ComputerName, cancellationToken).ConfigureAwait(false);
            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var warnings = BuildWarnings(posture);
            var model = new PlatformSecurityResponse(
                ComputerName: effectiveComputerName,
                FirmwareType: posture.FirmwareType?.ToString(),
                SecureBootEnabled: posture.SecureBoot.Enabled,
                SecureBootCapable: posture.SecureBoot.Capable,
                SecureBootRequiredByPolicy: posture.SecureBoot.RequiredByPolicy,
                TpmPresent: posture.Tpm.Present,
                TpmReady: posture.Tpm.Ready,
                TpmEnabled: posture.Tpm.Enabled,
                TpmActivated: posture.Tpm.Activated,
                TpmOwned: posture.Tpm.Owned,
                TpmSpecVersion: posture.Tpm.SpecVersion,
                HvciConfigured: posture.DriverTrust.HvciConfigured,
                VulnerableDriverBlocklistEnabled: posture.DriverTrust.VulnerableDriverBlocklistEnabled,
                DriverSigningPolicyMode: posture.DriverTrust.DriverSigningPolicyMode,
                BootTestSigningEnabled: posture.DriverTrust.BootTestSigningEnabled,
                BootDebugEnabled: posture.DriverTrust.BootDebugEnabled,
                BootNoIntegrityChecksEnabled: posture.DriverTrust.BootNoIntegrityChecksEnabled,
                ConfiguredSecurityServices: posture.DriverTrust.SecurityServicesConfigured.Count,
                RunningSecurityServices: posture.DriverTrust.SecurityServicesRunning.Count,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_platform_security_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(1, posture.DriverTrust.SecurityServicesConfigured.Count + posture.DriverTrust.SecurityServicesRunning.Count),
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Platform security posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Firmware Type", posture.FirmwareType?.ToString() ?? "unknown"),
                    ("Secure Boot Enabled", FormatNullableBool(posture.SecureBoot.Enabled)),
                    ("Secure Boot Capable", FormatNullableBool(posture.SecureBoot.Capable)),
                    ("Secure Boot Required By Policy", FormatNullableBool(posture.SecureBoot.RequiredByPolicy)),
                    ("TPM Present", posture.Tpm.Present ? "true" : "false"),
                    ("TPM Ready", FormatNullableBool(posture.Tpm.Ready)),
                    ("TPM Spec Version", posture.Tpm.SpecVersion ?? "unknown"),
                    ("HVCI Configured", FormatNullableBool(posture.DriverTrust.HvciConfigured)),
                    ("Vulnerable Driver Blocklist Enabled", FormatNullableBool(posture.DriverTrust.VulnerableDriverBlocklistEnabled)),
                    ("Driver Signing Policy", posture.DriverTrust.DriverSigningPolicyMode ?? "unknown"),
                    ("Boot Test Signing Enabled", posture.DriverTrust.BootTestSigningEnabled ? "true" : "false"),
                    ("Boot Debug Enabled", posture.DriverTrust.BootDebugEnabled ? "true" : "false"),
                    ("Boot No Integrity Checks Enabled", posture.DriverTrust.BootNoIntegrityChecksEnabled ? "true" : "false"),
                    ("Configured Security Services", posture.DriverTrust.SecurityServicesConfigured.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Running Security Services", posture.DriverTrust.SecurityServicesRunning.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Platform security posture query failed.");
        }
    }

    private static IReadOnlyList<string> BuildWarnings(PlatformSecurityInfo posture) {
        var warnings = new List<string>();
        if (posture.SecureBoot.Enabled == false) {
            warnings.Add("Secure Boot is not enabled.");
        }
        if (posture.Tpm.Present && posture.Tpm.Ready == false) {
            warnings.Add("TPM is present but not ready.");
        }
        if (posture.DriverTrust.HvciConfigured == false) {
            warnings.Add("HVCI is not configured.");
        }
        if (posture.DriverTrust.VulnerableDriverBlocklistEnabled == false) {
            warnings.Add("Vulnerable driver blocklist is not enabled.");
        }
        if (posture.DriverTrust.BootTestSigningEnabled) {
            warnings.Add("Boot test-signing mode is enabled.");
        }
        if (posture.DriverTrust.BootDebugEnabled) {
            warnings.Add("Boot debugging is enabled.");
        }
        if (posture.DriverTrust.BootNoIntegrityChecksEnabled) {
            warnings.Add("Boot no-integrity-checks mode is enabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : "unknown";
}
