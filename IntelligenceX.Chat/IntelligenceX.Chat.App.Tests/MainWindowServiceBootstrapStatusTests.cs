using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies status text extraction from service bootstrap diagnostics.
/// </summary>
public sealed class MainWindowServiceBootstrapStatusTests {
    /// <summary>
    /// Parses pack bootstrap begin-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPackProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_load_progress pack='eventlog' phase='begin' index='2' total='11'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... initializing tool packs 2/11 (eventlog)", statusText);
    }

    /// <summary>
    /// Parses pack bootstrap end-progress diagnostics so users can see what just finished and how long it took.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPackProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_load_progress pack='eventlog' phase='end' index='2' total='11' elapsed_ms='42'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... initialized tool packs 2/11 (eventlog, 42ms)", statusText);
    }

    /// <summary>
    /// Parses pack registration begin-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPackRegistrationProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_register_progress pack='eventlog' phase='begin' index='2' total='11'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... registering tool pack 2/11 (eventlog)", statusText);
    }

    /// <summary>
    /// Marks pack registration progress as send-safe so startup progress remains visible
    /// during first-turn waits while metadata sync is still running.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_PackRegistrationProgress_IsSendSafe() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_register_progress pack='eventlog' phase='begin' index='2' total='11'",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... registering tool pack 2/11 (eventlog)", statusText);
        Assert.True(allowDuringSend);
    }

    /// <summary>
    /// Parses pack registration end-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPackRegistrationProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_register_progress pack='eventlog' phase='end' index='2' total='11' elapsed_ms='42'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... registered tool pack 2/11 (eventlog, 42ms)", statusText);
    }

    /// <summary>
    /// Parses runtime provider connect begin diagnostics and marks status updates as send-safe.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesProviderConnectProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[startup] provider_connect_progress phase='begin' operation='connect_client' transport='native'",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... connecting runtime provider (native)", statusText);
        Assert.True(allowDuringSend);
    }

    /// <summary>
    /// Parses runtime provider connect end diagnostics and marks status updates as send-safe.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesProviderConnectProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[startup] provider_connect_progress phase='end' operation='connect_client' transport='native' status='ok' elapsed_ms='3120'",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... connected runtime provider (native, 3120ms)", statusText);
        Assert.True(allowDuringSend);
    }

    /// <summary>
    /// Parses plugin folder begin-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPluginProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [plugin] load_progress plugin='dnsclientx' phase='begin' index='3' total='12'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... loading tool packs 3/12 (dnsclientx)", statusText);
    }

    /// <summary>
    /// Parses plugin end-progress diagnostics so users can see what just finished and how long it took.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPluginProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [plugin] load_progress plugin='dnsclientx' phase='end' index='3' total='12' elapsed_ms='99'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... loaded tool packs 3/12 (dnsclientx, 99ms)", statusText);
    }

    /// <summary>
    /// Parses summarized plugin progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPluginProgressSummary() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] plugin load progress: processed 8/15 plugin folders (begin=15, end=8).",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... plugin folder scan 8/15", statusText);
    }

    /// <summary>
    /// Parses tooling bootstrap timing diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesBootstrapTiming() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] tooling bootstrap timings total=1.8s policy=50ms options=20ms packs=1.6s registry=120ms tools=200 packsLoaded=14 packsDisabled=2 pluginRoots=3.",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... tool bootstrap finished (1.8s), finalizing runtime connection", statusText);
    }

    /// <summary>
    /// Rejects unrelated service output lines.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ReturnsFalseForUnrelatedLines() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "Client connected.",
            out var statusText);

        Assert.False(parsed);
        Assert.Equal(string.Empty, statusText);
    }

    /// <summary>
    /// Publishes bootstrap progress while disconnected, and while connected only when startup metadata sync is still active.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, true, false, false, true, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    public void ShouldPublishServiceBootstrapStatus_ReturnsExpectedValue(
        bool shutdownRequested,
        bool isConnected,
        bool isSending,
        bool turnStartupInProgress,
        bool startupMetadataSyncInProgress,
        bool expected) {
        var shouldPublish = MainWindow.ShouldPublishServiceBootstrapStatus(
            shutdownRequested,
            isConnected,
            isSending,
            turnStartupInProgress,
            startupMetadataSyncInProgress);

        Assert.Equal(expected, shouldPublish);
    }

    /// <summary>
    /// Allows provider-connect progress updates during active send when send-override is requested.
    /// </summary>
    [Fact]
    public void ShouldPublishServiceBootstrapStatus_AllowsSendOverrideForProviderConnectProgress() {
        var shouldPublish = MainWindow.ShouldPublishServiceBootstrapStatus(
            shutdownRequested: false,
            isConnected: true,
            isSending: true,
            turnStartupInProgress: false,
            startupMetadataSyncInProgress: false,
            allowDuringSend: true);

        Assert.True(shouldPublish);
    }

    /// <summary>
    /// Keeps startup pack progress visible even while a turn-startup path is active,
    /// as long as the status source is marked send-safe.
    /// </summary>
    [Fact]
    public void ShouldPublishServiceBootstrapStatus_AllowsSendOverrideDuringTurnStartup() {
        var shouldPublish = MainWindow.ShouldPublishServiceBootstrapStatus(
            shutdownRequested: false,
            isConnected: true,
            isSending: true,
            turnStartupInProgress: true,
            startupMetadataSyncInProgress: true,
            allowDuringSend: true);

        Assert.True(shouldPublish);
    }

    /// <summary>
    /// Allows send-safe startup statuses to publish while connected even if metadata-sync tracking
    /// has not yet toggled on, preventing transient visibility gaps.
    /// </summary>
    [Fact]
    public void ShouldPublishServiceBootstrapStatus_AllowsSendSafeConnectedStatusWithoutMetadataFlag() {
        var shouldPublish = MainWindow.ShouldPublishServiceBootstrapStatus(
            shutdownRequested: false,
            isConnected: true,
            isSending: false,
            turnStartupInProgress: false,
            startupMetadataSyncInProgress: false,
            allowDuringSend: true);

        Assert.True(shouldPublish);
    }

    /// <summary>
    /// Late send-safe bootstrap lines should not keep forcing startup-loading status once startup scope is finished.
    /// </summary>
    [Fact]
    public void ResolveAllowDuringSendForBootstrapStatus_SuppressesLateConnectedBootstrapUpdatesAfterStartup() {
        var effectiveAllow = MainWindow.ResolveAllowDuringSendForBootstrapStatus(
            isConnected: true,
            startupMetadataSyncInProgress: false,
            startupMetadataSyncQueued: false,
            startupFlowState: 0,
            allowDuringSend: true);

        Assert.False(effectiveAllow);
    }

    /// <summary>
    /// Send-safe bootstrap lines remain visible while startup flow is still actively running.
    /// </summary>
    [Fact]
    public void ResolveAllowDuringSendForBootstrapStatus_KeepsConnectedBootstrapUpdatesWhileStartupFlowRunning() {
        var effectiveAllow = MainWindow.ResolveAllowDuringSendForBootstrapStatus(
            isConnected: true,
            startupMetadataSyncInProgress: false,
            startupMetadataSyncQueued: false,
            startupFlowState: 1,
            allowDuringSend: true);

        Assert.True(effectiveAllow);
    }

    /// <summary>
    /// Detached mode keeps the owned sidecar alive across window close so the next launch can reuse warm runtime state.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldStopOwnedServiceOnWindowClose_UsesDetachedModePolicy(
        bool detachedServiceMode,
        bool expected) {
        var shouldStop = MainWindow.ShouldStopOwnedServiceOnWindowClose(detachedServiceMode);

        Assert.Equal(expected, shouldStop);
    }

    /// <summary>
    /// When runtime is already connected, rewrites startup-prefixed bootstrap text into connected wording and tags metadata-sync cause.
    /// </summary>
    [Fact]
    public void BuildConnectedBootstrapStatusText_RewritesStartingRuntimePrefix_AndAppendsCause() {
        var statusText = MainWindow.BuildConnectedBootstrapStatusText(
            "Starting runtime... loading tool packs 3/12 (dnsclientx)",
            MainWindow.StartupStatusCauseMetadataSync);

        Assert.Equal(
            "Runtime connected. Loading tool packs 3/12 (dnsclientx) (phase startup_metadata_sync, cause metadata_sync)",
            statusText);
    }

    /// <summary>
    /// Keeps existing cause suffix stable to avoid duplicate cause markers in status-chip text.
    /// </summary>
    [Fact]
    public void BuildConnectedBootstrapStatusText_DoesNotDuplicateCauseSuffix() {
        var statusText = MainWindow.BuildConnectedBootstrapStatusText(
            "Runtime connected. Loading tool packs in background... (cause metadata_sync)",
            MainWindow.StartupStatusCauseMetadataSync);

        Assert.Equal(
            "Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)",
            statusText);
    }

    /// <summary>
    /// Keeps existing structured startup phase/cause context stable to avoid duplicate context markers.
    /// </summary>
    [Fact]
    public void BuildConnectedBootstrapStatusText_DoesNotDuplicatePhaseAndCauseContext() {
        var statusText = MainWindow.BuildConnectedBootstrapStatusText(
            "Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)",
            MainWindow.StartupStatusCauseMetadataSync);

        Assert.Equal(
            "Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)",
            statusText);
    }

    /// <summary>
    /// Final bootstrap-summary lines should not keep startup phase context active once runtime is already connected.
    /// </summary>
    [Fact]
    public void BuildConnectedBootstrapStatusText_DoesNotAnnotateFinalizingBootstrapSummaryAsStartupPhase() {
        var statusText = MainWindow.BuildConnectedBootstrapStatusText(
            "Starting runtime... tool bootstrap finished (1.8s), finalizing runtime connection",
            MainWindow.StartupStatusCauseMetadataSync);

        Assert.Equal(
            "Runtime connected. Tool bootstrap finished (1.8s), finalizing runtime connection",
            statusText);
    }
}
