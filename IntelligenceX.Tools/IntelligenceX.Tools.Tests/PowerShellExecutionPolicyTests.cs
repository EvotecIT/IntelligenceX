using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.PowerShell;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class PowerShellExecutionPolicyTests {
    [Fact]
    public async Task RunTool_ShouldRejectInvalidIntent() {
        var tool = new PowerShellRunTool(new PowerShellToolOptions {
            Enabled = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("command", "Get-Date")
                .Add("intent", "invalid"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task RunTool_ShouldRejectMutatingPayloadInReadOnlyMode() {
        var tool = new PowerShellRunTool(new PowerShellToolOptions {
            Enabled = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("command", "Set-ItemProperty -Path HKLM:\\SOFTWARE\\Contoso -Name Enabled -Value 1"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", doc.RootElement.GetProperty("error_code").GetString());
        var error = doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
        Assert.Contains("Read-only intent rejected", error);
    }

    [Fact]
    public async Task RunTool_ShouldRejectReadWriteWhenPolicyDisallowsWrites() {
        var tool = new PowerShellRunTool(new PowerShellToolOptions {
            Enabled = true,
            AllowWrite = false
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("command", "Set-Service -Name bits -StartupType Manual")
                .Add("intent", "read_write")
                .Add("allow_write", true),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("disabled", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task RunTool_ShouldRequireExplicitAllowWriteWhenConfigured() {
        var tool = new PowerShellRunTool(new PowerShellToolOptions {
            Enabled = true,
            AllowWrite = true,
            RequireExplicitWriteFlag = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("command", "Set-Service -Name bits -StartupType Manual")
                .Add("intent", "read_write"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task EnvironmentDiscover_ShouldExposePolicyAndRuntimeFields() {
        var options = new PowerShellToolOptions {
            Enabled = true,
            AllowWrite = true,
            RequireExplicitWriteFlag = true
        };
        var tool = new PowerShellEnvironmentDiscoverTool(options);

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.TryGetProperty("policy", out var policy));
        Assert.True(policy.GetProperty("allow_write").GetBoolean());
        Assert.True(policy.GetProperty("require_explicit_write_flag").GetBoolean());
        Assert.True(root.TryGetProperty("runtime", out _));
    }
}
