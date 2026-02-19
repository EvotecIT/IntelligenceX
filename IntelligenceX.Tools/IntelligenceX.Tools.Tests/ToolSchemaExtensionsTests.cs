using IntelligenceX.Tools.Common;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolSchemaExtensionsTests {
    [Fact]
    public void WithTableViewOptions_ShouldAddProjectionProperties() {
        var schema = ToolSchema.Object(
                ("q", ToolSchema.String("query")))
            .WithTableViewOptions()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject("columns"));
        Assert.NotNull(properties.GetObject("sort_by"));
        Assert.NotNull(properties.GetObject("sort_direction"));
        Assert.NotNull(properties.GetObject("top"));
    }

    [Fact]
    public void WithWriteGovernanceMetadata_ShouldAddCanonicalGovernanceProperties() {
        var schema = ToolSchema.Object(
                ("send", ToolSchema.Boolean("apply write action")))
            .WithWriteGovernanceMetadata()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolWriteGovernanceArgumentNames.ExecutionId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ActorId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ChangeReason));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackPlanId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackProviderId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.AuditCorrelationId));
    }

    [Fact]
    public void WithWriteGovernanceDefaults_ShouldAddGovernanceMetadataAndDisableAdditionalProperties() {
        var schema = ToolSchema.Object(
                ("send", ToolSchema.Boolean("apply write action")))
            .WithWriteGovernanceDefaults();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolWriteGovernanceArgumentNames.ExecutionId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ActorId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ChangeReason));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackPlanId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackProviderId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.AuditCorrelationId));
        Assert.False(schema.GetBoolean("additionalProperties", true));
    }

    [Fact]
    public void WithAuthenticationProfileReference_ShouldAddAuthProfileProperty() {
        var schema = ToolSchema.Object(
                ("query", ToolSchema.String("query text")))
            .WithAuthenticationProfileReference()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolAuthenticationArgumentNames.ProfileId));
    }

    [Fact]
    public void WithRunAsProfileReference_ShouldAddRunAsProfileProperty() {
        var schema = ToolSchema.Object(
                ("query", ToolSchema.String("query text")))
            .WithRunAsProfileReference()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolAuthenticationArgumentNames.RunAsProfileId));
    }

    [Fact]
    public void WithAuthenticationProbeReference_ShouldAddProbeIdProperty() {
        var schema = ToolSchema.Object(
                ("query", ToolSchema.String("query text")))
            .WithAuthenticationProbeReference()
            .NoAdditionalProperties();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolAuthenticationArgumentNames.ProbeId));
    }

    [Fact]
    public void WithWriteGovernanceAndAuthenticationProbe_ShouldAddProbeAndGovernanceAndDisableAdditionalProperties() {
        var schema = ToolSchema.Object(
                ("send", ToolSchema.Boolean("apply write action")))
            .WithWriteGovernanceAndAuthenticationProbe();

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject(ToolAuthenticationArgumentNames.ProbeId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ExecutionId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ActorId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ChangeReason));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackPlanId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackProviderId));
        Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.AuditCorrelationId));
        Assert.False(schema.GetBoolean("additionalProperties", true));
    }
}
