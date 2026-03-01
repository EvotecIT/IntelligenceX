using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolHealthDiagnosticsTests {
    [Fact]
    public void TryReadFailure_ParsesOkFalseEnvelope() {
        const string json = """
            {"ok":false,"error_code":"provider_unavailable","error":"Domain controller unreachable."}
            """;

        var failed = ToolHealthDiagnostics.TryReadFailure(json, out var errorCode, out var error);

        Assert.True(failed);
        Assert.Equal("provider_unavailable", errorCode);
        Assert.Equal("Domain controller unreachable.", error);
    }

    [Fact]
    public void TryReadFailure_DetectsInvalidJson() {
        const string json = "{";

        var failed = ToolHealthDiagnostics.TryReadFailure(json, out var errorCode, out var error);

        Assert.True(failed);
        Assert.Equal("invalid_json", errorCode);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsToolNotRegistered_WhenMissing() {
        var registry = new ToolRegistry();

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("tool_not_registered", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsOk_WhenToolOutputIsHealthy() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("system_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_RunsOperationalSmokeTool_WhenAvailable() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("system_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new StubTool("system_info", static (_, _) => Task.FromResult("""{"ok":true}""")));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_FailsWhenOperationalSmokeToolFails() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("system_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new StubTool("system_info", static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"WMI not available."}""")));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("smoke_access_denied", result.ErrorCode);
        Assert.Contains("system_info", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsTimeout_WhenProbeExceedsDeadline() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("ad_pack_info", static async (_, token) => {
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            return """{"ok":true}""";
        }));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "ad_pack_info", timeoutSeconds: 1, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("tool_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_DoesNotRunContractVerifySmoke_ForReviewerSetupPackInfo() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("reviewer_setup_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new StubTool("reviewer_setup_contract_verify",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"invalid_argument","error":"autodetect_contract_version is required."}""")));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "reviewer_setup_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void GetPackInfoToolNames_PrefersRoutingRoleWithSuffixFallback() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "custom_pack_summary",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool("legacy_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new StubTool(
            "custom_operational_tool",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var names = ToolHealthDiagnostics.GetPackInfoToolNames(registry);

        Assert.Contains("custom_pack_summary", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("legacy_pack_info", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("custom_operational_tool", names, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public StubTool(
            string name,
            Func<JsonObject?, CancellationToken, Task<string>> invoke,
            ToolRoutingContract? routing = null) {
            Definition = new ToolDefinition(name, description: "stub", routing: routing);
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }
}
