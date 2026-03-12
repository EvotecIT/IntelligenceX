using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupBootstrapWarningFormatterTests {
    [Fact]
    public void TryBuildStatusText_ParsesPackProgressBeginWithPackWarningPrefix() {
        var parsed = StartupBootstrapWarningFormatter.TryBuildStatusText(
            "[pack warning] [startup] pack_load_progress pack='eventlog' phase='begin' index='2' total='11'",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... initializing tool packs 2/11 (eventlog)", statusText);
        Assert.True(allowDuringSend);
    }

    [Fact]
    public void TryBuildStatusText_ParsesProviderConnectEnd() {
        var parsed = StartupBootstrapWarningFormatter.TryBuildStatusText(
            "[startup] provider_connect_progress phase='end' operation='connect_client' transport='native' status='ok' elapsed_ms='3120'",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... connected runtime provider (native, 3120ms)", statusText);
        Assert.True(allowDuringSend);
    }

    [Fact]
    public void TryBuildStatusText_ParsesBootstrapTimingSummary() {
        var parsed = StartupBootstrapWarningFormatter.TryBuildStatusText(
            "[pack warning] [startup] tooling bootstrap timings total=1.8s policy=50ms options=20ms packs=1.6s registry=120ms tools=200 packsLoaded=14 packsDisabled=2 pluginRoots=3.",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... tool bootstrap finished (1.8s), finalizing runtime connection", statusText);
        Assert.True(allowDuringSend);
    }

    [Fact]
    public void TryBuildStatusText_ParsesCacheHitSummary() {
        var parsed = StartupBootstrapWarningFormatter.TryBuildStatusText(
            "[startup] tooling bootstrap cache hit elapsed=42ms tools=187 packsLoaded=10.",
            out var statusText,
            out var allowDuringSend);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... reused tooling bootstrap cache (42ms), finalizing runtime connection", statusText);
        Assert.True(allowDuringSend);
    }

    [Fact]
    public void IsPersistedPreviewRestoredWarning_MatchesCanonicalEnvelope() {
        var matched = StartupBootstrapWarningFormatter.IsPersistedPreviewRestoredWarning(
            "[startup] tooling bootstrap preview restored from persisted cache while runtime rebuild continues.");

        Assert.True(matched);
    }
}
