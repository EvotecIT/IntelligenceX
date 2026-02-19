using IntelligenceX.Tools.Common;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolPackGuidanceTests {
    [Fact]
    public void Create_ShouldNormalizeAndDeduplicateToolNames() {
        var model = ToolPackGuidance.Create(
            pack: " system ",
            engine: " ComputerX ",
            tools: new[] { "system_info", " system_info ", "SYSTEM_INFO", "system_pack_info" });

        Assert.Equal("system", model.Pack);
        Assert.Equal("ComputerX", model.Engine);
        Assert.Equal(2, model.Tools.Count);
        Assert.Contains("system_info", model.Tools);
        Assert.Contains("system_pack_info", model.Tools);
    }

    [Fact]
    public void Create_ShouldApplyDefaultOutputContractValues() {
        var model = ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: new[] { "eventlog_pack_info" });

        Assert.Equal(1, model.GuidanceVersion);
        Assert.NotNull(model.OutputContract);
        Assert.Equal("_view", model.OutputContract.ViewFieldSuffix);
        Assert.Equal("Projection arguments are optional and view-only.", model.OutputContract.ViewProjectionPolicy);
        Assert.Contains("raw payload", model.OutputContract.RawPayloadPolicy);
    }

    [Fact]
    public void FlowStep_And_Capability_ShouldNormalizeToolCollections() {
        var step = ToolPackGuidance.FlowStep(
            goal: " Discover ",
            suggestedTools: new[] { "a", "A", " b ", " " },
            notes: " note ");
        var capability = ToolPackGuidance.Capability(
            id: " discovery ",
            summary: " summary ",
            primaryTools: new[] { "x", "X", " y " },
            notes: " details ");

        Assert.Equal("Discover", step.Goal);
        Assert.Equal(2, step.SuggestedTools.Count);
        Assert.Contains("a", step.SuggestedTools);
        Assert.Contains("b", step.SuggestedTools);
        Assert.Equal("note", step.Notes);

        Assert.Equal("discovery", capability.Id);
        Assert.Equal("summary", capability.Summary);
        Assert.Equal(2, capability.PrimaryTools.Count);
        Assert.Contains("x", capability.PrimaryTools);
        Assert.Contains("y", capability.PrimaryTools);
        Assert.Equal("details", capability.Notes);
    }

    [Fact]
    public void Create_ShouldExposeStructuredFlowAndCapabilities() {
        var model = ToolPackGuidance.Create(
            pack: "system",
            engine: "ComputerX",
            tools: new[] { "system_info", "system_pack_info" },
            recommendedFlow: new[] { "step one", "step two" },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep("Collect baseline", new[] { "system_info" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability("host_baseline", "Baseline host inventory", new[] { "system_info" })
            });

        Assert.Single(model.RecommendedFlowSteps);
        Assert.Equal("Collect baseline", model.RecommendedFlowSteps[0].Goal);
        Assert.Single(model.Capabilities);
        Assert.Equal("host_baseline", model.Capabilities[0].Id);
        Assert.Equal("Baseline host inventory", model.Capabilities[0].Summary);
        Assert.Equal(2, model.RecommendedFlow.Count);
    }

    [Fact]
    public void CatalogFromTools_ShouldExposeRequiredArgsAndProjectionSupport() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "stub_a",
                "Tool A",
                ToolSchema.Object(
                        ("query", ToolSchema.String()))
                    .Required("query")
                    .NoAdditionalProperties())),
            new StubTool(new ToolDefinition(
                "stub_b",
                "Tool B",
                ToolSchema.Object(
                        ("columns", ToolSchema.Array(ToolSchema.String())),
                        ("sort_by", ToolSchema.String()),
                        ("cursor", ToolSchema.String()),
                        ("page_size", ToolSchema.Integer()),
                        ("start_time_utc", ToolSchema.String()),
                        ("end_time_utc", ToolSchema.String()),
                        ("attributes", ToolSchema.Array(ToolSchema.String())),
                        ("domain_controller", ToolSchema.String()),
                        ("send", ToolSchema.Boolean()))
                    .WithAuthenticationProfileReference()
                    .WithWriteGovernanceMetadata()
                    .NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true,
                    RequiresGovernanceAuthorization = true,
                    GovernanceContractId = ToolWriteGovernanceContract.DefaultContractId,
                    IntentMode = ToolWriteIntentMode.BooleanFlagTrue,
                    IntentArgumentName = "send",
                    RequireExplicitConfirmation = true,
                    ConfirmationArgumentName = "send"
                },
                authentication: ToolAuthenticationConventions.ProfileReference()))
        });

        Assert.Equal(2, catalog.Count);

        var a = catalog[0];
        Assert.Equal("stub_a", a.Name);
        Assert.Equal("Tool A", a.Description);
        Assert.Single(a.RequiredArguments);
        Assert.Contains("query", a.RequiredArguments);
        Assert.False(a.SupportsTableViewProjection);
        Assert.Single(a.Arguments);
        Assert.Equal("query", a.Arguments[0].Name);
        Assert.Equal("string", a.Arguments[0].Type);
        Assert.True(a.Arguments[0].Required);
        Assert.NotNull(a.Traits);
        Assert.False(a.Traits.SupportsTableViewProjection);
        Assert.False(a.Traits.SupportsPaging);
        Assert.False(a.Traits.SupportsTimeRange);
        Assert.False(a.Traits.SupportsDynamicAttributes);
        Assert.False(a.Traits.SupportsTargetScoping);
        Assert.False(a.Traits.SupportsMutatingActions);
        Assert.False(a.Traits.SupportsWriteGovernanceMetadata);
        Assert.Empty(a.Traits.WriteGovernanceMetadataArguments);
        Assert.False(a.Traits.SupportsAuthentication);
        Assert.Empty(a.Traits.AuthenticationArguments);
        Assert.False(a.IsWriteCapable);
        Assert.False(a.RequiresWriteGovernance);
        Assert.Null(a.WriteGovernanceContractId);
        Assert.False(a.IsAuthenticationAware);
        Assert.False(a.RequiresAuthentication);
        Assert.Null(a.AuthenticationContractId);
        Assert.Null(a.AuthenticationMode);
        Assert.Empty(a.AuthenticationArguments);

        var b = catalog[1];
        Assert.Equal("stub_b", b.Name);
        Assert.True(b.RequiredArguments.Count == 0);
        Assert.True(b.SupportsTableViewProjection);
        Assert.Equal(16, b.Arguments.Count);
        Assert.Contains(b.Arguments, static arg => arg.Name == "columns" && arg.Type == "array<string>" && !arg.Required);
        Assert.Contains(b.Arguments, static arg => arg.Name == "sort_by" && arg.Type == "string" && !arg.Required);
        Assert.NotNull(b.Traits);
        Assert.True(b.Traits.SupportsTableViewProjection);
        Assert.Equal(new[] { "columns", "sort_by" }, b.Traits.TableViewArguments);
        Assert.True(b.Traits.SupportsPaging);
        Assert.Equal(new[] { "cursor", "page_size" }, b.Traits.PagingArguments);
        Assert.True(b.Traits.SupportsTimeRange);
        Assert.Equal(new[] { "start_time_utc", "end_time_utc" }, b.Traits.TimeRangeArguments);
        Assert.True(b.Traits.SupportsDynamicAttributes);
        Assert.Equal(new[] { "attributes" }, b.Traits.DynamicAttributeArguments);
        Assert.True(b.Traits.SupportsTargetScoping);
        Assert.Equal(new[] { "domain_controller" }, b.Traits.TargetScopeArguments);
        Assert.True(b.Traits.SupportsMutatingActions);
        Assert.Equal(new[] { "send" }, b.Traits.MutatingActionArguments);
        Assert.True(b.Traits.SupportsWriteGovernanceMetadata);
        Assert.Equal(ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments, b.Traits.WriteGovernanceMetadataArguments);
        Assert.True(b.Traits.SupportsAuthentication);
        Assert.Equal(new[] { ToolAuthenticationArgumentNames.ProfileId }, b.Traits.AuthenticationArguments);
        Assert.True(b.IsWriteCapable);
        Assert.True(b.RequiresWriteGovernance);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, b.WriteGovernanceContractId);
        Assert.True(b.IsAuthenticationAware);
        Assert.True(b.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, b.AuthenticationContractId);
        Assert.Equal("profile_reference", b.AuthenticationMode);
        Assert.Equal(new[] { ToolAuthenticationArgumentNames.ProfileId }, b.AuthenticationArguments);
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition;
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("{}");
        }
    }
}
