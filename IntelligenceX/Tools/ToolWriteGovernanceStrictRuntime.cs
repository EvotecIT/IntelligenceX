using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Strict default governance runtime for write tool authorization.
/// </summary>
public sealed class ToolWriteGovernanceStrictRuntime : IToolWriteGovernanceRuntime {
    /// <summary>
    /// Immutable audit provider id used by runtime policy.
    /// </summary>
    public string ImmutableAuditProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Rollback provider id used by runtime policy.
    /// </summary>
    public string RollbackProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Requires execution id argument.
    /// </summary>
    public bool RequireExecutionId { get; set; } = true;

    /// <summary>
    /// Requires actor id argument.
    /// </summary>
    public bool RequireActorId { get; set; } = true;

    /// <summary>
    /// Requires change reason argument.
    /// </summary>
    public bool RequireChangeReason { get; set; } = true;

    /// <summary>
    /// Requires rollback plan argument.
    /// </summary>
    public bool RequireRollbackPlanId { get; set; } = true;

    /// <summary>
    /// Execution id argument name.
    /// </summary>
    public string ExecutionIdArgumentName { get; set; } = "write_execution_id";

    /// <summary>
    /// Actor id argument name.
    /// </summary>
    public string ActorIdArgumentName { get; set; } = "write_actor_id";

    /// <summary>
    /// Change reason argument name.
    /// </summary>
    public string ChangeReasonArgumentName { get; set; } = "write_change_reason";

    /// <summary>
    /// Rollback plan id argument name.
    /// </summary>
    public string RollbackPlanIdArgumentName { get; set; } = "write_rollback_plan_id";

    /// <inheritdoc />
    public ToolWriteGovernanceResult Authorize(ToolWriteGovernanceRequest request) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        List<string> missing = new();

        if (string.IsNullOrWhiteSpace(ImmutableAuditProviderId)) {
            missing.Add("immutable_audit_provider_id");
        }
        if (RequireRollbackPlanId && string.IsNullOrWhiteSpace(RollbackProviderId)) {
            missing.Add("rollback_provider_id");
        }

        if (RequireExecutionId && IsMissingArgument(request, ExecutionIdArgumentName)) {
            missing.Add(ExecutionIdArgumentName);
        }
        if (RequireActorId && IsMissingArgument(request, ActorIdArgumentName)) {
            missing.Add(ActorIdArgumentName);
        }
        if (RequireChangeReason && IsMissingArgument(request, ChangeReasonArgumentName)) {
            missing.Add(ChangeReasonArgumentName);
        }
        if (RequireRollbackPlanId && IsMissingArgument(request, RollbackPlanIdArgumentName)) {
            missing.Add(RollbackPlanIdArgumentName);
        }

        if (missing.Count > 0) {
            return new ToolWriteGovernanceResult {
                IsAuthorized = false,
                ErrorCode = "write_governance_requirements_not_met",
                Error = "Write governance requirements are not met.",
                Hints = BuildHints(missing),
                IsTransient = false
            };
        }

        return new ToolWriteGovernanceResult {
            IsAuthorized = true
        };
    }

    private static bool IsMissingArgument(ToolWriteGovernanceRequest request, string argumentName) {
        if (string.IsNullOrWhiteSpace(argumentName)) {
            return false;
        }

        string? value = request.Arguments?.GetString(argumentName);
        return string.IsNullOrWhiteSpace(value);
    }

    private static IReadOnlyList<string> BuildHints(IReadOnlyList<string> missing) {
        if (missing.Count == 0) {
            return Array.Empty<string>();
        }

        string joined = string.Join(", ", missing);
        return new[] {
            $"Provide required write governance metadata: {joined}.",
            "Use immutable audit + rollback metadata for every write-intent call."
        };
    }
}
