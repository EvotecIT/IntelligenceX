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
            configuredModel: "gpt-5.3-codex",
            availableModels: new[] { Model("google/gemma-3-4b", isDefault: true) });

        Assert.Equal("gpt-5.3-codex", resolved);
    }

    /// <summary>
    /// Ensures LM Studio compatible runtime falls back to discovered catalog models when a cloud-only id is configured.
    /// </summary>
    [Fact]
    public void ResolveChatRequestModelOverride_FallsBackToCatalogModelForLmStudioCloudModelMismatch() {
        var resolved = MainWindow.ResolveChatRequestModelOverride(
            "compatible-http",
            "http://127.0.0.1:1234/v1",
            "gpt-5.3-codex",
            new[] {
                Model("openai/gpt-oss-20b"),
                Model("google/gemma-3-4b", isDefault: true)
            });

        Assert.Equal("google/gemma-3-4b", resolved);
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

    private static ModelInfoDto Model(string model, bool isDefault = false) {
        return new ModelInfoDto {
            Id = model,
            Model = model,
            IsDefault = isDefault
        };
    }
}
