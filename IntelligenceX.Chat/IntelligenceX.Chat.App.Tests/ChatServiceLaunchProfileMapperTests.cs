using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the shared desktop profile-to-service launch contract.
/// </summary>
public sealed class ChatServiceLaunchProfileMapperTests {
    /// <summary>
    /// Keeps detached ownership sticky when a later probe observes the already-running owned service.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void NativeRuntime_ResolveDetachedServiceOwnership_DoesNotLoseExistingOwnership(
        bool currentlyOwned,
        bool launched,
        bool detachedServiceMode,
        bool expected) {
        Assert.Equal(
            expected,
            NativeChatServiceRuntime.ResolveDetachedServiceOwnership(
                currentlyOwned,
                launched,
                detachedServiceMode));
    }

    /// <summary>
    /// Ensures persisted provider settings map identically for every desktop shell.
    /// </summary>
    [Fact]
    public void Create_MapsPersistedProviderSettings() {
        var options = ChatServiceLaunchProfileMapper.Create(
            new ChatAppState {
                ProfileName = " enterprise ",
                LocalProviderRuntimeOverrideActive = true,
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
        Assert.True(options.ApplyRuntimeOverrides);
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
    /// Ensures a conversation-only app state selects an existing service profile without replacing its runtime settings.
    /// </summary>
    [Fact]
    public void Create_UnconfiguredAppProfileIsLoadOnly() {
        var options = ChatServiceLaunchProfileMapper.Create(new ChatAppState {
            ProfileName = "existing-service-profile",
            LocalProviderModel = "app-default-that-must-not-win"
        });

        var args = ServiceLaunchArguments.Build(
            "intelligencex.chat",
            detachedServiceMode: true,
            parentProcessId: 42,
            profileOptions: options);

        Assert.False(options.ApplyRuntimeOverrides);
        Assert.Contains("--profile", args);
        Assert.Contains("existing-service-profile", args);
        Assert.DoesNotContain("--save-profile", args);
        Assert.DoesNotContain("--model", args);
        Assert.DoesNotContain("app-default-that-must-not-win", args);
    }

    /// <summary>
    /// Preserves legacy app-owned profiles while keeping explicit conversation-only profiles load-only.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void ResolveRuntimeOverrideActive_TracksPersistedAuthority(
        bool active,
        bool markerWasPresent,
        bool loadedProfile,
        bool expected) {
        var state = new ChatAppState {
            LocalProviderRuntimeOverrideActive = active,
            LocalProviderRuntimeOverrideActiveWasPresent = markerWasPresent
        };

        Assert.Equal(expected, ChatServiceLaunchProfileMapper.ResolveRuntimeOverrideActive(state, loadedProfile));
    }

    /// <summary>
    /// Ensures load-only profiles do not receive app-default provider controls on each turn.
    /// </summary>
    [Fact]
    public void CreateFromState_LoadOnlyProfileDefersProviderControlsToService() {
        var state = new ChatAppState {
            LocalProviderRuntimeOverrideActive = false,
            LocalProviderTransport = "compatible-http",
            LocalProviderBaseUrl = "http://127.0.0.1:1234/v1",
            LocalProviderModel = "app-default-that-must-not-win",
            LocalProviderReasoningEffort = "high",
            LocalProviderTemperature = 0.75,
            CachedModelsTransport = "compatible-http",
            CachedModelsBaseUrl = "http://127.0.0.1:1234/v1",
            CachedModels = [
                new ModelInfoDto { Id = "stale-local-default", Model = "stale-local-default", IsDefault = true }
            ]
        };

        var inherited = ChatRequestOptionsFactory.CreateFromState(state);
        var explicitConversation = ChatRequestOptionsFactory.CreateFromState(state, "gpt-5.4");

        Assert.Null(inherited.Model);
        Assert.Null(inherited.ReasoningEffort);
        Assert.Null(inherited.Temperature);
        Assert.Equal("gpt-5.4", explicitConversation.Model);
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
    /// Ensures an explicitly inactive override is authoritative even when old values remain in the profile.
    /// </summary>
    [Fact]
    public void Create_ExplicitInactiveImageGenerationOverrideIgnoresStaleValues() {
        var options = ChatServiceLaunchProfileMapper.Create(new ChatAppState {
            LocalProviderImageGenerationOverrideActive = false,
            LocalProviderImageGenerationOverrideActiveWasPresent = true,
            LocalProviderImageGenerationEnabled = true,
            LocalProviderImageGenerationQuality = "high",
            LocalProviderImageGenerationSize = "1024x1024",
            LocalProviderImageGenerationOutputCompression = 80
        });

        Assert.Null(options.ImageGenerationEnabled);
        Assert.Null(options.ImageGenerationQuality);
        Assert.Null(options.ImageGenerationSize);
        Assert.Null(options.ImageGenerationOutputCompression);
        Assert.False(options.ClearImageGenerationQuality);
        Assert.False(options.ClearImageGenerationSize);
        Assert.False(options.ClearImageGenerationOutputCompression);
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

    /// <summary>
    /// Ensures persisted profile controls are carried into native and legacy turn requests.
    /// </summary>
    [Fact]
    public void CreateFromState_MapsPerTurnProviderAndAutonomyOptions() {
        var options = ChatRequestOptionsFactory.CreateFromState(
            new ChatAppState {
                LocalProviderRuntimeOverrideActive = true,
                LocalProviderTransport = "compatible-http",
                LocalProviderBaseUrl = "http://127.0.0.1:1234/v1",
                LocalProviderModel = "profile-model",
                LocalProviderReasoningEffort = "high",
                LocalProviderReasoningSummary = "concise",
                LocalProviderTextVerbosity = "low",
                LocalProviderTemperature = 0.4,
                LocalProviderImageGenerationOverrideActive = true,
                LocalProviderImageGenerationEnabled = true,
                LocalProviderImageGenerationQuality = "high",
                DisabledTools = [" z_tool ", "a_tool", "A_TOOL"],
                AutonomyMaxToolRounds = 7,
                AutonomyParallelTools = false,
                AutonomyTurnTimeoutSeconds = 0,
                AutonomyToolTimeoutSeconds = 55,
                AutonomyWeightedToolRouting = true,
                AutonomyMaxCandidateTools = 19,
                AutonomyPlanExecuteReviewLoop = true,
                AutonomyMaxReviewPasses = 2,
                AutonomyModelHeartbeatSeconds = 3
            },
            conversationModelOverride: "conversation-model");

        Assert.Equal("conversation-model", options.Model);
        Assert.Equal("high", options.ReasoningEffort);
        Assert.Equal("concise", options.ReasoningSummary);
        Assert.Equal("low", options.TextVerbosity);
        Assert.Equal(0.4, options.Temperature);
        Assert.True(options.ImageGenerationEnabled);
        Assert.Equal("high", options.ImageGenerationQuality);
        Assert.NotNull(options.DisabledTools);
        Assert.Equal(["a_tool", "z_tool"], options.DisabledTools!);
        Assert.Equal(7, options.MaxToolRounds);
        Assert.False(options.ParallelTools);
        Assert.Equal("force_serial", options.ParallelToolMode);
        Assert.Equal(0, options.TurnTimeoutSeconds);
        Assert.Equal(55, options.ToolTimeoutSeconds);
        Assert.True(options.WeightedToolRouting);
        Assert.Equal(19, options.MaxCandidateTools);
        Assert.True(options.PlanExecuteReviewLoop);
        Assert.Equal(2, options.MaxReviewPasses);
        Assert.Equal(3, options.ModelHeartbeatSeconds);
    }

    /// <summary>
    /// Ensures local compatible runtimes retain the shared conservative defaults.
    /// </summary>
    [Fact]
    public void CreateFromState_UsesConservativeLocalRuntimeDefaultsWithoutStaleImageOverrides() {
        var options = ChatRequestOptionsFactory.CreateFromState(new ChatAppState {
            LocalProviderTransport = "compatible-http",
            LocalProviderBaseUrl = "http://localhost:11434/v1",
            LocalProviderImageGenerationOverrideActive = false,
            LocalProviderImageGenerationEnabled = true,
            LocalProviderImageGenerationQuality = "stale"
        });

        Assert.Equal(8, options.MaxToolRounds);
        Assert.False(options.WeightedToolRouting);
        Assert.Equal(0, options.ModelHeartbeatSeconds);
        Assert.Null(options.ImageGenerationEnabled);
        Assert.Null(options.ImageGenerationQuality);
    }

    /// <summary>
    /// Ensures local runtime defaults require an actual loopback host instead of a matching hostname fragment.
    /// </summary>
    [Fact]
    public void CreateFromState_DoesNotTreatLookalikeHostAsLocalRuntime() {
        var options = ChatRequestOptionsFactory.CreateFromState(new ChatAppState {
            LocalProviderTransport = "compatible-http",
            LocalProviderBaseUrl = "https://example.test/v1?upstream=127.0.0.1:1234"
        });

        Assert.Equal(ChatRequestOptionLimits.DefaultToolRounds, options.MaxToolRounds);
        Assert.Null(options.WeightedToolRouting);
        Assert.Null(options.ModelHeartbeatSeconds);
    }

    /// <summary>
    /// Ensures unset native autonomy values inherit the connected service policy.
    /// </summary>
    [Fact]
    public void CreateFromState_UsesConnectedServicePolicyDefaults() {
        var options = ChatRequestOptionsFactory.CreateFromState(
            new ChatAppState(),
            servicePolicy: new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 11,
                ParallelTools = false,
                AllowMutatingParallelToolCalls = false,
                TurnTimeoutSeconds = 420,
                ToolTimeoutSeconds = 75
            });

        Assert.Equal(11, options.MaxToolRounds);
        Assert.False(options.ParallelTools);
        Assert.Equal(ChatRequestOptionsFactory.ParallelToolModeAuto, options.ParallelToolMode);
        Assert.Equal(420, options.TurnTimeoutSeconds);
        Assert.Equal(75, options.ToolTimeoutSeconds);
    }
}
