using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.AppControl;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns application-control posture for the local or remote Windows host.
/// </summary>
public sealed class SystemAppControlPostureTool : SystemToolBase, ITool {
    private sealed record AppControlRequest(
        string? ComputerName,
        string Target);

    private sealed record AppControlResponse(
        string ComputerName,
        bool? AppLockerPolicyPresent,
        bool? AppLockerServiceInstalled,
        bool? AppLockerServiceRunning,
        string? AppLockerServiceStartupType,
        int AppLockerCollections,
        int AppLockerRuleCount,
        bool? WdacPolicyPresent,
        string KernelModeEnforcement,
        string UserModeEnforcement,
        int ConfiguredSecurityServices,
        int RunningSecurityServices,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_app_control_posture",
        "Return app-control posture (AppLocker and WDAC) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAppControlPostureTool"/> class.
    /// </summary>
    public SystemAppControlPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<AppControlRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<AppControlRequest>.Success(new AppControlRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<AppControlRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_app_control_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await AppControl.GetAsync(request.ComputerName, cancellationToken).ConfigureAwait(false);
            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var warnings = BuildWarnings(posture);
            var totalRuleCount = posture.Collections.Sum(static collection => Math.Max(0, collection.RuleCount));
            var model = new AppControlResponse(
                ComputerName: effectiveComputerName,
                AppLockerPolicyPresent: posture.AppLockerPolicyPresent,
                AppLockerServiceInstalled: posture.AppLockerServiceInstalled,
                AppLockerServiceRunning: posture.AppLockerServiceRunning,
                AppLockerServiceStartupType: posture.AppLockerServiceStartupType,
                AppLockerCollections: posture.Collections.Count,
                AppLockerRuleCount: totalRuleCount,
                WdacPolicyPresent: posture.Wdac.PolicyPresent,
                KernelModeEnforcement: posture.Wdac.KernelModeEnforcement.ToString(),
                UserModeEnforcement: posture.Wdac.UserModeEnforcement.ToString(),
                ConfiguredSecurityServices: posture.Wdac.SecurityServicesConfigured.Count,
                RunningSecurityServices: posture.Wdac.SecurityServicesRunning.Count,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_app_control_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(1, posture.Collections.Count),
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "App control posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("AppLocker Policy Present", FormatNullableBool(posture.AppLockerPolicyPresent)),
                    ("AppLocker Service Installed", FormatNullableBool(posture.AppLockerServiceInstalled)),
                    ("AppLocker Service Running", FormatNullableBool(posture.AppLockerServiceRunning)),
                    ("AppLocker Startup Type", posture.AppLockerServiceStartupType ?? "unknown"),
                    ("AppLocker Collections", posture.Collections.Count.ToString(CultureInfo.InvariantCulture)),
                    ("AppLocker Rules", totalRuleCount.ToString(CultureInfo.InvariantCulture)),
                    ("WDAC Policy Present", FormatNullableBool(posture.Wdac.PolicyPresent)),
                    ("WDAC Kernel Enforcement", posture.Wdac.KernelModeEnforcement.ToString()),
                    ("WDAC User Enforcement", posture.Wdac.UserModeEnforcement.ToString()),
                    ("Configured Security Services", posture.Wdac.SecurityServicesConfigured.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Running Security Services", posture.Wdac.SecurityServicesRunning.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "App-control posture query failed.");
        }
    }

    private static IReadOnlyList<string> BuildWarnings(AppControlInfo posture) {
        var warnings = new List<string>();
        if (posture.AppLockerPolicyPresent != true && posture.Wdac.PolicyPresent != true) {
            warnings.Add("No AppLocker or WDAC policy indicators were discovered.");
        }
        if (posture.AppLockerPolicyPresent == true && posture.AppLockerServiceRunning == false) {
            warnings.Add("AppLocker policy indicators exist but AppIDSvc is not running.");
        }
        if (posture.Wdac.PolicyPresent == true && posture.Wdac.KernelModeEnforcement == WdacEnforcementMode.Off) {
            warnings.Add("WDAC policy indicators exist but kernel-mode enforcement is disabled.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : "unknown";
}
