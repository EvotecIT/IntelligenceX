using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupBootstrapWarningBuilderTests {
    [Fact]
    public void BuildTimingSummary_EmitsCanonicalBootstrapTimingEnvelope() {
        var warning = StartupBootstrapWarningBuilder.BuildTimingSummary(
            total: "1.8s",
            policy: "50ms",
            options: "20ms",
            packs: "1.6s",
            register: "200ms",
            finalize: "120ms",
            registry: "320ms",
            tools: 200,
            packsLoaded: 14,
            packsDisabled: 2,
            pluginRoots: 3);

        Assert.Equal(
            "[startup] tooling bootstrap timings total=1.8s policy=50ms options=20ms descriptorDiscovery=1.6s packActivation=200ms activationFinalize=120ms registry=320ms tools=200 packsLoaded=14 packsDisabled=2 pluginRoots=3.",
            warning);
    }

    [Fact]
    public void BuildPluginLoadProgressSummary_EmitsCanonicalProgressEnvelope() {
        var warning = StartupBootstrapWarningBuilder.BuildPluginLoadProgressSummary(
            processed: 8,
            total: 15,
            beginCount: 15,
            endCount: 8);

        Assert.Equal(
            "[startup] plugin load progress: processed 8/15 plugin folders (begin=15, end=8).",
            warning);
    }

    [Fact]
    public void BuildSlowPackRegistrationsTop_EmitsCanonicalTopSummary() {
        var warning = StartupBootstrapWarningBuilder.BuildSlowPackRegistrationsTop(
            topCount: 1,
            totalCount: 2,
            segments: new[] { "eventlog=1200ms (tools=10)" });

        Assert.Equal(
            "[startup] slow pack registrations top 1/2: eventlog=1200ms (tools=10)",
            warning);
    }

    [Fact]
    public void BuildCacheHitSummary_EmitsCanonicalCacheHitEnvelope() {
        var warning = StartupBootstrapWarningBuilder.BuildCacheHitSummary(
            elapsedMs: 42,
            tools: 187,
            packsLoaded: 10);

        Assert.Equal(
            "[startup] tooling bootstrap cache hit elapsed=42ms tools=187 packsLoaded=10.",
            warning);
    }

    [Fact]
    public void BuildPersistedPreviewRestoredSummary_EmitsCanonicalPreviewEnvelope() {
        var warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewRestoredSummary();

        Assert.Equal(
            "[startup] tooling bootstrap preview restored from persisted cache while runtime rebuild continues.",
            warning);
    }

    [Fact]
    public void BuildPersistedPreviewIgnoredSummary_EmitsCanonicalSchemaMismatchEnvelope() {
        var warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary(
            reason: "schema_mismatch",
            expectedSchemaVersion: 4,
            actualSchemaVersion: 3);

        Assert.Equal(
            "[startup] tooling bootstrap persisted preview ignored reason=schema_mismatch expectedSchemaVersion=4 actualSchemaVersion=3.",
            warning);
    }
}
