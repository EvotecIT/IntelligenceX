using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Applies governed Windows Event Log channel policy changes (dry-run by default).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogChannelPolicySetTool : EventLogToolBase, ITool {
    private sealed record ChannelPolicySetRequest(
        string? MachineName,
        string TargetMachineName,
        string LogName,
        bool? RequestedIsEnabled,
        long? RequestedMaximumSizeBytes,
        global::System.Diagnostics.Eventing.Reader.EventLogMode? RequestedMode,
        bool Apply);

    private sealed record ChannelPolicySnapshot(
        string LogName,
        string MachineName,
        bool? IsEnabled,
        long? MaximumSizeBytes,
        string? LogFilePath,
        string? Isolation,
        string? Mode,
        bool HasSecurityDescriptor);

    private sealed record ChannelPolicyRollbackArguments(
        string LogName,
        string? MachineName,
        bool? IsEnabled,
        long? MaximumSizeBytes,
        string? Mode,
        bool Apply);

    private sealed record ChannelPolicyApplyDetails(
        bool Success,
        bool PartialSuccess,
        IReadOnlyList<string> AppliedProperties,
        IReadOnlyList<string> SkippedOrUnsupported,
        IReadOnlyList<string> Errors);

    private sealed record ChannelPolicySetResult(
        string LogName,
        string MachineName,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        bool PartialSuccess,
        string Message,
        bool? RequestedIsEnabled,
        long? RequestedMaximumSizeBytes,
        string? RequestedMode,
        IReadOnlyList<string> RequestedChanges,
        IReadOnlyList<string> Warnings,
        ChannelPolicySnapshot Before,
        ChannelPolicySnapshot After,
        ChannelPolicyRollbackArguments RollbackArguments,
        ChannelPolicyApplyDetails? ApplyDetails);

    private sealed record ChannelPolicyMutationPlan(
        bool CanApply,
        bool Changed,
        ChannelPolicySnapshot PredictedAfter,
        IReadOnlyList<string> RequestedChanges,
        string PreviewMessage,
        string ApplyMessage);

    private static readonly IReadOnlyDictionary<string, global::System.Diagnostics.Eventing.Reader.EventLogMode> ModeByName =
        new Dictionary<string, global::System.Diagnostics.Eventing.Reader.EventLogMode>(StringComparer.OrdinalIgnoreCase) {
            ["circular"] = global::System.Diagnostics.Eventing.Reader.EventLogMode.Circular,
            ["retain"] = global::System.Diagnostics.Eventing.Reader.EventLogMode.Retain,
            ["auto_backup"] = global::System.Diagnostics.Eventing.Reader.EventLogMode.AutoBackup
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_channel_policy_set",
        "Governed Event Log channel policy changes for enable/disable, size, and retention mode. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for the local machine.")),
                ("log_name", ToolSchema.String("Exact Windows Event Log channel name to manage.")),
                ("is_enabled", ToolSchema.Boolean("Optional target enabled state for the channel.")),
                ("maximum_size_bytes", ToolSchema.Integer("Optional target maximum size in bytes for the channel.")),
                ("mode", ToolSchema.String("Optional retention mode for the channel.").Enum("circular", "retain", "auto_backup")),
                ("apply", ToolSchema.Boolean("When true, performs the channel policy write. Otherwise returns a dry-run preview.")))
            .Required("log_name")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogChannelPolicySetTool"/> class.
    /// </summary>
    public EventLogChannelPolicySetTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<ChannelPolicySetRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var machineName = reader.OptionalString("machine_name");
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<ChannelPolicySetRequest>.Failure(logNameError);
            }

            var requestedIsEnabled = reader.OptionalBoolean("is_enabled");
            var maximumSizeRaw = reader.OptionalInt64("maximum_size_bytes");
            long? requestedMaximumSizeBytes = null;
            if (maximumSizeRaw.HasValue) {
                if (maximumSizeRaw.Value < 65_536) {
                    return ToolRequestBindingResult<ChannelPolicySetRequest>.Failure(
                        "maximum_size_bytes must be at least 65536 (64 KiB).");
                }

                requestedMaximumSizeBytes = Math.Min(maximumSizeRaw.Value, 1_099_511_627_776L);
            }

            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("mode"),
                    ModeByName,
                    "mode",
                    out global::System.Diagnostics.Eventing.Reader.EventLogMode? requestedMode,
                    out var modeError)) {
                return ToolRequestBindingResult<ChannelPolicySetRequest>.Failure(
                    modeError ?? "mode must be one of: circular, retain, auto_backup.");
            }

            if (requestedIsEnabled is null && requestedMaximumSizeBytes is null && requestedMode is null) {
                return ToolRequestBindingResult<ChannelPolicySetRequest>.Failure(
                    "Provide at least one requested change: is_enabled, maximum_size_bytes, or mode.");
            }

            return ToolRequestBindingResult<ChannelPolicySetRequest>.Success(new ChannelPolicySetRequest(
                MachineName: machineName,
                TargetMachineName: string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim(),
                LogName: logName,
                RequestedIsEnabled: requestedIsEnabled,
                RequestedMaximumSizeBytes: requestedMaximumSizeBytes,
                RequestedMode: requestedMode,
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<ChannelPolicySetRequest> context,
        CancellationToken cancellationToken) {
        return Task.Run(() => ExecuteSync(context, cancellationToken), cancellationToken);
    }

    private string ExecuteSync(
        ToolPipelineContext<ChannelPolicySetRequest> context,
        CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) {
            return ToolResultV2.Error(
                errorCode: "platform_not_supported",
                error: "eventlog_channel_policy_set is supported only on Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var beforeAttempt = TryGetChannelPolicySnapshot(request.LogName, request.MachineName, request.TargetMachineName);
        if (beforeAttempt.ErrorResponse is not null) {
            return beforeAttempt.ErrorResponse;
        }

        if (beforeAttempt.Snapshot is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Event Log channel '{request.LogName}' was not found or its policy could not be read on '{request.TargetMachineName}'.",
                hints: new[] {
                    "Call eventlog_channels_list to confirm the exact log_name on the target host.",
                    "Use eventlog_connectivity_probe when remote Event Log reachability or permissions are uncertain."
                });
        }

        var before = beforeAttempt.Snapshot;
        var plan = BuildMutationPlan(request, before);
        if (request.Apply && !plan.CanApply) {
            return ToolResultV2.Error(
                errorCode: "precondition_failed",
                error: plan.ApplyMessage);
        }

        var after = plan.PredictedAfter;
        var postChangeVerified = true;
        var writeExecuted = false;
        var partialSuccess = false;
        var warnings = new List<string>();
        ChannelPolicyApplyDetails? applyDetails = null;

        if (request.Apply && plan.Changed) {
            var applyResult = SearchEvents.SetChannelPolicyDetailed(new ChannelPolicy {
                LogName = request.LogName,
                MachineName = request.MachineName,
                IsEnabled = request.RequestedIsEnabled,
                MaximumSizeInBytes = request.RequestedMaximumSizeBytes,
                Mode = request.RequestedMode
            });

            writeExecuted = applyResult.Success || applyResult.PartialSuccess || applyResult.AppliedProperties.Count > 0;
            partialSuccess = applyResult.PartialSuccess || applyResult.SkippedOrUnsupported.Count > 0;
            warnings.AddRange(applyResult.SkippedOrUnsupported);
            warnings.AddRange(applyResult.Errors);
            applyDetails = new ChannelPolicyApplyDetails(
                Success: applyResult.Success,
                PartialSuccess: applyResult.PartialSuccess,
                AppliedProperties: applyResult.AppliedProperties.ToArray(),
                SkippedOrUnsupported: applyResult.SkippedOrUnsupported.ToArray(),
                Errors: applyResult.Errors.ToArray());

            if (!applyResult.Success && applyResult.AppliedProperties.Count == 0) {
                return ToolResultV2.Error(
                    errorCode: "action_failed",
                    error: $"The requested Event Log channel policy write failed for '{request.LogName}' on '{request.TargetMachineName}'.",
                    hints: BuildApplyFailureHints(applyResult));
            }

            var afterAttempt = TryGetChannelPolicySnapshot(request.LogName, request.MachineName, request.TargetMachineName);
            if (afterAttempt.ErrorResponse is not null || afterAttempt.Snapshot is null) {
                postChangeVerified = false;
                warnings.Add("Post-change verification could not re-read the channel policy after the write.");
            } else {
                after = afterAttempt.Snapshot;
            }
        }

        var result = new ChannelPolicySetResult(
            LogName: before.LogName,
            MachineName: before.MachineName,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            PartialSuccess: partialSuccess,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            RequestedIsEnabled: request.RequestedIsEnabled,
            RequestedMaximumSizeBytes: request.RequestedMaximumSizeBytes,
            RequestedMode: request.RequestedMode is null ? null : NormalizeMode(request.RequestedMode.Value),
            RequestedChanges: plan.RequestedChanges,
            Warnings: warnings,
            Before: before,
            After: after,
            RollbackArguments: BuildRollbackArguments(before, request.MachineName),
            ApplyDetails: applyDetails);

        return CreateSuccessResponse(result);
    }

    private static (ChannelPolicySnapshot? Snapshot, string? ErrorResponse) TryGetChannelPolicySnapshot(
        string logName,
        string? machineName,
        string targetMachineName) {
        try {
            var policy = SearchEvents.GetChannelPolicy(logName, machineName);
            return policy is null
                ? (null, null)
                : (CreateSnapshot(policy, targetMachineName), null);
        } catch (Exception ex) {
            return (null, ErrorFromException(
                ex,
                defaultMessage: "Event Log channel policy query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed"));
        }
    }

    private static ChannelPolicyMutationPlan BuildMutationPlan(
        ChannelPolicySetRequest request,
        ChannelPolicySnapshot before) {
        var requestedChanges = new List<string>(3);
        var after = before;

        if (request.RequestedIsEnabled.HasValue) {
            requestedChanges.Add("is_enabled");
            after = after with { IsEnabled = request.RequestedIsEnabled.Value };
        }

        if (request.RequestedMaximumSizeBytes.HasValue) {
            requestedChanges.Add("maximum_size_bytes");
            after = after with { MaximumSizeBytes = request.RequestedMaximumSizeBytes.Value };
        }

        if (request.RequestedMode.HasValue) {
            requestedChanges.Add("mode");
            after = after with { Mode = NormalizeMode(request.RequestedMode.Value) };
        }

        var changed =
            (request.RequestedIsEnabled.HasValue && request.RequestedIsEnabled != before.IsEnabled)
            || (request.RequestedMaximumSizeBytes.HasValue && request.RequestedMaximumSizeBytes != before.MaximumSizeBytes)
            || (request.RequestedMode is not null && !string.Equals(before.Mode, NormalizeMode(request.RequestedMode.Value), StringComparison.OrdinalIgnoreCase));

        if (!changed) {
            const string noChangeMessage = "Requested channel policy already matches the current state. No change required.";
            return new ChannelPolicyMutationPlan(
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                RequestedChanges: requestedChanges,
                PreviewMessage: noChangeMessage,
                ApplyMessage: "Channel policy already matched the requested state. No change applied.");
        }

        var previewMessage = $"Preview only: channel policy would be updated for '{request.LogName}'.";
        var applyMessage = $"Channel policy updated for '{request.LogName}'.";
        return new ChannelPolicyMutationPlan(
            CanApply: true,
            Changed: true,
            PredictedAfter: after,
            RequestedChanges: requestedChanges,
            PreviewMessage: previewMessage,
            ApplyMessage: applyMessage);
    }

    private static ChannelPolicySnapshot CreateSnapshot(ChannelPolicy policy, string targetMachineName) {
        return new ChannelPolicySnapshot(
            LogName: policy.LogName,
            MachineName: string.IsNullOrWhiteSpace(policy.MachineName) ? targetMachineName : policy.MachineName,
            IsEnabled: policy.IsEnabled,
            MaximumSizeBytes: policy.MaximumSizeInBytes,
            LogFilePath: ToolArgs.NormalizeOptional(policy.LogFilePath),
            Isolation: policy.Isolation?.ToString(),
            Mode: policy.Mode is null ? null : NormalizeMode(policy.Mode.Value),
            HasSecurityDescriptor: !string.IsNullOrWhiteSpace(policy.SecurityDescriptor));
    }

    private static ChannelPolicyRollbackArguments BuildRollbackArguments(ChannelPolicySnapshot before, string? machineName) {
        return new ChannelPolicyRollbackArguments(
            LogName: before.LogName,
            MachineName: machineName,
            IsEnabled: before.IsEnabled,
            MaximumSizeBytes: before.MaximumSizeBytes,
            Mode: before.Mode,
            Apply: true);
    }

    private static IReadOnlyList<string> BuildApplyFailureHints(ChannelPolicyApplyResult applyResult) {
        var hints = new List<string> {
            "Call eventlog_connectivity_probe to confirm Event Log reachability and permissions before retrying.",
            "Call eventlog_channels_list with the same machine_name to confirm the channel is still visible."
        };

        if (applyResult.SkippedOrUnsupported.Count > 0) {
            hints.Add("The target channel or API surface reported unsupported properties; review skipped_or_unsupported before retrying.");
        }

        return hints;
    }

    private static string CreateSuccessResponse(ChannelPolicySetResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Machine", result.MachineName),
            ("Channel", result.LogName),
            ("Enabled", FormatNullableBoolean(result.Before.IsEnabled) + " -> " + FormatNullableBoolean(result.After.IsEnabled)),
            ("Maximum size", FormatNullableInt64(result.Before.MaximumSizeBytes) + " -> " + FormatNullableInt64(result.After.MaximumSizeBytes)),
            ("Retention mode", FormatNullableString(result.Before.Mode) + " -> " + FormatNullableString(result.After.Mode)),
            ("Message", result.Message)
        };
        if (result.RequestedChanges.Count > 0) {
            facts.Add(("Requested changes", string.Join(", ", result.RequestedChanges)));
        }
        if (result.PartialSuccess) {
            facts.Add(("Apply outcome", "partial_success"));
        }
        if (result.Warnings.Count > 0) {
            facts.Add(("Warnings", string.Join(" | ", result.Warnings)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("log_name", result.LogName)
            .Add("machine_name", result.MachineName)
            .Add("write_candidate", true)
            .Add("changed", result.Changed)
            .Add("can_apply", result.CanApply)
            .Add("post_change_verified", result.PostChangeVerified)
            .Add("write_executed", result.WriteExecuted)
            .Add("partial_success", result.PartialSuccess)
            .Add("requested_change_count", result.RequestedChanges.Count);
        if (result.RequestedIsEnabled.HasValue) {
            meta.Add("requested_is_enabled", result.RequestedIsEnabled.Value);
        }
        if (result.RequestedMaximumSizeBytes.HasValue) {
            meta.Add("requested_maximum_size_bytes", result.RequestedMaximumSizeBytes.Value);
        }
        if (!string.IsNullOrWhiteSpace(result.RequestedMode)) {
            meta.Add("requested_mode", result.RequestedMode);
        }
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "eventlog_channel_policy_set",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "Event Log channel policy");
    }

    private static string NormalizeMode(global::System.Diagnostics.Eventing.Reader.EventLogMode mode) {
        return mode switch {
            global::System.Diagnostics.Eventing.Reader.EventLogMode.Circular => "circular",
            global::System.Diagnostics.Eventing.Reader.EventLogMode.Retain => "retain",
            global::System.Diagnostics.Eventing.Reader.EventLogMode.AutoBackup => "auto_backup",
            _ => mode.ToString().ToLowerInvariant()
        };
    }

    private static string FormatNullableBoolean(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }

    private static string FormatNullableInt64(long? value) {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string FormatNullableString(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
