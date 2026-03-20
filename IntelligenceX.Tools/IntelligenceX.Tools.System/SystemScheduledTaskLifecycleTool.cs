using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.ScheduledTasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Performs governed scheduled-task control actions (dry-run by default).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemScheduledTaskLifecycleTool : SystemToolBase, ITool {
    private sealed record ScheduledTaskLifecycleRequest(
        string? ComputerName,
        string TargetComputerName,
        string TaskPath,
        ScheduledTaskLifecycleOperation Operation,
        bool Apply);

    private sealed record ScheduledTaskStateSnapshot(
        string ComputerName,
        string Name,
        string Path,
        string Command,
        string? Arguments,
        bool Enabled,
        DateTime? LastRunTimeUtc,
        DateTime? NextRunTimeUtc,
        string? RunAsUser,
        string? RunAsLogonType,
        bool? RunAsIsSystem,
        bool? RunAsIsGmsa);

    private sealed record ScheduledTaskLifecycleResult(
        string Operation,
        string ComputerName,
        string TaskPath,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        bool ExpectedTaskPresentAfter,
        string Message,
        IReadOnlyList<string> Warnings,
        ScheduledTaskStateSnapshot Before,
        ScheduledTaskStateSnapshot? After);

    private sealed record ScheduledTaskMutationPlan(
        ScheduledTaskControlAction Action,
        bool CanApply,
        bool Changed,
        bool ExpectedTaskPresentAfter,
        ScheduledTaskStateSnapshot? PredictedAfter,
        string PreviewMessage,
        string ApplyMessage,
        IReadOnlyList<string> Warnings);

    private static readonly IReadOnlyDictionary<string, ScheduledTaskLifecycleOperation> OperationByName =
        new Dictionary<string, ScheduledTaskLifecycleOperation>(StringComparer.OrdinalIgnoreCase) {
            ["enable"] = ScheduledTaskLifecycleOperation.Enable,
            ["disable"] = ScheduledTaskLifecycleOperation.Disable,
            ["run_now"] = ScheduledTaskLifecycleOperation.RunNow,
            ["delete"] = ScheduledTaskLifecycleOperation.Delete
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_scheduled_task_lifecycle",
        "Governed scheduled-task actions for enable/disable/run_now/delete. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("task_path", ToolSchema.String("Exact scheduled-task path, for example \\\\Microsoft\\\\Windows\\\\Defrag\\\\ScheduledDefrag.")),
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("enable", "disable", "run_now", "delete")),
                ("apply", ToolSchema.Boolean("When true, performs the scheduled-task write. Otherwise returns a dry-run preview.")))
            .Required("task_path", "operation")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemScheduledTaskLifecycleTool"/> class.
    /// </summary>
    public SystemScheduledTaskLifecycleTool(SystemToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ScheduledTaskLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            if (!reader.TryReadRequiredString("task_path", out var taskPath, out var taskPathError)) {
                return ToolRequestBindingResult<ScheduledTaskLifecycleRequest>.Failure(taskPathError);
            }

            if (!reader.TryReadRequiredString("operation", out var operationRaw, out var operationError)) {
                return ToolRequestBindingResult<ScheduledTaskLifecycleRequest>.Failure(operationError);
            }

            if (!OperationByName.TryGetValue(operationRaw.Trim(), out var operation)) {
                return ToolRequestBindingResult<ScheduledTaskLifecycleRequest>.Failure(
                    "operation must be one of: enable, disable, run_now, delete.");
            }

            var normalizedTaskPath = NormalizeTaskPath(taskPath);
            if (string.Equals(normalizedTaskPath, "\\", StringComparison.Ordinal)) {
                return ToolRequestBindingResult<ScheduledTaskLifecycleRequest>.Failure(
                    "task_path must identify an exact scheduled task path, not the scheduler root.");
            }

            return ToolRequestBindingResult<ScheduledTaskLifecycleRequest>.Success(new ScheduledTaskLifecycleRequest(
                ComputerName: computerName,
                TargetComputerName: ResolveTargetComputerName(computerName),
                TaskPath: normalizedTaskPath,
                Operation: operation,
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<ScheduledTaskLifecycleRequest> context,
        CancellationToken cancellationToken) {
        return Task.Run(() => ExecuteSync(context, cancellationToken), cancellationToken);
    }

    private string ExecuteSync(
        ToolPipelineContext<ScheduledTaskLifecycleRequest> context,
        CancellationToken cancellationToken) {
        var windowsSupportError = ValidateWindowsSupport("system_scheduled_task_lifecycle");
        if (!string.IsNullOrWhiteSpace(windowsSupportError)) {
            return windowsSupportError;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var beforeAttempt = TryGetScheduledTaskState(request);
        if (beforeAttempt.ErrorResponse is not null) {
            return beforeAttempt.ErrorResponse;
        }

        if (beforeAttempt.Task is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Scheduled task '{request.TaskPath}' was not found on '{request.TargetComputerName}', or the scheduler could not be queried.",
                hints: new[] {
                    "Call system_scheduled_tasks_list to confirm the exact task_path on the target host.",
                    "Use system_connectivity_probe when remote scheduler access is uncertain."
                });
        }

        var before = beforeAttempt.Task;
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
            if (!TryApplyMutation(request, plan.Action)) {
                return ToolResultV2.Error(
                    errorCode: "action_failed",
                    error: $"The requested {NormalizeOperation(request.Operation)} action failed for scheduled task '{request.TaskPath}' on '{request.TargetComputerName}'.",
                    hints: new[] {
                        "Verify scheduled-task permissions and host reachability with system_connectivity_probe.",
                        "Inspect the current task state with system_scheduled_tasks_list before retrying."
                    });
            }

            writeExecuted = true;

            var afterAttempt = TryGetScheduledTaskState(request);
            if (afterAttempt.ErrorResponse is not null) {
                postChangeVerified = false;
                warnings.Add("Post-change verification could not re-query the scheduled task state.");
            } else if (plan.ExpectedTaskPresentAfter) {
                if (afterAttempt.Task is null) {
                    postChangeVerified = false;
                    warnings.Add("Post-change verification could not resolve the scheduled task after the action completed.");
                } else {
                    after = afterAttempt.Task;
                }
            } else {
                after = afterAttempt.Task;
            }
        }

        var result = new ScheduledTaskLifecycleResult(
            Operation: NormalizeOperation(request.Operation),
            ComputerName: before.ComputerName,
            TaskPath: before.Path,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            ExpectedTaskPresentAfter: plan.ExpectedTaskPresentAfter,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            Warnings: warnings,
            Before: before,
            After: after);

        return CreateSuccessResponse(result);
    }

    private static (ScheduledTaskStateSnapshot? Task, string? ErrorResponse) TryGetScheduledTaskState(ScheduledTaskLifecycleRequest request) {
        try {
            var task = TaskSchedulerQuery.GetOne(request.TaskPath, request.ComputerName);
            return task is null
                ? (null, null)
                : (CreateSnapshot(task, request.TargetComputerName), null);
        } catch (Exception ex) {
            return (null, ErrorFromException(ex, defaultMessage: "Scheduled task query failed."));
        }
    }

    private static ScheduledTaskMutationPlan BuildMutationPlan(
        ScheduledTaskLifecycleRequest request,
        ScheduledTaskStateSnapshot before) {
        return request.Operation switch {
            ScheduledTaskLifecycleOperation.Enable => BuildEnablePlan(before),
            ScheduledTaskLifecycleOperation.Disable => BuildDisablePlan(before),
            ScheduledTaskLifecycleOperation.RunNow => BuildRunNowPlan(before),
            ScheduledTaskLifecycleOperation.Delete => BuildDeletePlan(before),
            _ => new ScheduledTaskMutationPlan(
                Action: ScheduledTaskControlAction.None,
                CanApply: false,
                Changed: false,
                ExpectedTaskPresentAfter: true,
                PredictedAfter: before,
                PreviewMessage: "Unsupported scheduled-task lifecycle operation.",
                ApplyMessage: "Unsupported scheduled-task lifecycle operation.",
                Warnings: new[] { "Unsupported scheduled-task lifecycle operation." })
        };
    }

    private static ScheduledTaskMutationPlan BuildEnablePlan(ScheduledTaskStateSnapshot before) {
        if (before.Enabled) {
            return new ScheduledTaskMutationPlan(
                Action: ScheduledTaskControlAction.None,
                CanApply: true,
                Changed: false,
                ExpectedTaskPresentAfter: true,
                PredictedAfter: before,
                PreviewMessage: "Scheduled task is already enabled. No change required.",
                ApplyMessage: "Scheduled task was already enabled. No change applied.",
                Warnings: Array.Empty<string>());
        }

        return new ScheduledTaskMutationPlan(
            Action: ScheduledTaskControlAction.Enable,
            CanApply: true,
            Changed: true,
            ExpectedTaskPresentAfter: true,
            PredictedAfter: before with { Enabled = true },
            PreviewMessage: "Preview only: scheduled task would be enabled.",
            ApplyMessage: "Scheduled task enabled.",
            Warnings: Array.Empty<string>());
    }

    private static ScheduledTaskMutationPlan BuildDisablePlan(ScheduledTaskStateSnapshot before) {
        if (!before.Enabled) {
            return new ScheduledTaskMutationPlan(
                Action: ScheduledTaskControlAction.None,
                CanApply: true,
                Changed: false,
                ExpectedTaskPresentAfter: true,
                PredictedAfter: before,
                PreviewMessage: "Scheduled task is already disabled. No change required.",
                ApplyMessage: "Scheduled task was already disabled. No change applied.",
                Warnings: Array.Empty<string>());
        }

        return new ScheduledTaskMutationPlan(
            Action: ScheduledTaskControlAction.Disable,
            CanApply: true,
            Changed: true,
            ExpectedTaskPresentAfter: true,
            PredictedAfter: before with { Enabled = false },
            PreviewMessage: "Preview only: scheduled task would be disabled.",
            ApplyMessage: "Scheduled task disabled.",
            Warnings: Array.Empty<string>());
    }

    private static ScheduledTaskMutationPlan BuildRunNowPlan(ScheduledTaskStateSnapshot before) {
        if (!before.Enabled) {
            const string message = "Scheduled task is disabled. Enable it before attempting run_now.";
            return new ScheduledTaskMutationPlan(
                Action: ScheduledTaskControlAction.None,
                CanApply: false,
                Changed: true,
                ExpectedTaskPresentAfter: true,
                PredictedAfter: before,
                PreviewMessage: message,
                ApplyMessage: message,
                Warnings: new[] { message });
        }

        return new ScheduledTaskMutationPlan(
            Action: ScheduledTaskControlAction.RunNow,
            CanApply: true,
            Changed: true,
            ExpectedTaskPresentAfter: true,
            PredictedAfter: before,
            PreviewMessage: "Preview only: scheduled task would be run immediately.",
            ApplyMessage: "Scheduled task triggered to run immediately.",
            Warnings: Array.Empty<string>());
    }

    private static ScheduledTaskMutationPlan BuildDeletePlan(ScheduledTaskStateSnapshot before) {
        return new ScheduledTaskMutationPlan(
            Action: ScheduledTaskControlAction.Delete,
            CanApply: true,
            Changed: true,
            ExpectedTaskPresentAfter: false,
            PredictedAfter: null,
            PreviewMessage: "Preview only: scheduled task would be deleted.",
            ApplyMessage: "Scheduled task deleted.",
            Warnings: Array.Empty<string>());
    }

    private static bool TryApplyMutation(
        ScheduledTaskLifecycleRequest request,
        ScheduledTaskControlAction action) {
        return action switch {
            ScheduledTaskControlAction.Enable => TaskSchedulerWriter.Enable(request.TaskPath, request.ComputerName),
            ScheduledTaskControlAction.Disable => TaskSchedulerWriter.Disable(request.TaskPath, request.ComputerName),
            ScheduledTaskControlAction.RunNow => TaskSchedulerWriter.RunNow(request.TaskPath, request.ComputerName),
            ScheduledTaskControlAction.Delete => TaskSchedulerWriter.Delete(request.TaskPath, request.ComputerName),
            ScheduledTaskControlAction.None => true,
            _ => false
        };
    }

    private static ScheduledTaskStateSnapshot CreateSnapshot(ScheduledTaskInfo task, string fallbackComputerName) {
        return new ScheduledTaskStateSnapshot(
            ComputerName: fallbackComputerName,
            Name: task.Name,
            Path: task.Path,
            Command: task.Command,
            Arguments: ToolArgs.NormalizeOptional(task.Arguments),
            Enabled: task.Enabled,
            LastRunTimeUtc: task.LastRunTime?.ToUniversalTime(),
            NextRunTimeUtc: task.NextRunTime?.ToUniversalTime(),
            RunAsUser: ToolArgs.NormalizeOptional(task.RunAsUser),
            RunAsLogonType: ToolArgs.NormalizeOptional(task.RunAsLogonType),
            RunAsIsSystem: task.RunAsIsSystem,
            RunAsIsGmsa: task.RunAsIsGmsa);
    }

    private static string CreateSuccessResponse(ScheduledTaskLifecycleResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Operation", result.Operation),
            ("Computer", result.ComputerName),
            ("Task path", result.TaskPath),
            ("Enabled", result.Before.Enabled + " -> " + (result.After?.Enabled.ToString() ?? "deleted")),
            ("Message", result.Message)
        };
        if (!string.IsNullOrWhiteSpace(result.Before.Name)) {
            facts.Insert(4, ("Task name", result.Before.Name));
        }
        if (!string.IsNullOrWhiteSpace(result.Before.Command)) {
            facts.Add(("Command", result.Before.Command));
        }
        if (result.Warnings.Count > 0) {
            facts.Add(("Warnings", string.Join(" | ", result.Warnings)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("operation", result.Operation)
            .Add("computer_name", result.ComputerName)
            .Add("task_path", result.TaskPath)
            .Add("write_candidate", true)
            .Add("changed", result.Changed)
            .Add("can_apply", result.CanApply)
            .Add("post_change_verified", result.PostChangeVerified)
            .Add("write_executed", result.WriteExecuted)
            .Add("expected_task_present_after", result.ExpectedTaskPresentAfter);
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "system_scheduled_task_" + result.Operation,
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "System scheduled task lifecycle");
    }

    private static string NormalizeTaskPath(string taskPath) {
        var normalized = (taskPath ?? string.Empty).Trim().Replace('/', '\\');
        if (normalized.Length == 0) {
            return "\\";
        }

        return normalized.StartsWith("\\", StringComparison.Ordinal)
            ? normalized
            : "\\" + normalized;
    }

    private static string NormalizeOperation(ScheduledTaskLifecycleOperation operation) {
        return operation switch {
            ScheduledTaskLifecycleOperation.Enable => "enable",
            ScheduledTaskLifecycleOperation.Disable => "disable",
            ScheduledTaskLifecycleOperation.RunNow => "run_now",
            ScheduledTaskLifecycleOperation.Delete => "delete",
            _ => "unknown"
        };
    }

    private enum ScheduledTaskLifecycleOperation {
        Enable,
        Disable,
        RunNow,
        Delete
    }

    private enum ScheduledTaskControlAction {
        None,
        Enable,
        Disable,
        RunNow,
        Delete
    }
}
