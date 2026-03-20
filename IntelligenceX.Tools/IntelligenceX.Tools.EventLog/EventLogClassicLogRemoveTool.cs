using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Removes a classic Windows Event Log source and optional custom log with governed preview/apply behavior.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogClassicLogRemoveTool : EventLogToolBase, ITool {
    private static readonly HashSet<string> ReservedClassicLogNames = new(StringComparer.OrdinalIgnoreCase) {
        "Application",
        "Security",
        "System",
        "Setup",
        "ForwardedEvents"
    };

    private sealed record ClassicLogRemoveRequest(
        string? MachineName,
        string TargetMachineName,
        string LogName,
        string SourceName,
        bool RemoveSource,
        bool RemoveLog,
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
        int? MinimumRetentionDays,
        bool IsReservedLogName);

    private sealed record ClassicLogRollbackArguments(
        string LogName,
        string? MachineName,
        string SourceName,
        int? MaximumKilobytes,
        string? OverflowAction,
        int? RetentionDays,
        bool Apply);

    private sealed record ClassicLogApplyDetails(
        bool Success,
        bool PartialSuccess,
        IReadOnlyList<string> AppliedChanges,
        IReadOnlyList<string> FailedChanges,
        IReadOnlyList<string> Errors);

    private sealed record ClassicLogRemoveResult(
        string LogName,
        string MachineName,
        string SourceName,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        bool PartialSuccess,
        bool RequestedRemoveSource,
        bool RequestedRemoveLog,
        string Message,
        IReadOnlyList<string> RequestedChanges,
        IReadOnlyList<string> Warnings,
        ClassicLogSnapshot Before,
        ClassicLogSnapshot After,
        ClassicLogRollbackArguments RollbackArguments,
        ClassicLogApplyDetails? ApplyDetails);

    private sealed record ClassicLogMutationPlan(
        bool CanApply,
        bool Changed,
        ClassicLogSnapshot PredictedAfter,
        IReadOnlyList<string> RequestedChanges,
        string PreviewMessage,
        string ApplyMessage,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_classic_log_remove",
        "Governed classic Windows Event Log cleanup for removing a custom log source and optional custom log. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for the local machine.")),
                ("log_name", ToolSchema.String("Exact classic Windows Event Log name to clean up.")),
                ("source_name", ToolSchema.String("Exact event source/provider name to remove from the classic log.")),
                ("remove_source", ToolSchema.Boolean("When true, removes the named classic Event Log source registration.")),
                ("remove_log", ToolSchema.Boolean("When true, removes the named classic custom Event Log after source cleanup. Built-in standard logs are blocked.")),
                ("apply", ToolSchema.Boolean("When true, performs the classic log cleanup write. Otherwise returns a dry-run preview.")))
            .Required("log_name", "source_name")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogClassicLogRemoveTool"/> class.
    /// </summary>
    public EventLogClassicLogRemoveTool(EventLogToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ClassicLogRemoveRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var machineName = reader.OptionalString("machine_name");
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<ClassicLogRemoveRequest>.Failure(logNameError);
            }

            if (!reader.TryReadRequiredString("source_name", out var sourceName, out var sourceNameError)) {
                return ToolRequestBindingResult<ClassicLogRemoveRequest>.Failure(sourceNameError);
            }

            var removeSource = reader.Boolean("remove_source", defaultValue: false);
            var removeLog = reader.Boolean("remove_log", defaultValue: false);
            if (!removeSource && !removeLog) {
                return ToolRequestBindingResult<ClassicLogRemoveRequest>.Failure(
                    "Set remove_source=true and/or remove_log=true to request a classic Event Log cleanup action.");
            }

            if (removeLog && !removeSource) {
                return ToolRequestBindingResult<ClassicLogRemoveRequest>.Failure(
                    "remove_log requires remove_source=true so cleanup remains explicit and rollback-ready.");
            }

            return ToolRequestBindingResult<ClassicLogRemoveRequest>.Success(new ClassicLogRemoveRequest(
                MachineName: machineName,
                TargetMachineName: string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim(),
                LogName: logName.Trim(),
                SourceName: sourceName.Trim(),
                RemoveSource: removeSource,
                RemoveLog: removeLog,
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<ClassicLogRemoveRequest> context,
        CancellationToken cancellationToken) {
        return Task.Run(() => ExecuteSync(context, cancellationToken), cancellationToken);
    }

    private string ExecuteSync(
        ToolPipelineContext<ClassicLogRemoveRequest> context,
        CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) {
            return ToolResultV2.Error(
                errorCode: "platform_not_supported",
                error: "eventlog_classic_log_remove is supported only on Windows.");
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
                error: $"Classic Event Log state for '{request.LogName}' on '{request.TargetMachineName}' could not be inspected before cleanup.",
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
        var partialSuccess = false;
        var warnings = new List<string>(plan.Warnings);
        ClassicLogApplyDetails? applyDetails = null;

        if (request.Apply && plan.Changed) {
            try {
                var appliedChanges = new List<string>(2);
                var failedChanges = new List<string>(2);
                var errors = new List<string>(2);

                if (request.RemoveSource && before.SourceExists) {
                    var removeSourceSucceeded = SearchEvents.RemoveSource(
                        request.SourceName,
                        request.MachineName,
                        request.LogName);
                    if (removeSourceSucceeded) {
                        appliedChanges.Add("remove_source");
                    } else {
                        failedChanges.Add("remove_source");
                        errors.Add($"Failed to remove classic Event Log source '{request.SourceName}'.");
                    }
                }

                if (request.RemoveLog && before.LogExists) {
                    var removeLogSucceeded = SearchEvents.RemoveLog(
                        request.LogName,
                        request.MachineName);
                    if (removeLogSucceeded) {
                        appliedChanges.Add("remove_log");
                    } else {
                        failedChanges.Add("remove_log");
                        errors.Add($"Failed to remove classic Event Log '{request.LogName}'.");
                    }
                }

                writeExecuted = appliedChanges.Count > 0;
                partialSuccess = failedChanges.Count > 0 && appliedChanges.Count > 0;
                warnings.AddRange(errors);
                applyDetails = new ClassicLogApplyDetails(
                    Success: failedChanges.Count == 0 && appliedChanges.Count > 0,
                    PartialSuccess: partialSuccess,
                    AppliedChanges: appliedChanges.ToArray(),
                    FailedChanges: failedChanges.ToArray(),
                    Errors: errors.ToArray());

                if (failedChanges.Count > 0 && appliedChanges.Count == 0) {
                    return ToolResultV2.Error(
                        errorCode: "action_failed",
                        error: $"The requested classic Event Log cleanup failed for '{request.LogName}' on '{request.TargetMachineName}'.",
                        hints: BuildApplyFailureHints());
                }
            } catch (Exception ex) {
                return ErrorFromException(
                    ex,
                    defaultMessage: "Classic Event Log cleanup failed.",
                    fallbackErrorCode: "action_failed",
                    invalidOperationErrorCode: "precondition_failed");
            }

            var afterAttempt = TryGetClassicLogSnapshot(request);
            if (afterAttempt.ErrorResponse is not null || afterAttempt.Snapshot is null) {
                postChangeVerified = false;
                warnings.Add("Post-change verification could not re-read the classic Event Log state after cleanup.");
            } else {
                after = afterAttempt.Snapshot;
            }
        }

        var result = new ClassicLogRemoveResult(
            LogName: request.LogName,
            MachineName: request.TargetMachineName,
            SourceName: request.SourceName,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            PartialSuccess: partialSuccess,
            RequestedRemoveSource: request.RemoveSource,
            RequestedRemoveLog: request.RemoveLog,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            RequestedChanges: plan.RequestedChanges,
            Warnings: warnings,
            Before: before,
            After: after,
            RollbackArguments: BuildRollbackArguments(before, request),
            ApplyDetails: applyDetails);

        return CreateSuccessResponse(result);
    }

    private static (ClassicLogSnapshot? Snapshot, string? ErrorResponse) TryGetClassicLogSnapshot(ClassicLogRemoveRequest request) {
        try {
            var logExists = SearchEvents.LogExists(request.LogName, request.MachineName);
            var sourceExists = string.IsNullOrWhiteSpace(request.MachineName)
                ? System.Diagnostics.EventLog.SourceExists(request.SourceName)
                : System.Diagnostics.EventLog.SourceExists(request.SourceName, request.MachineName);

            string? sourceRegisteredLogName = null;
            if (sourceExists) {
                sourceRegisteredLogName = ToolArgs.NormalizeOptional(
                    System.Diagnostics.EventLog.LogNameFromSourceName(request.SourceName, request.MachineName ?? "."));
            }

            string? logDisplayName = null;
            int? maximumKilobytes = null;
            string? overflowAction = null;
            int? minimumRetentionDays = null;

            if (logExists) {
                using var log = string.IsNullOrWhiteSpace(request.MachineName)
                    ? new System.Diagnostics.EventLog(request.LogName)
                    : new System.Diagnostics.EventLog(request.LogName, request.MachineName);
                logDisplayName = ToolArgs.NormalizeOptional(log.LogDisplayName);
                maximumKilobytes = log.MaximumKilobytes > int.MaxValue
                    ? int.MaxValue
                    : (int)log.MaximumKilobytes;
                overflowAction = NormalizeOverflowAction(log.OverflowAction);
                minimumRetentionDays = log.MinimumRetentionDays;
            }

            return (new ClassicLogSnapshot(
                LogName: request.LogName,
                MachineName: request.TargetMachineName,
                SourceName: request.SourceName,
                LogExists: logExists,
                SourceExists: sourceExists,
                SourceRegisteredLogName: sourceRegisteredLogName,
                LogDisplayName: logDisplayName,
                MaximumKilobytes: maximumKilobytes,
                OverflowAction: overflowAction,
                MinimumRetentionDays: minimumRetentionDays,
                IsReservedLogName: IsReservedClassicLogName(request.LogName)), null);
        } catch (Exception ex) {
            return (null, ErrorFromException(
                ex,
                defaultMessage: "Classic Event Log state query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed"));
        }
    }

    private static ClassicLogMutationPlan BuildMutationPlan(
        ClassicLogRemoveRequest request,
        ClassicLogSnapshot before) {
        var requestedChanges = new List<string>(2);
        var warnings = new List<string>();
        var canApply = true;

        if (request.RemoveSource
            && before.SourceExists
            && !string.IsNullOrWhiteSpace(before.SourceRegisteredLogName)
            && !string.Equals(before.SourceRegisteredLogName, request.LogName, StringComparison.OrdinalIgnoreCase)) {
            var conflictMessage =
                $"Source '{request.SourceName}' is registered to log '{before.SourceRegisteredLogName}', so it cannot be safely removed through '{request.LogName}'.";
            return new ClassicLogMutationPlan(
                CanApply: false,
                Changed: false,
                PredictedAfter: before,
                RequestedChanges: new[] { "source_mapping_conflict" },
                PreviewMessage: conflictMessage,
                ApplyMessage: conflictMessage,
                Warnings: new[] { conflictMessage });
        }

        var after = before;
        if (request.RemoveSource) {
            if (before.SourceExists) {
                requestedChanges.Add("remove_source");
                after = after with {
                    SourceExists = false,
                    SourceRegisteredLogName = null
                };
                warnings.Add("Rollback for source removal requires re-running eventlog_classic_log_ensure with the same log_name and source_name.");
            } else {
                warnings.Add($"Source '{request.SourceName}' is already absent on '{request.TargetMachineName}'.");
            }
        }

        if (request.RemoveLog) {
            if (before.IsReservedLogName) {
                canApply = false;
                warnings.Add($"Log '{request.LogName}' is a reserved built-in standard log and cannot be removed with governed cleanup.");
            } else if (before.LogExists) {
                requestedChanges.Add("remove_log");
                after = after with {
                    LogExists = false,
                    LogDisplayName = null,
                    MaximumKilobytes = null,
                    OverflowAction = null,
                    MinimumRetentionDays = null
                };
                warnings.Add("Rollback for custom log removal requires re-running eventlog_classic_log_ensure with the previous log and source settings.");
            } else {
                warnings.Add($"Log '{request.LogName}' is already absent on '{request.TargetMachineName}'.");
            }
        }

        var changed = requestedChanges.Count > 0;
        if (!changed) {
            const string noChangeMessage = "Requested classic Event Log cleanup already matches the current state. No change required.";
            return new ClassicLogMutationPlan(
                CanApply: canApply,
                Changed: false,
                PredictedAfter: before,
                RequestedChanges: requestedChanges,
                PreviewMessage: noChangeMessage,
                ApplyMessage: "Classic Event Log cleanup already matched the requested state. No change applied.",
                Warnings: warnings.ToArray());
        }

        var actionTarget = request.RemoveLog
            ? $"classic Event Log '{request.LogName}' and source '{request.SourceName}'"
            : $"classic Event Log source '{request.SourceName}'";
        var previewMessage = canApply
            ? $"Preview only: {actionTarget} would be removed."
            : $"Preview only: {actionTarget} cannot be fully removed until the reported preconditions are satisfied.";
        var applyMessage = canApply
            ? $"Removed {actionTarget}."
            : $"Classic Event Log cleanup for '{request.LogName}' cannot be applied until the reported preconditions are satisfied.";

        return new ClassicLogMutationPlan(
            CanApply: canApply,
            Changed: true,
            PredictedAfter: after,
            RequestedChanges: requestedChanges,
            PreviewMessage: previewMessage,
            ApplyMessage: applyMessage,
            Warnings: warnings.ToArray());
    }

    private static ClassicLogRollbackArguments BuildRollbackArguments(
        ClassicLogSnapshot before,
        ClassicLogRemoveRequest request) {
        return new ClassicLogRollbackArguments(
            LogName: request.LogName,
            MachineName: request.MachineName,
            SourceName: request.SourceName,
            MaximumKilobytes: before.MaximumKilobytes,
            OverflowAction: before.OverflowAction,
            RetentionDays: before.MinimumRetentionDays,
            Apply: true);
    }

    private static IReadOnlyList<string> BuildApplyFailureHints() {
        return new[] {
            "Call eventlog_connectivity_probe to confirm host reachability and Event Log administrative permissions before retrying.",
            "Call eventlog_channels_list to verify whether the target custom log is still visible on the host.",
            "Call eventlog_providers_list to verify whether the target source/provider registration is still visible.",
            "Use eventlog_classic_log_ensure with the rollback arguments from the preview if the cleanup only partially applied."
        };
    }

    private static string CreateSuccessResponse(ClassicLogRemoveResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Machine", result.MachineName),
            ("Log", result.LogName),
            ("Source", result.SourceName),
            ("Log exists", FormatBoolean(result.Before.LogExists) + " -> " + FormatBoolean(result.After.LogExists)),
            ("Source exists", FormatBoolean(result.Before.SourceExists) + " -> " + FormatBoolean(result.After.SourceExists)),
            ("Message", result.Message)
        };
        if (result.RequestedChanges.Count > 0) {
            facts.Add(("Requested changes", string.Join(", ", result.RequestedChanges)));
        }
        if (result.PartialSuccess) {
            facts.Add(("Partial success", "true"));
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
            .Add("partial_success", result.PartialSuccess)
            .Add("requested_change_count", result.RequestedChanges.Count)
            .Add("requested_remove_source", result.RequestedRemoveSource)
            .Add("requested_remove_log", result.RequestedRemoveLog);
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "eventlog_classic_log_remove",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "Classic Event Log cleanup");
    }

    private static bool IsReservedClassicLogName(string logName) {
        return ReservedClassicLogNames.Contains(logName ?? string.Empty);
    }

    private static string NormalizeOverflowAction(OverflowAction overflowAction) {
        return overflowAction switch {
            OverflowAction.OverwriteAsNeeded => "overwrite_as_needed",
            OverflowAction.OverwriteOlder => "overwrite_older",
            OverflowAction.DoNotOverwrite => "do_not_overwrite",
            _ => overflowAction.ToString().ToLowerInvariant()
        };
    }

    private static string FormatBoolean(bool value) {
        return value ? "true" : "false";
    }
}
