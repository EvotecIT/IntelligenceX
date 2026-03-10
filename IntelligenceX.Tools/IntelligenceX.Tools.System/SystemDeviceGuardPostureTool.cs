using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.SecurityPolicy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns Device Guard and virtualization-based security posture for the local or remote Windows host.
/// </summary>
public sealed class SystemDeviceGuardPostureTool : SystemToolBase, ITool {
    private sealed record DeviceGuardPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record DeviceGuardPostureResponse(
        string ComputerName,
        bool? EnableVbs,
        int? RequirePlatformSecurityFeatures,
        bool? HvciEnabled,
        bool? CredentialGuardEnabled,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_device_guard_posture",
        "Return Device Guard, VBS, HVCI, and Credential Guard posture for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:device_guard_posture", "intent:vbs_policy", "scope:host_virtualization_security" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemDeviceGuardPostureTool"/> class.
    /// </summary>
    public SystemDeviceGuardPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<DeviceGuardPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<DeviceGuardPostureRequest>.Success(new DeviceGuardPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DeviceGuardPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_device_guard_posture");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var posture = DeviceGuardPolicyQuery.Get(request.ComputerName);
            var warnings = BuildWarnings(posture);
            var effectiveComputerName = request.Target;
            var model = new DeviceGuardPostureResponse(
                ComputerName: effectiveComputerName,
                EnableVbs: posture.EnableVbs,
                RequirePlatformSecurityFeatures: posture.RequirePlatformSecurityFeatures,
                HvciEnabled: posture.HvciEnabled,
                CredentialGuardEnabled: posture.CredentialGuardEnabled,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_device_guard_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: model,
                title: "Device Guard posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("VBS Enabled", FormatNullableBool(posture.EnableVbs)),
                    ("Platform Security Features", posture.RequirePlatformSecurityFeatures?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("HVCI Enabled", FormatNullableBool(posture.HvciEnabled)),
                    ("Credential Guard Enabled", FormatNullableBool(posture.CredentialGuardEnabled)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Device Guard posture query failed."));
        }
    }

    private static IReadOnlyList<string> BuildWarnings(DeviceGuardPolicyState posture) {
        var warnings = new List<string>();
        if (posture.EnableVbs == false) {
            warnings.Add("Virtualization-based security is disabled.");
        }
        if (posture.HvciEnabled == false) {
            warnings.Add("HVCI is disabled.");
        }
        if (posture.CredentialGuardEnabled == false) {
            warnings.Add("Credential Guard is disabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
