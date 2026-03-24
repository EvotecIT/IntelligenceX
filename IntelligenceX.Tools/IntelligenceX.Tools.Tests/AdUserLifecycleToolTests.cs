using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdUserLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_UpdateDryRun_ShouldExposeTypedAttributeAndMembershipPlan() {
        var tool = new AdUserLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "update")
            .Add("identity", "alice")
            .Add("department", "Finance")
            .Add("title", "Senior Analyst")
            .Add("groups_to_add", new JsonArray().Add("GG-Finance-Users"))
            .Add("groups_to_remove", new JsonArray().Add("GG-Legacy-Users"))
            .Add("clear_attributes", new JsonArray().Add("mobile"))
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("update", root.GetProperty("operation").GetString());
        Assert.Equal("alice", root.GetProperty("identity").GetString());
        Assert.Equal("Dry-run only. Set apply=true to execute the lifecycle action.", root.GetProperty("message").GetString());

        var updatedAttributes = root.GetProperty("updated_attributes")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        Assert.Contains("department", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("title", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("memberOf", updatedAttributes, StringComparer.OrdinalIgnoreCase);

        var clearedAttributes = root.GetProperty("cleared_attributes")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        Assert.Contains("mobile", clearedAttributes, StringComparer.OrdinalIgnoreCase);

        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldPredictTargetDistinguishedName() {
        var tool = new AdUserLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=Alice Example,CN=Users,DC=lab,DC=local")
            .Add("target_organizational_unit", "OU=Sales,DC=lab,DC=local")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("move", root.GetProperty("operation").GetString());
        Assert.Equal("OU=Sales,DC=lab,DC=local", root.GetProperty("target_organizational_unit").GetString());
        Assert.Equal("CN=Alice Example,OU=Sales,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());

        var updatedAttributes = root.GetProperty("updated_attributes")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        Assert.Contains("distinguishedName", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldUseDnAwareLeafParsingForEscapedCommaIdentity() {
        var tool = new AdUserLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=Doe\\, Alice,CN=Users,DC=lab,DC=local")
            .Add("target_organizational_unit", "OU=Sales,DC=lab,DC=local")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("CN=Doe\\, Alice,OU=Sales,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());
    }
}
