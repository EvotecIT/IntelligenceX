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
/// Ensures a classic Windows Event Log and source exist with optional governed configuration changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogClassicLogEnsureTool : EventLogToolBase, ITool {
    private sealed record ClassicLogEnsureRequest(
        string? MachineName,
        string TargetMachineName,
        string LogName,
        string SourceName,
        int? RequestedMaximumKilobytes,
        string? RequestedOverflowAction,
        int? RequestedRetentionDays,
        bool Apply);

    private sealed record ClassicLogSnapshot(
        string LogName,
        string MachineName,
        string SourceName,
        bool LogExists,
        bool SourceExists,
        string? SourceRegisteredLogName,
        string? LogDisplayName,
        int? MaximumKilobytes,
        string? OverflowAction,
        int? MinimumRetentionDays);

    private sealed record ClassicLogRollbackGuidance(
        string LogName,
        string? MachineName,
        string SourceName,
        bool RemoveLogIfCreated,
        bool RemoveSourceIfCreated,
        string? PreviousSourceRegisteredLogName,
        int? PreviousMaximumKilobytes,
        string? PreviousOverflowAction,
        int? PreviousMinimumRetentionDays);

    private sealed record ClassicLogEnsureResult(
        string LogName,
        string MachineName,
        string SourceName,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        string Message,
        int? RequestedMaximumKilobytes,
        string? RequestedOverflowAction,
        int? RequestedRetentionDays,
        IReadOnlyList<string> RequestedChanges,
        IReadOnlyList<string> Warnings,
        ClassicLogSnapshot Before,
        ClassicLogSnapshot After,
        ClassicLogRollbackGuidance RollbackGuidance);

    private sealed record ClassicLogMutationPlan(
        bool CanApply,
        bool Changed,
        ClassicLogSnapshot PredictedAfter,
        IReadOnlyList<string> RequestedChanges,
        string PreviewMessage,
        string ApplyMessage,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_classic_log_ensure",
        "Governed classic Windows Event Log provisioning for ensuring a custom log and source exist with optional size and overflow configuration. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for the local machine.")),
                ("log_name", ToolSchema.String("Exact classic Windows Event Log name to ensure.")),
                ("source_name", ToolSchema.String("Optional event source/provider name to ensure for the log. Defaults to log_name.")),
                ("maximum_kilobytes", ToolSchema.Integer("Optional maximum log size in kilobytes to apply when ensuring the log.")),
                ("overflow_action", ToolSchema.String("Optional overflow policy to apply when ensuring the log.").Enum(ClassicLogOverflowActions.Names)),
                ("retention_days", ToolSchema.Integer("Optional retention days. Supported only when overflow_action=overwrite_older.")),
                ("apply", ToolSchema.Boolean("When true, performs the classic log ensure write. Otherwise returns a dry-run preview.")))
            .Required("log_name")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogClassicLogEnsureTool"/> class.
    /// </summary>
    public EventLogClassicLogEnsureTool(EventLogToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ClassicLogEnsureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var machineName = reader.OptionalString("machine_name");
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<ClassicLogEnsureRequest>.Failure(logNameError);
            }

            var sourceName = ToolArgs.NormalizeOptional(reader.OptionalString("source_name")) ?? logName.Trim();
            var requestedMaximumKilobytesRaw = reader.OptionalInt64("maximum_kilobytes");
            int? requestedMaximumKilobytes = null;
            if (requestedMaximumKilobytesRaw.HasValue) {
                if (requestedMaximumKilobytesRaw.Value < 64) {
                    return ToolRequestBindingResult<ClassicLogEnsureRequest>.Failure(
                        "maximum_kilobytes must be at least 64.");
                }

                requestedMaximumKilobytes = (int)Math.Min(requestedMaximumKilobytesRaw.Value, 4_194_240);
            }

            if (!ClassicLogOverflowActions.TryNormalize(
                    reader.OptionalString("overflow_action"),
                    out var requestedOverflowAction,
                    out var overflowActionError)) {
                return ToolRequestBindingResult<ClassicLogEnsureRequest>.Failure(
                    overflowActionError ?? "overflow_action must be one of: overwrite_as_needed, overwrite_older, do_not_overwrite.");
            }

            var requestedRetentionDaysRaw = reader.OptionalInt64("retention_days");
            int? requestedRetentionDays = null;
            if (requestedRetentionDaysRaw.HasValue) {
                requestedRetentionDays = (int)requestedRetentionDaysRaw.Value;
            }
            if (requestedRetentionDays.HasValue) {
                if (requestedRetentionDays.Value < 1 || requestedRetentionDays.Value > 3650) {
                    return ToolRequestBindingResult<ClassicLogEnsureRequest>.Failure(
                        "retention_days must be between 1 and 3650.");
                }

                if (!string.Equals(requestedOverflowAction, "overwrite_older", StringComparison.OrdinalIgnoreCase)) {
                    return ToolRequestBindingResult<ClassicLogEnsureRequest>.Failure(
                        "retention_days is supported only when overflow_action=overwrite_older.");
                }
            }

            return ToolRequestBindingResult<ClassicLogEnsureRequest>.Success(new ClassicLogEnsureRequest(
                MachineName: machineName,
                TargetMachineName: string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim(),
                LogName: logName.Trim(),
                SourceName: sourceName,
                RequestedMaximumKilobytes: requestedMaximumKilobytes,
                RequestedOverflowAction: requestedOverflowAction,
                RequestedRetentionDays: requestedRetentionDays,
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<ClassicLogEnsureRequest> context,
        CancellationToken cancellationToken) {
        return Task.Run(() => ExecuteSync(context, cancellationToken), cancellationToken);
    }

    private string ExecuteSync(
        ToolPipelineContext<ClassicLogEnsureRequest> context,
        CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) {
            return ToolResultV2.Error(
                errorCode: "platform_not_supported",
                error: "eventlog_classic_log_ensure is supported only on Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var beforeAttempt = TryGetClassicLogSnapshot(request);
        if (beforeAttempt.ErrorResponse is not null) {
            return beforeAttempt.ErrorResponse;
        }

        if (beforeAttempt.Snapshot is null) {
            return ToolResultV2.Error(
                errorCode: "query_failed",
                error: $"Classic Event Log state for '{request.LogName}' on '{request.TargetMachineName}' could not be inspected before the write.",
                hints: new[] {
                    "Use eventlog_connectivity_probe to confirm host reachability and Event Log permissions.",
                    "Use eventlog_channels_list or eventlog_providers_list to inspect current Event Log visibility before retrying."
                });
        }

        var before = beforeAttempt.Snapshot;
        var plan = BuildMutationPlan(request, before);
        if (request.Apply && !plan.CanApply) {
            return ToolResultV2.Error(
                errorCode: "precondition_failed",
                error: plan.ApplyMessage,
                hints: plan.Warnings);
        }

        var after = plan.PredictedAfter;
        var postChangeVerified = true;
        var writeExecuted = false;
        var warnings = new List<string>(plan.Warnings);

        if (request.Apply && plan.Changed) {
            var retentionDays = request.RequestedRetentionDays
                ?? before.MinimumRetentionDays
                ?? 7;

            var writeSucceeded = SearchEvents.CreateLog(
                logName: request.LogName,
                sourceName: request.SourceName,
                machineName: request.MachineName,
                maximumKilobytes: request.RequestedMaximumKilobytes ?? 0,
                overflowActionName: request.RequestedOverflowAction ?? before.OverflowAction,
                retentionDays: retentionDays,
                sourceLogName: request.LogName);
            if (!writeSucceeded) {
                return ToolResultV2.Error(
                    errorCode: "action_failed",
                    error: $"The requested classic Event Log ensure write failed for '{request.LogName}' on '{request.TargetMachineName}'.",
                    hints: BuildApplyFailureHints());
            }

            writeExecuted = true;

            var afterAttempt = TryGetClassicLogSnapshot(request);
            if (afterAttempt.ErrorResponse is not null || afterAttempt.Snapshot is null) {
                postChangeVerified = false;
                warnings.Add("Post-change verification could not re-read the classic Event Log state after the write.");
            } else {
                after = afterAttempt.Snapshot;
            }
        }

        var result = new ClassicLogEnsureResult(
            LogName: request.LogName,
            MachineName: request.TargetMachineName,
            SourceName: request.SourceName,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            RequestedMaximumKilobytes: request.RequestedMaximumKilobytes,
            RequestedOverflowAction: request.RequestedOverflowAction,
            RequestedRetentionDays: request.RequestedRetentionDays,
            RequestedChanges: plan.RequestedChanges,
            Warnings: warnings,
            Before: before,
            After: after,
            RollbackGuidance: BuildRollbackGuidance(before, request));

        return CreateSuccessResponse(result);
    }

    private static (ClassicLogSnapshot? Snapshot, string? ErrorResponse) TryGetClassicLogSnapshot(ClassicLogEnsureRequest request) {
        try {
            var state = SearchEvents.GetClassicLogState(request.LogName, request.SourceName, request.MachineName);

            return (new ClassicLogSnapshot(
                LogName: request.LogName,
                MachineName: request.TargetMachineName,
                SourceName: request.SourceName,
                LogExists: state.LogExists,
                SourceExists: state.SourceExists,
                SourceRegisteredLogName: ToolArgs.NormalizeOptional(state.SourceRegisteredLogName),
                LogDisplayName: ToolArgs.NormalizeOptional(state.LogDisplayName),
                MaximumKilobytes: state.MaximumKilobytes,
                OverflowAction: ToolArgs.NormalizeOptional(state.OverflowActionName),
                MinimumRetentionDays: state.MinimumRetentionDays), null);
        } catch (Exception ex) {
            return (null, ErrorFromException(
                ex,
                defaultMessage: "Classic Event Log state query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed"));
        }
    }

    private static ClassicLogMutationPlan BuildMutationPlan(
        ClassicLogEnsureRequest request,
        ClassicLogSnapshot before) {
        var requestedChanges = new List<string>(4);
        var warnings = new List<string>();

        if (before.SourceExists
            && !string.IsNullOrWhiteSpace(before.SourceRegisteredLogName)
            && !string.Equals(before.SourceRegisteredLogName, request.LogName, StringComparison.OrdinalIgnoreCase)) {
            var conflictMessage =
                $"Source '{request.SourceName}' is already registered to log '{before.SourceRegisteredLogName}', so it cannot be safely ensured on '{request.LogName}' without an explicit remapping step.";
            return new ClassicLogMutationPlan(
                CanApply: false,
                Changed: true,
                PredictedAfter: before,
                RequestedChanges: new[] { "source_mapping_conflict" },
                PreviewMessage: conflictMessage,
                ApplyMessage: conflictMessage,
                Warnings: new[] { conflictMessage });
        }

        var after = before;
        if (!before.LogExists) {
            requestedChanges.Add("create_log");
            after = after with { LogExists = true };
            warnings.Add("Rollback for a newly created classic Event Log requires deleting the custom log if the change must be fully reverted.");
        }

        if (!before.SourceExists) {
            requestedChanges.Add("create_source");
            after = after with {
                SourceExists = true,
                SourceRegisteredLogName = request.LogName
            };
            warnings.Add("Rollback for a newly created event source requires deleting that source if the change must be fully reverted.");
        }

        if (request.RequestedMaximumKilobytes.HasValue
            && request.RequestedMaximumKilobytes != before.MaximumKilobytes) {
            requestedChanges.Add("maximum_kilobytes");
            after = after with { MaximumKilobytes = request.RequestedMaximumKilobytes.Value };
        }

        if (!string.IsNullOrWhiteSpace(request.RequestedOverflowAction)) {
            if (!string.Equals(before.OverflowAction, request.RequestedOverflowAction, StringComparison.OrdinalIgnoreCase)) {
                requestedChanges.Add("overflow_action");
                after = after with { OverflowAction = request.RequestedOverflowAction };
            }
        }

        if (request.RequestedRetentionDays.HasValue
            && request.RequestedRetentionDays != before.MinimumRetentionDays) {
            requestedChanges.Add("retention_days");
            after = after with { MinimumRetentionDays = request.RequestedRetentionDays.Value };
        }

        var changed = requestedChanges.Count > 0;
        if (!changed) {
            const string noChangeMessage = "Requested classic Event Log state already matches the current state. No change required.";
            return new ClassicLogMutationPlan(
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                RequestedChanges: requestedChanges,
                PreviewMessage: noChangeMessage,
                ApplyMessage: "Classic Event Log already matched the requested state. No change applied.",
                Warnings: warnings.ToArray());
        }

        return new ClassicLogMutationPlan(
            CanApply: true,
            Changed: true,
            PredictedAfter: after,
            RequestedChanges: requestedChanges,
            PreviewMessage: $"Preview only: classic Event Log '{request.LogName}' would be ensured with source '{request.SourceName}'.",
            ApplyMessage: $"Classic Event Log '{request.LogName}' ensured with source '{request.SourceName}'.",
            Warnings: warnings.ToArray());
    }

    private static ClassicLogRollbackGuidance BuildRollbackGuidance(
        ClassicLogSnapshot before,
        ClassicLogEnsureRequest request) {
        return new ClassicLogRollbackGuidance(
            LogName: request.LogName,
            MachineName: request.MachineName,
            SourceName: request.SourceName,
            RemoveLogIfCreated: !before.LogExists,
            RemoveSourceIfCreated: !before.SourceExists,
            PreviousSourceRegisteredLogName: before.SourceRegisteredLogName,
            PreviousMaximumKilobytes: before.MaximumKilobytes,
            PreviousOverflowAction: before.OverflowAction,
            PreviousMinimumRetentionDays: before.MinimumRetentionDays);
    }

    private static IReadOnlyList<string> BuildApplyFailureHints() {
        return new[] {
            "Call eventlog_connectivity_probe to confirm host reachability and Event Log administrative permissions before retrying.",
            "Call eventlog_channels_list to verify the target log name is visible on the host.",
            "Call eventlog_providers_list to verify the requested source/provider name does not conflict with an existing registration.",
            "Custom classic log creation on remote hosts can require administrative rights and compatible Event Log / registry access."
        };
    }

    private static string CreateSuccessResponse(ClassicLogEnsureResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Machine", result.MachineName),
            ("Log", result.LogName),
            ("Source", result.SourceName),
            ("Log exists", FormatBoolean(result.Before.LogExists) + " -> " + FormatBoolean(result.After.LogExists)),
            ("Source exists", FormatBoolean(result.Before.SourceExists) + " -> " + FormatBoolean(result.After.SourceExists)),
            ("Overflow", FormatNullableString(result.Before.OverflowAction) + " -> " + FormatNullableString(result.After.OverflowAction)),
            ("Maximum KB", FormatNullableInt32(result.Before.MaximumKilobytes) + " -> " + FormatNullableInt32(result.After.MaximumKilobytes)),
            ("Message", result.Message)
        };
        if (result.RequestedRetentionDays.HasValue || result.Before.MinimumRetentionDays.HasValue || result.After.MinimumRetentionDays.HasValue) {
            facts.Insert(8, ("Retention days", FormatNullableInt32(result.Before.MinimumRetentionDays) + " -> " + FormatNullableInt32(result.After.MinimumRetentionDays)));
        }
        if (result.RequestedChanges.Count > 0) {
            facts.Add(("Requested changes", string.Join(", ", result.RequestedChanges)));
        }
        if (result.Warnings.Count > 0) {
            facts.Add(("Warnings", string.Join(" | ", result.Warnings)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("log_name", result.LogName)
            .Add("machine_name", result.MachineName)
            .Add("source_name", result.SourceName)
            .Add("write_candidate", true)
            .Add("changed", result.Changed)
            .Add("can_apply", result.CanApply)
            .Add("post_change_verified", result.PostChangeVerified)
            .Add("write_executed", result.WriteExecuted)
            .Add("requested_change_count", result.RequestedChanges.Count);
        if (result.RequestedMaximumKilobytes.HasValue) {
            meta.Add("requested_maximum_kilobytes", result.RequestedMaximumKilobytes.Value);
        }
        if (!string.IsNullOrWhiteSpace(result.RequestedOverflowAction)) {
            meta.Add("requested_overflow_action", result.RequestedOverflowAction);
        }
        if (result.RequestedRetentionDays.HasValue) {
            meta.Add("requested_retention_days", result.RequestedRetentionDays.Value);
        }
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "eventlog_classic_log_ensure",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "Classic Event Log ensure");
    }

    private static string FormatBoolean(bool value) {
        return value ? "true" : "false";
    }

    private static string FormatNullableInt32(int? value) {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string FormatNullableString(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
