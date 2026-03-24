using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdComputerLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldPredictTargetDistinguishedName() {
        var tool = new AdComputerLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=SRV-SQL-01,OU=Servers,DC=lab,DC=local")
            .Add("target_organizational_unit", "OU=Staging,DC=lab,DC=local")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("move", root.GetProperty("operation").GetString());
        Assert.Equal("OU=Staging,DC=lab,DC=local", root.GetProperty("target_organizational_unit").GetString());
        Assert.Equal("CN=SRV-SQL-01,OU=Staging,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());
        Assert.Equal("SRV-SQL-01", root.GetProperty("computer_name").GetString());

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
        var tool = new AdComputerLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=SRV-SQL-01,OU=Servers,DC=lab,DC=local")
            .Add("new_common_name", "SRV-SQL-02")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("move", root.GetProperty("operation").GetString());
        Assert.Equal("CN=SRV-SQL-02,OU=Servers,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());
        Assert.Equal("SRV-SQL-02", root.GetProperty("computer_name").GetString());

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

    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldPreserveDnAwareLeafParsingAndEscapeReplacementCn() {
        var tool = new AdComputerLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "CN=SRV\\,SQL-01,OU=Servers,DC=lab,DC=local")
            .Add("new_common_name", "SRV,SQL-02")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("CN=SRV\\,SQL-02,OU=Servers,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());
        Assert.Equal("SRV,SQL-02", root.GetProperty("computer_name").GetString());
    }
}
