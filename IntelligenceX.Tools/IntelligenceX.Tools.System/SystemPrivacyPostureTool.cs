using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Privacy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns privacy-related machine policy posture for the local or remote Windows host.
/// </summary>
public sealed class SystemPrivacyPostureTool : SystemToolBase, ITool {
    private sealed record PrivacyPostureRequest(
        string? ComputerName,
        string Target);

    private sealed record PrivacyPostureResponse(
        string ComputerName,
        int? AllowTelemetryRaw,
        string? TelemetryLevel,
        bool? ActivityFeedEnabled,
        bool? PublishUserActivitiesEnabled,
        bool? UploadUserActivitiesEnabled,
        bool? TailoredExperiencesDisabled,
        bool? ConsumerFeaturesDisabled,
        bool? CopilotDisabled,
        bool? RecallDisabled,
        int PolicySignalsCount,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_privacy_posture",
        "Return privacy-related machine policy posture (telemetry/activity history/Copilot/Recall) for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPrivacyPostureTool"/> class.
    /// </summary>
    public SystemPrivacyPostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<PrivacyPostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<PrivacyPostureRequest>.Success(new PrivacyPostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<PrivacyPostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_privacy_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await PrivacyPosture
                .GetAsync(request.ComputerName, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var warnings = BuildWarnings(posture);
            var model = new PrivacyPostureResponse(
                ComputerName: effectiveComputerName,
                AllowTelemetryRaw: posture.AllowTelemetryRaw,
                TelemetryLevel: posture.TelemetryLevel?.ToString(),
                ActivityFeedEnabled: posture.ActivityFeedEnabled,
                PublishUserActivitiesEnabled: posture.PublishUserActivitiesEnabled,
                UploadUserActivitiesEnabled: posture.UploadUserActivitiesEnabled,
                TailoredExperiencesDisabled: posture.TailoredExperiencesDisabled,
                ConsumerFeaturesDisabled: posture.ConsumerFeaturesDisabled,
                CopilotDisabled: posture.CopilotDisabled,
                RecallDisabled: posture.RecallDisabled,
                PolicySignalsCount: posture.PolicySignalsCount,
                Warnings: warnings);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_privacy_posture",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: Math.Max(posture.PolicySignalsCount, 1),
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "Privacy posture",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("Telemetry Level", posture.TelemetryLevel?.ToString() ?? posture.AllowTelemetryRaw?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Activity Feed Enabled", FormatNullableBool(posture.ActivityFeedEnabled)),
                    ("Publish User Activities", FormatNullableBool(posture.PublishUserActivitiesEnabled)),
                    ("Upload User Activities", FormatNullableBool(posture.UploadUserActivitiesEnabled)),
                    ("Tailored Experiences Disabled", FormatNullableBool(posture.TailoredExperiencesDisabled)),
                    ("Consumer Features Disabled", FormatNullableBool(posture.ConsumerFeaturesDisabled)),
                    ("Copilot Disabled", FormatNullableBool(posture.CopilotDisabled)),
                    ("Recall Disabled", FormatNullableBool(posture.RecallDisabled)),
                    ("Policy Signals Count", posture.PolicySignalsCount.ToString(CultureInfo.InvariantCulture)),
                    ("Warnings", warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Privacy posture query failed.");
        }
    }

    private static IReadOnlyList<string> BuildWarnings(PrivacyPostureInfo posture) {
        var warnings = new List<string>();
        if (posture.TelemetryLevel == TelemetryLevel.Full) {
            warnings.Add("Telemetry policy is set to Full.");
        }
        if (posture.ActivityFeedEnabled == true) {
            warnings.Add("Activity Feed is enabled by policy.");
        }
        if (posture.PublishUserActivitiesEnabled == true || posture.UploadUserActivitiesEnabled == true) {
            warnings.Add("User activity publishing/upload is enabled by policy.");
        }
        if (posture.TailoredExperiencesDisabled == false) {
            warnings.Add("Tailored experiences with diagnostic data are not disabled.");
        }
        if (posture.ConsumerFeaturesDisabled == false) {
            warnings.Add("Windows consumer features are not disabled.");
        }
        if (posture.CopilotDisabled == false) {
            warnings.Add("Windows Copilot is not disabled by policy.");
        }
        if (posture.RecallDisabled == false) {
            warnings.Add("Windows Recall-related features are not disabled by policy.");
        }

        return warnings;
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}
