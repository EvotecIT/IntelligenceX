using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for app-side prompt context gating on ordinary conversational turns.
/// </summary>
public sealed class MainWindowPromptContextGatingTests {
    /// <summary>
    /// Ensures unfinished onboarding does not keep riding along on concrete task turns.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsFalseForConcreteTaskTurn() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "Check AD replication health across all DCs.",
            onboardingInProgress: true,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures light openers can still carry onboarding context when setup is unfinished.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsTrueForLightOpener() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "Hello",
            onboardingInProgress: true,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures assistant-capability questions do not reactivate ambient onboarding context.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsFalseForCapabilityQuestion() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "What can you do for me today?",
            onboardingInProgress: true,
            assistantCapabilityQuestion: true,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures assistant-capability questions do not turn proactive execution hints back on.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsFalseForCapabilityQuestion() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "What can you do for me today?",
            assistantCapabilityQuestion: true,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures runtime self-report questions do not trigger proactive execution hints.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsFalseForRuntimeIntrospectionQuestion() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "What model and tools are you using right now?",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: true,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures concrete imperative tasks can still carry proactive execution guidance when enabled.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsTrueForConcreteImperativeTask() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "Check AD replication health across the remaining DCs.",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures opt-out sessions still emit explicit disabled proactive-mode guidance.
    /// </summary>
    [Fact]
    public void ResolveProactiveExecutionGuidanceMode_ReturnsFalseWhenProactiveModeIsDisabled() {
        var result = MainWindow.ResolveProactiveExecutionGuidanceMode(
            proactiveModeEnabled: false,
            userText: "Check AD replication health across the remaining DCs.",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures runtime self-report turns still receive runtime-mode capability self-knowledge guidance.
    /// </summary>
    [Fact]
    public void SelectCapabilitySelfKnowledgeLines_ReturnsRuntimeModeLinesForRuntimeQuestion() {
        var result = MainWindow.SelectCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            },
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: true);

        Assert.NotNull(result);
        Assert.Contains(result!, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result!, line => line.Contains("invite the user's task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures lexical-fallback runtime self-report narrows capability self-knowledge to direct runtime facts instead of broader provenance detail.
    /// </summary>
    [Fact]
    public void SelectCapabilitySelfKnowledgeLines_NarrowsRuntimeModeLinesForLexicalFallbackRuntimeQuestion() {
        var result = MainWindow.SelectCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogPlugins: new[] {
                new PluginInfoDto {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Origin = "plugin_folder",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    DefaultEnabled = true,
                    Enabled = true,
                    IsDangerous = false,
                    PackIds = new[] { "system" }
                }
            },
            toolCatalogRoutingCatalog: new SessionRoutingCatalogDiagnosticsDto {
                AutonomyReadinessHighlights = new[] { "remote host-targeting is ready for representative tools." }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 1,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "local_only",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                DeferredWorkAffordances = new[] {
                    new SessionCapabilityDeferredWorkAffordanceDto {
                        CapabilityId = "email",
                        DisplayName = "Email",
                        Summary = "Compose or send email follow-up.",
                        AvailabilityMode = "pack_declared",
                        SupportsBackgroundExecution = true,
                        PackIds = new[] { "system" },
                        RoutingFamilies = Array.Empty<string>(),
                        RepresentativeExamples = new[] { "send an email summary after the run" }
                    }
                }
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "system_metrics_summary",
                    Description = "Collect system metrics.",
                    PackId = "system",
                    PackName = "System",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true
                }
            },
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: true,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.LexicalFallback);

        Assert.NotNull(result);
        Assert.Contains(result!, line => line.Contains("confirmed enabled areas", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result!, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result!, line => line.Contains("Registered tool sources currently visible include", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result!, line => line.Contains("Deferred follow-up affordances currently registered", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result!, line => line.Contains("Routing autonomy right now includes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability-question prompt context can surface tool-backed live examples when the tool catalog is available.
    /// </summary>
    [Fact]
    public void SelectCapabilitySelfKnowledgeLines_UsesToolContractExamples_ForCapabilityQuestion() {
        var result = MainWindow.SelectCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "active_directory", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 2,
                EnabledPackCount = 2,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "remote_capable",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "ad_environment_discover",
                    Description = "Discover AD environment context.",
                    PackId = "active_directory",
                    PackName = "Active Directory",
                    IsEnvironmentDiscoverTool = true,
                    SupportsTargetScoping = true,
                    TargetScopeArguments = new[] { "domain_controller", "search_base_dn" }
                },
                new ToolDefinitionDto {
                    Name = "system_metrics_summary",
                    Description = "Collect system metrics.",
                    PackId = "system",
                    PackName = "System",
                    RoutingScope = "host",
                    RoutingEntity = "host",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true,
                    SupportsRemoteExecution = true
                }
            },
            assistantCapabilityQuestion: true,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.NotNull(result);
        Assert.Contains(result!, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result!, line => line.Contains("domain controller or base DN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result!, line => line.Contains("CPU, memory, and disk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures broader runtime self-report questions keep the richer runtime handshake path instead of the compact one.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsFalseForBroaderRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime self-report prefers the compact runtime handshake path even when the raw question is broader.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsTrueForLexicalFallbackRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: false,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.LexicalFallback);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures structured runtime self-report keeps the broader handshake path unless the turn is explicitly compact.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsFalseForStructuredRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: false,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.StructuredDirective);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures compact lexical-fallback runtime handshakes omit execution-locality detail to stay narrowly scoped.
    /// </summary>
    [Fact]
    public void ShouldIncludeExecutionLocalityInRuntimeCapabilityContext_ReturnsFalseForCompactLexicalFallbackRuntimeTurn() {
        var result = MainWindow.ShouldIncludeExecutionLocalityInRuntimeCapabilityContext(
            compactSelfReport: true,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.LexicalFallback);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures compact structured runtime handshakes can still keep execution-locality detail on the trusted path.
    /// </summary>
    [Fact]
    public void ShouldIncludeExecutionLocalityInRuntimeCapabilityContext_ReturnsTrueForCompactStructuredRuntimeTurn() {
        var result = MainWindow.ShouldIncludeExecutionLocalityInRuntimeCapabilityContext(
            compactSelfReport: true,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.StructuredDirective);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures broader runtime handshakes always keep execution-locality detail regardless of provenance.
    /// </summary>
    [Fact]
    public void ShouldIncludeExecutionLocalityInRuntimeCapabilityContext_ReturnsTrueForBroaderRuntimeTurn() {
        var result = MainWindow.ShouldIncludeExecutionLocalityInRuntimeCapabilityContext(
            compactSelfReport: false,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.LexicalFallback);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures genuinely compact runtime self-report questions still use the compact runtime handshake path.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsTrueForCompactRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: true);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures ordinary operational turns do not keep live profile-update scaffolding enabled.
    /// </summary>
    [Fact]
    public void ShouldIncludeLiveProfileUpdates_ReturnsFalseWhenNoProfileFieldsArePresent() {
        var result = MainWindow.ShouldIncludeLiveProfileUpdates(
            hasUserNameUpdate: false,
            hasAssistantPersonaUpdate: false,
            hasThemePresetUpdate: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures actual name/persona/theme updates still opt into the live profile-update guidance path.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ShouldIncludeLiveProfileUpdates_ReturnsTrueWhenAnyProfileFieldIsPresent(
        bool hasUserNameUpdate,
        bool hasAssistantPersonaUpdate,
        bool hasThemePresetUpdate) {
        var result = MainWindow.ShouldIncludeLiveProfileUpdates(
            hasUserNameUpdate,
            hasAssistantPersonaUpdate,
            hasThemePresetUpdate);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures explicit capability/runtime meta turns still use the thin request path when no onboarding
    /// or live profile update guidance is actually needed.
    /// </summary>
    [Fact]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsTrueForMetaTurnsWithoutOnboardingOrProfileUpdates() {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: false,
            includeLiveProfileUpdates: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures unfinished onboarding still opts into the fuller request envelope even after the thin-path cleanup.
    /// </summary>
    [Fact]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsFalseWhenAmbientOnboardingContextIsIncluded() {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: true,
            includeLiveProfileUpdates: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures actual name/persona/theme updates still opt into the fuller request envelope.
    /// </summary>
    [Fact]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsFalseWhenLiveProfileUpdatesAreIncluded() {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: false,
            includeLiveProfileUpdates: true);

        Assert.False(result);
    }
}
