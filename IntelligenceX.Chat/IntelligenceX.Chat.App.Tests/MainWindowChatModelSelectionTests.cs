using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards request-time model resolution so compatible runtimes don't send stale cloud model ids.
/// </summary>
public sealed class MainWindowChatModelSelectionTests {
    /// <summary>
    /// Ensures native transport keeps the explicitly configured model.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_KeepsConfiguredModelForNativeTransport() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "native",
            baseUrl: null,
            configuredModel: "gpt-5.4",
            availableModels: new[] { Model("google/gemma-3-4b", isDefault: true) });

        Assert.Equal("gpt-5.4", resolved);
    }

    /// <summary>
    /// Ensures LM Studio compatible runtime falls back to discovered catalog models when a cloud-only id is configured.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_FallsBackToCatalogModelForLmStudioCloudModelMismatch() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "gpt-5.4",
            new[] {
                Model("openai/gpt-oss-20b"),
                Model("google/gemma-3-4b", isDefault: true)
            });

        Assert.Equal("google/gemma-3-4b", resolved);
    }

    /// <summary>
    /// Ensures local compatible runtimes do not send stale cloud model ids before discovery completes.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_ClearsCloudModelWhenLocalCatalogIsUnavailable() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "gpt-5.4",
            Array.Empty<ModelInfoDto>());

        Assert.Null(resolved);
    }

    /// <summary>
    /// Ensures the newer GPT-5 mini/nano ids are also treated as cloud-only ids before local discovery completes.
    /// </summary>
    [Theory]
    [InlineData("gpt-5-mini")]
    [InlineData("gpt-5-nano")]
    public void ResolveChatRequestModelOverride_ClearsMiniAndNanoCloudModelsWhenLocalCatalogIsUnavailable(string configuredModel) {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            configuredModel,
            Array.Empty<ModelInfoDto>());

        Assert.Null(resolved);
    }

    /// <summary>
    /// Ensures compatible runtimes use a discovered default model when no model is configured.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_UsesCatalogDefaultWhenConfiguredModelIsEmpty() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:11434",
            configuredModel: "",
            availableModels: new[] {
                Model("qwen/qwen3-coder-next"),
                Model("mistralai/devstral-small-2-2512", isDefault: true)
            });

        Assert.Equal("mistralai/devstral-small-2-2512", resolved);
    }

    /// <summary>
    /// Ensures non-local compatible endpoints preserve manual deployment ids not present in discovered catalogs.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_PreservesManualModelForNonLocalCompatibleEndpoint() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "https://api.example.com/v1",
            "my-private-deployment",
            new[] { Model("gpt-4.1") });

        Assert.Equal("my-private-deployment", resolved);
    }

    /// <summary>
    /// Ensures configured model is preserved when it matches a discovered catalog entry.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_KeepsConfiguredModelWhenPresentInCatalog() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "openai/gpt-oss-20b",
            new[] {
                Model("openai/gpt-oss-20b"),
                Model("google/gemma-3-4b", isDefault: true)
            });

        Assert.Equal("openai/gpt-oss-20b", resolved);
    }

    /// <summary>
    /// Ensures local compatible runtimes report tools unavailable when selected model lacks tool_use capability.
    /// </summary>
    [Fact]
    public void DescribeTurnToolAvailability_ReportsUnavailableWhenLocalModelDoesNotExposeToolUse() {
        var description = MainWindow.DescribeTurnToolAvailability(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "google/gemma-3-4b",
            new[] { Model("google/gemma-3-4b", capabilities: Array.Empty<string>()) },
            knownToolCount: 4,
            enabledTools: 3,
            disabledTools: 1);

        Assert.Contains("unavailable", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_use", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures local compatible runtimes report tools available when selected model advertises tool_use.
    /// </summary>
    [Fact]
    public void DescribeTurnToolAvailability_ReportsAvailableWhenLocalModelSupportsToolUse() {
        var description = MainWindow.DescribeTurnToolAvailability(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "openai/gpt-oss-20b",
            new[] { Model("openai/gpt-oss-20b", capabilities: new[] { "tool_use" }) },
            knownToolCount: 4,
            enabledTools: 4,
            disabledTools: 0);

        Assert.Contains("available", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_use", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures tool availability reports disabled when no runtime packs are enabled.
    /// </summary>
    [Fact]
    public void DescribeTurnToolAvailability_ReportsUnavailableWhenNoToolsEnabled() {
        var description = MainWindow.DescribeTurnToolAvailability(
            "native",
            baseUrl: null,
            selectedModel: "gpt-5.4",
            availableModels: null,
            knownToolCount: 8,
            enabledTools: 0,
            disabledTools: 8);

        Assert.Contains("unavailable", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures tool availability reports unknown instead of disabled before tool catalog is loaded.
    /// </summary>
    [Fact]
    public void DescribeTurnToolAvailability_ReportsUnknownWhenToolCatalogNotLoadedYet() {
        var description = MainWindow.DescribeTurnToolAvailability(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            selectedModel: null,
            availableModels: null,
            knownToolCount: 0,
            enabledTools: 0,
            disabledTools: 0);

        Assert.Contains("unknown", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("catalog", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures compact tool availability preserves a reason token when all tool packs are disabled.
    /// </summary>
    [Fact]
    public void DescribeCompactTurnToolAvailability_ReportsDisabledReasonToken() {
        var description = MainWindow.DescribeCompactTurnToolAvailability(
            "native",
            baseUrl: null,
            selectedModel: "gpt-5.3-codex",
            availableModels: null,
            knownToolCount: 8,
            enabledTools: 0,
            disabledTools: 8);

        Assert.Equal("unavailable:tool_packs_disabled.", description);
    }

    /// <summary>
    /// Ensures compact tool availability preserves a reason token when the local model lacks tool_use support.
    /// </summary>
    [Fact]
    public void DescribeCompactTurnToolAvailability_ReportsModelCapabilityReasonToken() {
        var description = MainWindow.DescribeCompactTurnToolAvailability(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "openai/gpt-oss-20b",
            new[] { Model("openai/gpt-oss-20b", capabilities: Array.Empty<string>()) },
            knownToolCount: 4,
            enabledTools: 4,
            disabledTools: 0);

        Assert.Equal("unavailable:model_no_tool_use.", description);
    }

    /// <summary>
    /// Ensures compact tool availability preserves a reason token while the tool catalog is still loading.
    /// </summary>
    [Fact]
    public void DescribeCompactTurnToolAvailability_ReportsCatalogLoadingReasonToken() {
        var description = MainWindow.DescribeCompactTurnToolAvailability(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            selectedModel: null,
            availableModels: null,
            knownToolCount: 0,
            enabledTools: 0,
            disabledTools: 0);

        Assert.Equal("unknown:catalog_loading.", description);
    }

    /// <summary>
    /// Ensures runtime execution-locality summaries stay explicit when the live tool catalog mixes local-only and remote-ready tools.
    /// </summary>
    [Fact]
    public void DescribeExecutionLocalitySummary_ReportsMixedExecutionCatalog() {
        var summary = MainWindow.DescribeExecutionLocalitySummary(new ToolCatalogExecutionSummary {
            ExecutionAwareToolCount = 3,
            LocalOnlyToolCount = 1,
            LocalOrRemoteToolCount = 2,
            LocalOnlyPackIds = new[] { "system" },
            RemoteCapablePackIds = new[] { "eventlog", "active_directory" }
        });

        Assert.Contains("mixed locality", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("execution-aware tools=3", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-only=1", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-or-remote=2", summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures compact runtime execution-locality summaries emit stable UI-friendly reason tokens.
    /// </summary>
    [Fact]
    public void DescribeExecutionLocalitySummary_CompactReportsLocalOnlyToken() {
        var summary = MainWindow.DescribeExecutionLocalitySummary(new ToolCatalogExecutionSummary {
            ExecutionAwareToolCount = 1,
            LocalOnlyToolCount = 1,
            LocalOnlyPackIds = new[] { "system" }
        }, compact: true);

        Assert.Equal("local_only:execution_aware_tools.", summary);
    }

    private static ModelInfoDto Model(string model, bool isDefault = false, string[]? capabilities = null) {
        return new ModelInfoDto {
            Id = model,
            Model = model,
            IsDefault = isDefault,
            Capabilities = capabilities ?? Array.Empty<string>()
        };
    }
}
