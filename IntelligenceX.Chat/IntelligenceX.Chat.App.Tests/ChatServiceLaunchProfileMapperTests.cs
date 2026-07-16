using System.Threading.Tasks;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the shared desktop profile-to-service launch contract.
/// </summary>
public sealed class ChatServiceLaunchProfileMapperTests {
    /// <summary>
    /// Ensures persisted provider settings map identically for every desktop shell.
    /// </summary>
    [Fact]
    public void Create_MapsPersistedProviderSettings() {
        var options = ChatServiceLaunchProfileMapper.Create(
            new ChatAppState {
                ProfileName = " enterprise ",
                LocalProviderTransport = " compatible-http ",
                LocalProviderBaseUrl = " http://127.0.0.1:1234/v1 ",
                LocalProviderModel = " local-model ",
                LocalProviderOpenAIAuthMode = " basic ",
                LocalProviderOpenAIBasicUsername = " operator ",
                LocalProviderOpenAIAccountId = " account-1 ",
                LocalProviderReasoningEffort = " high ",
                LocalProviderReasoningSummary = " concise ",
                LocalProviderTextVerbosity = " low ",
                LocalProviderTemperature = 0.25,
                LocalProviderImageGenerationOverrideActive = true,
                LocalProviderImageGenerationEnabled = false,
                LocalProviderImageGenerationSize = " 1024x1024 "
            },
            new[] { new ChatServicePackToggle("active-directory", true) });

        Assert.Equal("enterprise", options.LoadProfileName);
        Assert.Equal("enterprise", options.SaveProfileName);
        Assert.Equal("compatible-http", options.OpenAITransport);
        Assert.Equal("http://127.0.0.1:1234/v1", options.OpenAIBaseUrl);
        Assert.Equal("local-model", options.Model);
        Assert.Equal("basic", options.OpenAIAuthMode);
        Assert.Equal("operator", options.OpenAIBasicUsername);
        Assert.Equal("account-1", options.OpenAIAccountId);
        Assert.True(options.OpenAIStreaming);
        Assert.True(options.OpenAIAllowInsecureHttp);
        Assert.Equal("high", options.ReasoningEffort);
        Assert.Equal("concise", options.ReasoningSummary);
        Assert.Equal("low", options.TextVerbosity);
        Assert.Equal(0.25, options.Temperature);
        Assert.False(options.ImageGenerationEnabled);
        Assert.True(options.ClearImageGenerationQuality);
        Assert.Equal("1024x1024", options.ImageGenerationSize);
        Assert.True(options.ClearImageGenerationOutputFormat);
        Assert.True(options.ClearImageGenerationOutputCompression);
        Assert.True(options.ClearImageGenerationBackground);
        Assert.True(options.ClearImageGenerationOutputDirectory);
        Assert.NotNull(options.PackToggles);
        var toggle = Assert.Single(options.PackToggles!);
        Assert.Equal("active-directory", toggle.PackId);
        Assert.True(toggle.Enabled);
    }

    /// <summary>
    /// Ensures default persisted image values do not override the service profile.
    /// </summary>
    [Fact]
    public void Create_OmitsUnsetImageGenerationOverrides() {
        var options = ChatServiceLaunchProfileMapper.Create(new ChatAppState());

        Assert.Null(options.ImageGenerationEnabled);
        Assert.False(options.ClearImageGenerationQuality);
        Assert.False(options.ClearImageGenerationSize);
        Assert.False(options.ClearImageGenerationOutputFormat);
        Assert.False(options.ClearImageGenerationOutputCompression);
        Assert.False(options.ClearImageGenerationBackground);
        Assert.False(options.ClearImageGenerationOutputDirectory);
    }

    /// <summary>
    /// Ensures the native runtime evaluates profile options when the service starts rather than at window construction.
    /// </summary>
    [Fact]
    public async Task NativeRuntime_CreateServiceProcessStartOptions_UsesLateBoundProfile() {
        var current = new ChatServiceLaunchProfileOptions { LoadProfileName = "first" };
        await using var runtime = new NativeChatServiceRuntime(
            "intelligencex.chat.tests",
            () => current);

        Assert.Same(current, runtime.CreateServiceProcessStartOptions().ProfileOptions);

        current = new ChatServiceLaunchProfileOptions { LoadProfileName = "second" };
        Assert.Same(current, runtime.CreateServiceProcessStartOptions().ProfileOptions);
    }
}
