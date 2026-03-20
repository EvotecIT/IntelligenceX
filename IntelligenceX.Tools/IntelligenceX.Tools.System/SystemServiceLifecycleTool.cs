using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Services;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Performs governed Windows service lifecycle actions (dry-run by default).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemServiceLifecycleTool : SystemToolBase, ITool {
    private sealed record ServiceLifecycleRequest(
        string? ComputerName,
        string TargetComputerName,
        string ServiceName,
        ServiceLifecycleOperation Operation,
        ServiceStartupType? RequestedStartupType,
        int TimeoutMs,
        bool Apply);

    private sealed record ServiceStateSnapshot(
        string ComputerName,
        string ServiceName,
        string DisplayName,
        string Status,
        string StartupType,
        bool CanPauseAndContinue,
        bool CanStop,
        string? ServiceAccount,
        string? BinaryPath,
        string? Description);

    private sealed record ServiceLifecycleResult(
        string Operation,
        string ComputerName,
        string ServiceName,
        string DisplayName,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        string Message,
        string? RequestedStartupType,
        int? TimeoutMs,
        IReadOnlyList<string> Warnings,
        ServiceStateSnapshot Before,
        ServiceStateSnapshot After);

    private sealed record ServiceMutationPlan(
        ServiceControlAction Action,
        bool CanApply,
        bool Changed,
        ServiceStateSnapshot PredictedAfter,
        string PreviewMessage,
        string ApplyMessage,
        IReadOnlyList<string> Warnings);

    private static readonly IReadOnlyDictionary<string, ServiceLifecycleOperation> OperationByName =
        new Dictionary<string, ServiceLifecycleOperation>(StringComparer.OrdinalIgnoreCase) {
            ["start"] = ServiceLifecycleOperation.Start,
            ["stop"] = ServiceLifecycleOperation.Stop,
            ["restart"] = ServiceLifecycleOperation.Restart,
            ["set_startup_type"] = ServiceLifecycleOperation.SetStartupType
        };

    private static readonly IReadOnlyDictionary<string, ServiceStartupType> StartupTypeByName =
        new Dictionary<string, ServiceStartupType>(StringComparer.OrdinalIgnoreCase) {
            ["automatic"] = ServiceStartupType.Automatic,
            ["manual"] = ServiceStartupType.Manual,
            ["disabled"] = ServiceStartupType.Disabled
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_service_lifecycle",
        "Governed Windows service lifecycle actions for start/stop/restart/startup type changes. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("service_name", ToolSchema.String("Exact Windows service name to manage.")),
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("start", "stop", "restart", "set_startup_type")),
                ("startup_type", ToolSchema.String("Required only for operation=set_startup_type.").Enum("automatic", "manual", "disabled")),
                ("timeout_ms", ToolSchema.Integer("Optional wait timeout in milliseconds for start/stop/restart operations.")),
                ("apply", ToolSchema.Boolean("When true, performs the lifecycle write. Otherwise returns a dry-run preview.")))
            .Required("service_name", "operation")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemServiceLifecycleTool"/> class.
    /// </summary>
    public SystemServiceLifecycleTool(SystemToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ServiceLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            if (!reader.TryReadRequiredString("service_name", out var serviceName, out var serviceNameError)) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(serviceNameError);
            }

            if (!reader.TryReadRequiredString("operation", out var operationRaw, out var operationError)) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(operationError);
            }

            if (!OperationByName.TryGetValue(operationRaw.Trim(), out var operation)) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(
                    "operation must be one of: start, stop, restart, set_startup_type.");
            }

            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("startup_type"),
                    StartupTypeByName,
                    "startup_type",
                    out ServiceStartupType? requestedStartupType,
                    out var startupTypeError)) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(
                    startupTypeError ?? "startup_type must be one of: automatic, manual, disabled.");
            }

            if (operation == ServiceLifecycleOperation.SetStartupType && requestedStartupType is null) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(
                    "startup_type is required when operation=set_startup_type.");
            }

            if (operation != ServiceLifecycleOperation.SetStartupType && requestedStartupType is not null) {
                return ToolRequestBindingResult<ServiceLifecycleRequest>.Failure(
                    "startup_type is supported only when operation=set_startup_type.");
            }

            return ToolRequestBindingResult<ServiceLifecycleRequest>.Success(new ServiceLifecycleRequest(
                ComputerName: computerName,
                TargetComputerName: ResolveTargetComputerName(computerName),
                ServiceName: serviceName,
                Operation: operation,
                RequestedStartupType: requestedStartupType,
                TimeoutMs: reader.CappedInt32("timeout_ms", defaultValue: 30_000, minInclusive: 200, maxInclusive: 120_000),
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private async Task<string> ExecuteAsync(
        ToolPipelineContext<ServiceLifecycleRequest> context,
        CancellationToken cancellationToken) {
        var windowsSupportError = ValidateWindowsSupport("system_service_lifecycle");
        if (!string.IsNullOrWhiteSpace(windowsSupportError)) {
            return windowsSupportError;
        }

        var request = context.Request;
        var beforeAttempt = await TryGetServiceStateAsync(request, cancellationToken).ConfigureAwait(false);
        if (beforeAttempt.ErrorResponse is not null) {
            return beforeAttempt.ErrorResponse;
        }

        if (beforeAttempt.Service is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Service '{request.ServiceName}' was not found on '{request.TargetComputerName}'.",
                hints: new[] {
                    "Call system_service_list to confirm the exact service_name on the target host."
                });
        }

        var before = beforeAttempt.Service;
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
            var timeout = request.Operation == ServiceLifecycleOperation.SetStartupType
                ? (TimeSpan?)null
                : TimeSpan.FromMilliseconds(request.TimeoutMs);
            if (!TryApplyMutation(request, plan.Action, timeout)) {
                return ToolResultV2.Error(
                    errorCode: "action_failed",
                    error: $"The requested {NormalizeOperation(request.Operation)} action failed for service '{before.ServiceName}' on '{request.TargetComputerName}'.",
                    hints: new[] {
                        "Verify service permissions and host reachability with system_connectivity_probe.",
                        "Inspect current service state with system_service_list before retrying."
                    });
            }

            writeExecuted = true;

            var afterAttempt = await TryGetServiceStateAsync(request, cancellationToken).ConfigureAwait(false);
            if (afterAttempt.ErrorResponse is not null || afterAttempt.Service is null) {
                postChangeVerified = false;
                if (!string.IsNullOrWhiteSpace(afterAttempt.ErrorResponse)) {
                    warnings.Add("Post-change verification could not re-query the service state.");
                } else {
                    warnings.Add("Post-change verification could not resolve the service after the action completed.");
                }
            } else {
                after = afterAttempt.Service;
            }
        }

        var result = new ServiceLifecycleResult(
            Operation: NormalizeOperation(request.Operation),
            ComputerName: before.ComputerName,
            ServiceName: before.ServiceName,
            DisplayName: before.DisplayName,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            RequestedStartupType: request.RequestedStartupType is null ? null : NormalizeStartupType(request.RequestedStartupType.Value),
            TimeoutMs: request.Operation == ServiceLifecycleOperation.SetStartupType ? null : request.TimeoutMs,
            Warnings: warnings,
            Before: before,
            After: after);

        return CreateSuccessResponse(result);
    }

    private static async Task<(ServiceStateSnapshot? Service, string? ErrorResponse)> TryGetServiceStateAsync(
        ServiceLifecycleRequest request,
        CancellationToken cancellationToken) {
        var attempt = await ServiceListQueryExecutor.TryExecuteAsync(
            request: new ServiceListQueryRequest {
                ComputerName = request.ComputerName,
                ServiceNames = new[] { request.ServiceName },
                Engine = ServiceEngine.Auto,
                Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs),
                MaxResults = 5,
                SortBy = ServiceListQuerySort.ServiceNameAsc
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!attempt.Success) {
            return (null, ErrorFromFailure(
                attempt.Failure,
                static failure => failure.Code,
                static failure => failure.Message,
                defaultMessage: "Service query failed."));
        }

        var service = attempt.Result?.Services.Count > 0
            ? attempt.Result.Services[0]
            : null;
        return service is null
            ? (null, null)
            : (CreateSnapshot(service, request.TargetComputerName), null);
    }

    private static ServiceMutationPlan BuildMutationPlan(ServiceLifecycleRequest request, ServiceStateSnapshot before) {
        return request.Operation switch {
            ServiceLifecycleOperation.Start => BuildStartPlan(before),
            ServiceLifecycleOperation.Stop => BuildStopPlan(before),
            ServiceLifecycleOperation.Restart => BuildRestartPlan(before),
            ServiceLifecycleOperation.SetStartupType => BuildStartupTypePlan(before, request.RequestedStartupType ?? ServiceStartupType.Unknown),
            _ => new ServiceMutationPlan(
                Action: ServiceControlAction.None,
                CanApply: false,
                Changed: false,
                PredictedAfter: before,
                PreviewMessage: "Unsupported service lifecycle operation.",
                ApplyMessage: "Unsupported service lifecycle operation.",
                Warnings: new[] { "Unsupported service lifecycle operation." })
        };
    }

    private static ServiceMutationPlan BuildStartPlan(ServiceStateSnapshot before) {
        if (string.Equals(before.StartupType, "disabled", StringComparison.OrdinalIgnoreCase)) {
            const string message = "Service is disabled. Change startup_type before attempting start.";
            return new ServiceMutationPlan(ServiceControlAction.None, false, true, before, message, message, new[] { message });
        }

        if (string.Equals(before.Status, "running", StringComparison.OrdinalIgnoreCase)) {
            return new ServiceMutationPlan(
                Action: ServiceControlAction.None,
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                PreviewMessage: "Service is already running. No change required.",
                ApplyMessage: "Service was already running. No change applied.",
                Warnings: Array.Empty<string>());
        }

        return new ServiceMutationPlan(
            Action: ServiceControlAction.Start,
            CanApply: true,
            Changed: true,
            PredictedAfter: before with { Status = "running" },
            PreviewMessage: "Preview only: service would be started.",
            ApplyMessage: "Service started.",
            Warnings: Array.Empty<string>());
    }

    private static ServiceMutationPlan BuildStopPlan(ServiceStateSnapshot before) {
        if (string.Equals(before.Status, "stopped", StringComparison.OrdinalIgnoreCase)) {
            return new ServiceMutationPlan(
                Action: ServiceControlAction.None,
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                PreviewMessage: "Service is already stopped. No change required.",
                ApplyMessage: "Service was already stopped. No change applied.",
                Warnings: Array.Empty<string>());
        }

        if (!before.CanStop) {
            const string message = "Service cannot be stopped because the current controller reports can_stop=false.";
            return new ServiceMutationPlan(ServiceControlAction.None, false, true, before, message, message, new[] { message });
        }

        return new ServiceMutationPlan(
            Action: ServiceControlAction.Stop,
            CanApply: true,
            Changed: true,
            PredictedAfter: before with { Status = "stopped" },
            PreviewMessage: "Preview only: service would be stopped.",
            ApplyMessage: "Service stopped.",
            Warnings: Array.Empty<string>());
    }

    private static ServiceMutationPlan BuildRestartPlan(ServiceStateSnapshot before) {
        if (string.Equals(before.StartupType, "disabled", StringComparison.OrdinalIgnoreCase)) {
            const string message = "Service is disabled. Change startup_type before attempting restart.";
            return new ServiceMutationPlan(ServiceControlAction.None, false, true, before, message, message, new[] { message });
        }

        if (string.Equals(before.Status, "running", StringComparison.OrdinalIgnoreCase) && !before.CanStop) {
            const string message = "Service cannot be restarted because the current controller reports can_stop=false.";
            return new ServiceMutationPlan(ServiceControlAction.None, false, true, before, message, message, new[] { message });
        }

        if (string.Equals(before.Status, "stopped", StringComparison.OrdinalIgnoreCase)) {
            return new ServiceMutationPlan(
                Action: ServiceControlAction.Start,
                CanApply: true,
                Changed: true,
                PredictedAfter: before with { Status = "running" },
                PreviewMessage: "Preview only: service is stopped and would be started.",
                ApplyMessage: "Service was stopped and has been started.",
                Warnings: Array.Empty<string>());
        }

        return new ServiceMutationPlan(
            Action: ServiceControlAction.Restart,
            CanApply: true,
            Changed: true,
            PredictedAfter: before with { Status = "running" },
            PreviewMessage: "Preview only: service would be restarted.",
            ApplyMessage: "Service restarted.",
            Warnings: Array.Empty<string>());
    }

    private static ServiceMutationPlan BuildStartupTypePlan(ServiceStateSnapshot before, ServiceStartupType requestedStartupType) {
        var requested = NormalizeStartupType(requestedStartupType);
        if (string.Equals(before.StartupType, requested, StringComparison.OrdinalIgnoreCase)) {
            return new ServiceMutationPlan(
                Action: ServiceControlAction.None,
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                PreviewMessage: $"Startup type is already {requested}. No change required.",
                ApplyMessage: $"Startup type was already {requested}. No change applied.",
                Warnings: Array.Empty<string>());
        }

        return new ServiceMutationPlan(
            Action: ServiceControlAction.SetStartupType,
            CanApply: true,
            Changed: true,
            PredictedAfter: before with { StartupType = requested },
            PreviewMessage: $"Preview only: startup type would change from {before.StartupType} to {requested}.",
            ApplyMessage: $"Startup type changed from {before.StartupType} to {requested}.",
            Warnings: Array.Empty<string>());
    }

    private static bool TryApplyMutation(
        ServiceLifecycleRequest request,
        ServiceControlAction action,
        TimeSpan? timeout) {
        var targetComputer = request.ComputerName ?? string.Empty;
        return action switch {
            ServiceControlAction.Start => ServicesControl.Start(targetComputer, request.ServiceName, timeout),
            ServiceControlAction.Stop => ServicesControl.Stop(targetComputer, request.ServiceName, timeout),
            ServiceControlAction.Restart => ServicesControl.Restart(targetComputer, request.ServiceName, timeout),
            ServiceControlAction.SetStartupType when request.RequestedStartupType is not null =>
                ServicesControl.SetStartupType(targetComputer, request.ServiceName, request.RequestedStartupType.Value),
            ServiceControlAction.None => true,
            _ => false
        };
    }

    private static ServiceStateSnapshot CreateSnapshot(ServiceInfo service, string fallbackComputerName) {
        return new ServiceStateSnapshot(
            ComputerName: string.IsNullOrWhiteSpace(service.ComputerName) ? fallbackComputerName : service.ComputerName,
            ServiceName: service.ServiceName,
            DisplayName: service.DisplayName,
            Status: NormalizeStatus(service.Status),
            StartupType: NormalizeStartupType(service.StartupType),
            CanPauseAndContinue: service.CanPauseAndContinue,
            CanStop: service.CanStop,
            ServiceAccount: ToolArgs.NormalizeOptional(service.ServiceAccount),
            BinaryPath: ToolArgs.NormalizeOptional(service.BinaryPath),
            Description: ToolArgs.NormalizeOptional(service.Description));
    }

    private static string CreateSuccessResponse(ServiceLifecycleResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Operation", result.Operation),
            ("Computer", result.ComputerName),
            ("Service", result.ServiceName),
            ("Status", result.Before.Status + " -> " + result.After.Status),
            ("Startup", result.Before.StartupType + " -> " + result.After.StartupType),
            ("Message", result.Message)
        };
        if (!string.IsNullOrWhiteSpace(result.DisplayName)) {
            facts.Insert(4, ("Display name", result.DisplayName));
        }
        if (!string.IsNullOrWhiteSpace(result.RequestedStartupType)) {
            facts.Add(("Requested startup", result.RequestedStartupType!));
        }
        if (result.TimeoutMs.HasValue) {
            facts.Add(("Timeout ms", result.TimeoutMs.Value.ToString(CultureInfo.InvariantCulture)));
        }
        if (!result.PostChangeVerified) {
            facts.Add(("Verification", "Post-change state could not be re-queried."));
        }
        if (result.Warnings.Count > 0) {
            facts.Add(("Warnings", string.Join(" | ", result.Warnings)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("operation", result.Operation)
            .Add("computer_name", result.ComputerName)
            .Add("service_name", result.ServiceName)
            .Add("write_candidate", true)
            .Add("changed", result.Changed)
            .Add("can_apply", result.CanApply)
            .Add("post_change_verified", result.PostChangeVerified)
            .Add("write_executed", result.WriteExecuted);
        if (!string.IsNullOrWhiteSpace(result.RequestedStartupType)) {
            meta.Add("requested_startup_type", result.RequestedStartupType);
        }
        if (result.TimeoutMs.HasValue) {
            meta.Add("timeout_ms", result.TimeoutMs.Value);
        }
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "system_service_" + result.Operation,
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "System service lifecycle");
    }

    private static string NormalizeOperation(ServiceLifecycleOperation operation) {
        return operation switch {
            ServiceLifecycleOperation.Start => "start",
            ServiceLifecycleOperation.Stop => "stop",
            ServiceLifecycleOperation.Restart => "restart",
            ServiceLifecycleOperation.SetStartupType => "set_startup_type",
            _ => "unknown"
        };
    }

    private static string NormalizeStartupType(ServiceStartupType startupType) {
        return startupType switch {
            ServiceStartupType.Automatic => "automatic",
            ServiceStartupType.Disabled => "disabled",
            ServiceStartupType.Manual => "manual",
            _ => "unknown"
        };
    }

    private static string NormalizeStatus(ServiceControllerStatus status) {
        return status switch {
            ServiceControllerStatus.Running => "running",
            ServiceControllerStatus.Stopped => "stopped",
            ServiceControllerStatus.Paused => "paused",
            ServiceControllerStatus.StartPending => "start_pending",
            ServiceControllerStatus.StopPending => "stop_pending",
            ServiceControllerStatus.ContinuePending => "continue_pending",
            ServiceControllerStatus.PausePending => "pause_pending",
            _ => status.ToString().ToLowerInvariant()
        };
    }

    private enum ServiceLifecycleOperation {
        Start,
        Stop,
        Restart,
        SetStartupType
    }

    private enum ServiceControlAction {
        None,
        Start,
        Stop,
        Restart,
        SetStartupType
    }
}
