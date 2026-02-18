using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolWriteGovernanceRegistryTests {
    [Fact]
    public async Task InvokeAsync_WriteIntentWithoutRuntime_ReturnsGovernanceRuntimeRequired() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var registry = new ToolRegistry();
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject().Add("send", true).Add("allow_write", true),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("write_governance_runtime_required", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WriteIntentWithRequiredAuditSinkAndMissingSink_ReturnsAuditSinkRequired() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var registry = new ToolRegistry {
            RequireWriteAuditSinkForWriteOperations = true,
            WriteGovernanceRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = "audit",
                RollbackProviderId = "rollback"
            }
        };
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject()
                .Add("send", true)
                .Add("allow_write", true)
                .Add("write_execution_id", "exec-1")
                .Add("write_actor_id", "actor-1")
                .Add("write_change_reason", "ticket-1")
                .Add("write_rollback_plan_id", "rollback-1"),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("write_audit_sink_required", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WriteIntentWithStrictRuntimeAndMissingMetadata_ReturnsDenied() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var registry = new ToolRegistry {
            WriteGovernanceRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = "audit",
                RollbackProviderId = "rollback"
            }
        };
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject().Add("send", true).Add("allow_write", true),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("write_governance_requirements_not_met", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WriteIntentWithStrictRuntimeAndMetadata_InvokesTool() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var sink = new InMemoryAuditSink();
        var registry = new ToolRegistry {
            WriteAuditSink = sink,
            WriteGovernanceRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = "audit",
                RollbackProviderId = "rollback"
            }
        };
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject()
                .Add("send", true)
                .Add("allow_write", true)
                .Add("write_execution_id", "exec-1")
                .Add("write_actor_id", "actor-1")
                .Add("write_change_reason", "ticket-1")
                .Add("write_rollback_plan_id", "rollback-1"),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invoked", doc.RootElement.GetProperty("status").GetString());

        ToolWriteAuditRecord record = Assert.Single(sink.Records);
        Assert.True(record.IsAuthorized);
        Assert.Equal("stub_write", record.ToolName);
        Assert.Equal("exec-1", record.ExecutionId);
        Assert.Equal("exec-1", record.AuditCorrelationId);
        Assert.Equal("actor-1", record.ActorId);
        Assert.Equal("ticket-1", record.ChangeReason);
        Assert.Equal("rollback-1", record.RollbackPlanId);
        Assert.Equal("audit", record.ImmutableAuditProviderId);
        Assert.Equal("rollback", record.RollbackProviderId);
    }

    [Fact]
    public async Task InvokeAsync_NonWriteIntent_DoesNotRequireGovernance() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var registry = new ToolRegistry();
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject().Add("send", false),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invoked", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WriteIntentDenied_AppendsAuditRecord() {
        var tool = new StubTool(CreateWriteToolDefinition());
        var sink = new InMemoryAuditSink();
        var registry = new ToolRegistry {
            WriteAuditSink = sink,
            WriteGovernanceRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = "audit",
                RollbackProviderId = "rollback"
            }
        };
        registry.Register(tool);

        Assert.True(registry.TryGet("stub_write", out var registeredTool));
        string output = await registeredTool.InvokeAsync(
            new JsonObject()
                .Add("send", true)
                .Add("allow_write", true)
                .Add("write_execution_id", "exec-2")
                .Add("write_actor_id", "actor-2"),
            CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("write_governance_requirements_not_met", doc.RootElement.GetProperty("error_code").GetString());

        ToolWriteAuditRecord record = Assert.Single(sink.Records);
        Assert.False(record.IsAuthorized);
        Assert.Equal("write_governance_requirements_not_met", record.ErrorCode);
        Assert.Equal("exec-2", record.ExecutionId);
        Assert.Equal("actor-2", record.ActorId);
    }

    [Fact]
    public void Register_WriteCapableToolWithoutGovernanceAuthorization_Throws() {
        var definition = new ToolDefinition(
            "bad_write",
            parameters: ToolSchema.Object(("apply", ToolSchema.Boolean())).NoAdditionalProperties(),
            writeGovernance: new ToolWriteGovernanceContract {
                IsWriteCapable = true,
                RequiresGovernanceAuthorization = false,
                IntentMode = ToolWriteIntentMode.BooleanFlagTrue,
                IntentArgumentName = "apply",
                RequireExplicitConfirmation = true,
                ConfirmationArgumentName = "apply"
            });

        var registry = new ToolRegistry();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("must require governance authorization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolDefinition CreateWriteToolDefinition() {
        return new ToolDefinition(
            "stub_write",
            parameters: ToolSchema.Object(
                    ("send", ToolSchema.Boolean()),
                    ("allow_write", ToolSchema.Boolean()),
                    ("write_execution_id", ToolSchema.String()),
                    ("write_actor_id", ToolSchema.String()),
                    ("write_change_reason", ToolSchema.String()),
                    ("write_rollback_plan_id", ToolSchema.String()))
                .NoAdditionalProperties(),
            writeGovernance: new ToolWriteGovernanceContract {
                IsWriteCapable = true,
                RequiresGovernanceAuthorization = true,
                GovernanceContractId = ToolWriteGovernanceContract.DefaultContractId,
                IntentMode = ToolWriteIntentMode.BooleanFlagTrue,
                IntentArgumentName = "send",
                RequireExplicitConfirmation = true,
                ConfirmationArgumentName = "allow_write"
            });
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition;
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("""{"ok":true,"status":"invoked"}""");
        }
    }

    private sealed class InMemoryAuditSink : IToolWriteAuditSink {
        public List<ToolWriteAuditRecord> Records { get; } = new();

        public void Append(ToolWriteAuditRecord record) {
            Records.Add(record);
        }
    }
}
