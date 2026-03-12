using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolHostTargetingTests {
    [Fact]
    public void ToolSupportsHostTargetInputs_RecognizesMachineNameSchemas() {
        var definition = new ToolDefinition(
            "eventlog_live_query",
            "Remote Event Log query.",
            ToolSchema.Object(
                    ("machine_name", ToolSchema.String("Remote machine.")),
                    ("channel", ToolSchema.String("Channel.")))
                .NoAdditionalProperties());

        Assert.True(ToolHostTargeting.ToolSupportsHostTargetInputs(definition));
        Assert.Equal(new[] { "machine_name" }, ToolHostTargeting.GetSupportedHostTargetArguments(definition));
    }

    [Fact]
    public void TryPickPreferredInputArgument_PrefersExistingArgumentCasingAndArrayShape() {
        var definition = new ToolDefinition(
            "eventlog_named_events_query",
            "Remote Event Log query.",
            ToolSchema.Object(
                    ("machine_name", ToolSchema.String("Remote machine.")),
                    ("machine_names", ToolSchema.Array(ToolSchema.String("Remote machines."))))
                .NoAdditionalProperties());
        var arguments = new JsonObject();
        arguments.Add("machine_names", new JsonArray().Add("dc01.contoso.com"));

        var ok = ToolHostTargeting.TryPickPreferredInputArgument(definition, arguments, out var key, out var keyIsArray);

        Assert.True(ok);
        Assert.Equal("machine_names", key);
        Assert.True(keyIsArray);
    }

    [Fact]
    public void TryReadHostTargetValues_ReadsMachineNameInputs() {
        var arguments = new JsonObject()
            .Add("machine_name", "dc01.contoso.com")
            .Add("channel", "Security");

        var ok = ToolHostTargeting.TryReadHostTargetValues(arguments, out var values);

        Assert.True(ok);
        Assert.Equal(new[] { "dc01.contoso.com" }, values);
    }
}
