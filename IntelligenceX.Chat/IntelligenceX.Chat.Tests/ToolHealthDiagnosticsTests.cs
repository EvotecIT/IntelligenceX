using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using JsonObject = IntelligenceX.Json.JsonObject;
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
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_RunsOperationalSmokeTool_WithPagingDefaultsWhenSupported() {
        JsonObject? capturedSmokeArguments = null;
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_info",
            (arguments, _) => {
                capturedSmokeArguments = arguments;
                return Task.FromResult("""{"ok":true}""");
            },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            parameters: ToolSchema.Object(
                ("page_size", ToolSchema.Integer("Optional page size.")))
                .NoAdditionalProperties()));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(capturedSmokeArguments);
        Assert.Equal(25, capturedSmokeArguments!.GetInt64("page_size"));
    }

    [Fact]
    public async Task ProbeAsync_FailsWhenOperationalSmokeToolFails() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_info",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"WMI not available."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("smoke_access_denied", result.ErrorCode);
        Assert.Contains("system_info", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_RebuildsSmokePlanCache_WhenToolCatalogChanges() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_smoke_operational",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"Legacy smoke selection."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var firstResult = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.False(firstResult.Ok);
        Assert.Equal("smoke_access_denied", firstResult.ErrorCode);
        Assert.Contains("system_smoke_operational", firstResult.Error, StringComparison.OrdinalIgnoreCase);

        registry.Register(new StubTool(
            "system_smoke_diagnostic",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));

        var secondResult = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(secondResult.Ok);
        Assert.Null(secondResult.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_RebuildsSmokePlanCache_WhenCatalogCountUnchangedButMetadataChanges() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_smoke_a",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        registry.Register(new StubTool(
            "system_smoke_b",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"Preferred diagnostic smoke."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));

        var firstResult = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.False(firstResult.Ok);
        Assert.Equal("smoke_access_denied", firstResult.ErrorCode);
        Assert.Contains("system_smoke_b", firstResult.Error, StringComparison.OrdinalIgnoreCase);

        registry.Register(new StubTool(
            "system_smoke_b",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"Now operational and lower-priority."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }),
            replaceExisting: true);

        var secondResult = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(secondResult.Ok);
        Assert.Null(secondResult.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_DoesNotRunSmoke_WhenComposedSchemaDeclaresRequiredArguments() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_info",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"should_not_run","error":"Smoke should skip required composed schema."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject(StringComparer.Ordinal)
                    .Add("query", ToolSchema.String("Target query.")))
                .Add("allOf", new JsonArray()
                    .Add(new JsonObject()
                        .Add("required", new JsonArray().Add("query"))))
                .Add("additionalProperties", false)));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_DoesNotRunSmoke_WhenToolRequiresSelectionForFallback() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "system_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool(
            "system_smoke",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"should_not_run","error":"Smoke should skip selector-gated tools."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] {
                "fallback:requires_selection",
                "fallback_selection_keys:target"
            }));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "system_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
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
        registry.Register(new StubTool(
            "reviewer_setup_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "reviewer_setup",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool("reviewer_setup_contract_verify",
            static (_, _) => Task.FromResult("""{"ok":false,"error_code":"invalid_argument","error":"autodetect_contract_version is required."}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "reviewer_setup",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            parameters: ToolSchema.Object(
                    ("autodetect_contract_version", ToolSchema.Boolean()))
                .Required("autodetect_contract_version")
                .NoAdditionalProperties()));

        var result = await ToolHealthDiagnostics.ProbeAsync(registry, "reviewer_setup_pack_info", timeoutSeconds: 2, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void GetPackInfoToolNames_UsesPackInfoRoleOnly() {
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
        Assert.DoesNotContain("legacy_pack_info", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("custom_operational_tool", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPackInfoToolNames_WhenExplicitRoleRequired_ExcludesNonExplicitPackInfoDefinitions() {
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
        registry.Register(new StubTool(
            "inferred_pack_info",
            static (_, _) => Task.FromResult("""{"ok":true}"""),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceInferred,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RolePackInfo
            }));
        registry.Register(new StubTool("legacy_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));

        var names = ToolHealthDiagnostics.GetPackInfoToolNames(registry, requireExplicitPackInfoRole: true);

        Assert.Contains("custom_pack_summary", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("inferred_pack_info", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("legacy_pack_info", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_DoesNotRunSmokeForSuffixOnlyToolWithoutPackInfoRole() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("legacy_pack_info", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new StubTool("legacy_diagnostic", static (_, _) => Task.FromResult("""{"ok":false,"error_code":"access_denied","error":"Should not run."}""")));

        var result = await ToolHealthDiagnostics.ProbeAsync(
            registry,
            toolName: "legacy_pack_info",
            timeoutSeconds: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.ErrorCode);
    }

    private sealed class StubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public StubTool(
            string name,
            Func<JsonObject?, CancellationToken, Task<string>> invoke,
            ToolRoutingContract? routing = null,
            JsonObject? parameters = null,
            IReadOnlyList<string>? tags = null) {
            var effectiveRouting = routing ?? new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = ResolveDefaultPackId(name),
                Role = ToolRoutingTaxonomy.RoleOperational
            };
            Definition = new ToolDefinition(name, description: "stub", parameters: parameters, tags: tags, routing: effectiveRouting);
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }

        private static string ResolveDefaultPackId(string name) {
            var normalizedName = (name ?? string.Empty).Trim();
            if (normalizedName.Length == 0) {
                return "test";
            }

            var separator = normalizedName.IndexOf('_');
            var candidate = separator <= 0
                ? normalizedName
                : normalizedName.Substring(0, separator);
            var normalizedPackId = ToolSelectionMetadata.NormalizePackId(candidate);
            return string.IsNullOrWhiteSpace(normalizedPackId) ? "test" : normalizedPackId;
        }
    }
}
