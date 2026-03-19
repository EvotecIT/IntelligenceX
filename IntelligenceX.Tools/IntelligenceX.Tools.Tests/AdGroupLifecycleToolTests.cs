using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdGroupLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldPredictTargetDistinguishedName() {
        var tool = new AdGroupLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=GG-SQL-Admins,OU=Legacy,DC=lab,DC=local")
            .Add("target_organizational_unit", "OU=Privileged,DC=lab,DC=local")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("move", root.GetProperty("operation").GetString());
        Assert.Equal("OU=Privileged,DC=lab,DC=local", root.GetProperty("target_organizational_unit").GetString());
        Assert.Equal("CN=GG-SQL-Admins,OU=Privileged,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());

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
    public async Task InvokeAsync_RenameDryRun_ShouldPredictRenamedDistinguishedName() {
        var tool = new AdGroupLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=GG-SQL-Admins,OU=Legacy,DC=lab,DC=local")
            .Add("new_common_name", "GG-SQL-Operators")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("move", root.GetProperty("operation").GetString());
        Assert.Equal("CN=GG-SQL-Operators,OU=Legacy,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());

        var updatedAttributes = root.GetProperty("updated_attributes")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        Assert.Contains("cn", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("name", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("distinguishedName", updatedAttributes, StringComparer.OrdinalIgnoreCase);
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
    }
}
