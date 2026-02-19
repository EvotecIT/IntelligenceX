using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolAuthenticationRegistryTests {
    [Fact]
    public void Register_AuthenticationAwareProfileContractWithoutSchemaArgument_Throws() {
        var definition = new ToolDefinition(
            "auth_profile_missing",
            parameters: ToolSchema.Object(
                    ("query", ToolSchema.String()))
                .NoAdditionalProperties(),
            authentication: ToolAuthenticationConventions.ProfileReference());

        var registry = new ToolRegistry();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("must expose authentication argument", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_AuthenticationAwareProfileContractWithSchemaArgument_Succeeds() {
        var definition = new ToolDefinition(
            "auth_profile_ok",
            parameters: ToolSchema.Object(
                    ("query", ToolSchema.String()))
                .WithAuthenticationProfileReference()
                .NoAdditionalProperties(),
            authentication: ToolAuthenticationConventions.ProfileReference());

        var registry = new ToolRegistry();
        registry.Register(new StubTool(definition));
        Assert.True(registry.TryGet("auth_profile_ok", out _));
    }

    [Fact]
    public void Register_AuthenticationAwareHostManagedWithoutSchemaArgument_Succeeds() {
        var definition = new ToolDefinition(
            "auth_host_managed_ok",
            parameters: ToolSchema.Object(
                    ("query", ToolSchema.String()))
                .NoAdditionalProperties(),
            authentication: ToolAuthenticationConventions.HostManaged());

        var registry = new ToolRegistry();
        registry.Register(new StubTool(definition));
        Assert.True(registry.TryGet("auth_host_managed_ok", out _));
    }

    [Fact]
    public void Register_AuthenticationAwareHostManagedProbeContractWithoutSchemaArgument_Throws() {
        var definition = new ToolDefinition(
            "auth_host_managed_probe_missing",
            parameters: ToolSchema.Object(
                    ("query", ToolSchema.String()))
                .NoAdditionalProperties(),
            authentication: ToolAuthenticationConventions.HostManaged(
                supportsConnectivityProbe: true,
                probeToolName: "smtp_probe"));

        var registry = new ToolRegistry();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("must expose authentication argument", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ToolAuthenticationArgumentNames.ProbeId, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_AuthenticationAwareHostManagedProbeContractWithSchemaArgument_Succeeds() {
        var definition = new ToolDefinition(
            "auth_host_managed_probe_ok",
            parameters: ToolSchema.Object(
                    ("query", ToolSchema.String()))
                .WithAuthenticationProbeReference()
                .NoAdditionalProperties(),
            authentication: ToolAuthenticationConventions.HostManaged(
                supportsConnectivityProbe: true,
                probeToolName: "smtp_probe"));

        var registry = new ToolRegistry();
        registry.Register(new StubTool(definition));
        Assert.True(registry.TryGet("auth_host_managed_probe_ok", out _));
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition;
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("""{"ok":true}""");
        }
    }
}
