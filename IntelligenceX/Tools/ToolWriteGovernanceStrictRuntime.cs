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
    public string ExecutionIdArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.ExecutionId;

    /// <summary>
    /// Actor id argument name.
    /// </summary>
    public string ActorIdArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.ActorId;

    /// <summary>
    /// Change reason argument name.
    /// </summary>
    public string ChangeReasonArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.ChangeReason;

    /// <summary>
    /// Rollback plan id argument name.
    /// </summary>
    public string RollbackPlanIdArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.RollbackPlanId;

    /// <summary>
    /// Optional rollback provider id argument name.
    /// </summary>
    public string RollbackProviderIdArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.RollbackProviderId;

    /// <summary>
    /// Optional audit correlation id argument name.
    /// </summary>
    public string AuditCorrelationIdArgumentName { get; set; } = ToolWriteGovernanceArgumentNames.AuditCorrelationId;

    /// <inheritdoc />
    public ToolWriteGovernanceResult Authorize(ToolWriteGovernanceRequest request) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        string executionId = ResolveArgumentValue(request, request.ExecutionId, ExecutionIdArgumentName);
        string actorId = ResolveArgumentValue(request, request.ActorId, ActorIdArgumentName);
        string changeReason = ResolveArgumentValue(request, request.ChangeReason, ChangeReasonArgumentName);
        string rollbackPlanId = ResolveArgumentValue(request, request.RollbackPlanId, RollbackPlanIdArgumentName);
        string rollbackProviderId = ResolveArgumentValue(request, request.RollbackProviderId, RollbackProviderIdArgumentName);
        string auditCorrelationId = ResolveArgumentValue(request, request.AuditCorrelationId, AuditCorrelationIdArgumentName);
        if (string.IsNullOrWhiteSpace(auditCorrelationId)) {
            auditCorrelationId = executionId;
        }

        List<string> missing = new();

        if (string.IsNullOrWhiteSpace(ImmutableAuditProviderId)) {
            missing.Add("immutable_audit_provider_id");
        }
        if (RequireRollbackPlanId && string.IsNullOrWhiteSpace(RollbackProviderId)) {
            missing.Add("rollback_provider_id");
        }

        if (RequireExecutionId && string.IsNullOrWhiteSpace(executionId)) {
            missing.Add(ExecutionIdArgumentName);
        }
        if (RequireActorId && string.IsNullOrWhiteSpace(actorId)) {
            missing.Add(ActorIdArgumentName);
        }
        if (RequireChangeReason && string.IsNullOrWhiteSpace(changeReason)) {
            missing.Add(ChangeReasonArgumentName);
        }
        if (RequireRollbackPlanId && string.IsNullOrWhiteSpace(rollbackPlanId)) {
            missing.Add(RollbackPlanIdArgumentName);
        }

        if (missing.Count > 0) {
            return new ToolWriteGovernanceResult {
                IsAuthorized = false,
                ErrorCode = ToolWriteGovernanceErrorCodes.WriteGovernanceRequirementsNotMet,
                Error = "Write governance requirements are not met.",
                MissingRequirements = missing.ToArray(),
                Hints = BuildHints(missing),
                IsTransient = false,
                ExecutionId = executionId,
                AuditCorrelationId = auditCorrelationId,
                ImmutableAuditProviderId = ImmutableAuditProviderId,
                RollbackProviderId = string.IsNullOrWhiteSpace(rollbackProviderId)
                    ? RollbackProviderId
                    : rollbackProviderId
            };
        }

        return new ToolWriteGovernanceResult {
            IsAuthorized = true,
            ExecutionId = executionId,
            AuditCorrelationId = auditCorrelationId,
            ImmutableAuditProviderId = ImmutableAuditProviderId,
            RollbackProviderId = string.IsNullOrWhiteSpace(rollbackProviderId)
                ? RollbackProviderId
                : rollbackProviderId
        };
    }

    /// <summary>
    /// Resolves metadata with precedence for explicit request fields over raw argument values.
    /// </summary>
    private static string ResolveArgumentValue(
        ToolWriteGovernanceRequest request,
        string preferredValue,
        string argumentName) {
        if (!string.IsNullOrWhiteSpace(preferredValue)) {
            return preferredValue;
        }

        if (string.IsNullOrWhiteSpace(argumentName)) {
            return string.Empty;
        }

        string? value = request.Arguments?.GetString(argumentName);
        return value?.Trim() ?? string.Empty;
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
