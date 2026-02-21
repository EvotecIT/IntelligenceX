using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolWriteGovernanceStrictRuntimeTests {
    [Fact]
    public void Authorize_MissingProviderAndMetadata_ReturnsMissingRequirements() {
        var runtime = new ToolWriteGovernanceStrictRuntime();
        var request = new ToolWriteGovernanceRequest {
            Arguments = new JsonObject()
        };

        ToolWriteGovernanceResult result = runtime.Authorize(request);

        Assert.False(result.IsAuthorized);
        Assert.Equal(ToolWriteGovernanceErrorCodes.WriteGovernanceRequirementsNotMet, result.ErrorCode);
        Assert.Contains("immutable_audit_provider_id", result.MissingRequirements);
        Assert.Contains("rollback_provider_id", result.MissingRequirements);
        Assert.Contains(ToolWriteGovernanceArgumentNames.OperationId, result.MissingRequirements);
        Assert.Contains(ToolWriteGovernanceArgumentNames.ExecutionId, result.MissingRequirements);
        Assert.Contains(ToolWriteGovernanceArgumentNames.ActorId, result.MissingRequirements);
        Assert.Contains(ToolWriteGovernanceArgumentNames.ChangeReason, result.MissingRequirements);
        Assert.Contains(ToolWriteGovernanceArgumentNames.RollbackPlanId, result.MissingRequirements);
    }

    [Fact]
    public void Authorize_RequestFieldsTakePrecedenceOverArguments() {
        var runtime = new ToolWriteGovernanceStrictRuntime {
            ImmutableAuditProviderId = "audit",
            RollbackProviderId = "rollback"
        };
        var request = new ToolWriteGovernanceRequest {
            OperationId = "request-operation",
            ExecutionId = "request-exec",
            ActorId = "request-actor",
            ChangeReason = "request-reason",
            RollbackPlanId = "request-plan",
            RollbackProviderId = "request-rollback-provider",
            AuditCorrelationId = "request-audit",
            Arguments = new JsonObject()
                .Add(ToolWriteGovernanceArgumentNames.OperationId, "arg-operation")
                .Add(ToolWriteGovernanceArgumentNames.ExecutionId, "arg-exec")
                .Add(ToolWriteGovernanceArgumentNames.ActorId, "arg-actor")
                .Add(ToolWriteGovernanceArgumentNames.ChangeReason, "arg-reason")
                .Add(ToolWriteGovernanceArgumentNames.RollbackPlanId, "arg-plan")
                .Add(ToolWriteGovernanceArgumentNames.RollbackProviderId, "arg-rollback-provider")
                .Add(ToolWriteGovernanceArgumentNames.AuditCorrelationId, "arg-audit")
        };

        ToolWriteGovernanceResult result = runtime.Authorize(request);

        Assert.True(result.IsAuthorized);
        Assert.Equal("request-operation", result.OperationId);
        Assert.Equal("request-exec", result.ExecutionId);
        Assert.Equal("request-audit", result.AuditCorrelationId);
        Assert.Equal("request-rollback-provider", result.RollbackProviderId);
    }

    [Fact]
    public void Authorize_UsesArgumentsWhenRequestFieldsAreEmpty() {
        var runtime = new ToolWriteGovernanceStrictRuntime {
            ImmutableAuditProviderId = "audit",
            RollbackProviderId = "runtime-rollback-provider"
        };
        var request = new ToolWriteGovernanceRequest {
            Arguments = new JsonObject()
                .Add(ToolWriteGovernanceArgumentNames.OperationId, "arg-operation")
                .Add(ToolWriteGovernanceArgumentNames.ExecutionId, "arg-exec")
                .Add(ToolWriteGovernanceArgumentNames.ActorId, "arg-actor")
                .Add(ToolWriteGovernanceArgumentNames.ChangeReason, "arg-reason")
                .Add(ToolWriteGovernanceArgumentNames.RollbackPlanId, "arg-plan")
                .Add(ToolWriteGovernanceArgumentNames.RollbackProviderId, "arg-rollback-provider")
                .Add(ToolWriteGovernanceArgumentNames.AuditCorrelationId, "arg-audit")
        };

        ToolWriteGovernanceResult result = runtime.Authorize(request);

        Assert.True(result.IsAuthorized);
        Assert.Equal("arg-operation", result.OperationId);
        Assert.Equal("arg-exec", result.ExecutionId);
        Assert.Equal("arg-audit", result.AuditCorrelationId);
        Assert.Equal("arg-rollback-provider", result.RollbackProviderId);
    }
}
