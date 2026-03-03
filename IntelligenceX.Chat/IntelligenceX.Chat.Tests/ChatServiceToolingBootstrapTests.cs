using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolingBootstrapTests {
    [Fact]
    public void RebuildToolingFromOptions_RefreshesPackAvailabilitySnapshot() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var packAvailabilityField = typeof(ChatServiceSession).GetField("_packAvailability", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(packAvailabilityField);

        var options = new ServiceOptions();
        var session = new ChatServiceSession(options, Stream.Null);

        var initialAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField!.GetValue(session));

        options.DisabledPackIds.Add("officeimo");
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var rebuiltAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField.GetValue(session));
        Assert.NotSame(initialAvailability, rebuiltAvailability);

        var officeImo = Assert.Single(rebuiltAvailability, static item =>
            string.Equals(item.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.False(officeImo.Enabled);
    }

    [Fact]
    public void RebuildToolingFromOptions_CapturesStartupBootstrapPhases() {
        var startupBootstrapField = typeof(ChatServiceSession).GetField("_startupBootstrap", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(startupBootstrapField);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var startupBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(session));

        Assert.Equal(5, startupBootstrap.Phases.Length);
        Assert.Equal("runtime_policy", startupBootstrap.Phases[0].Id);
        Assert.Equal("pack_load", startupBootstrap.Phases[2].Id);
        Assert.Equal("pack_register", startupBootstrap.Phases[3].Id);
        Assert.Equal("registry_finalize", startupBootstrap.Phases[4].Id);
        Assert.True(startupBootstrap.Phases[2].DurationMs >= 1);
        Assert.False(string.IsNullOrWhiteSpace(startupBootstrap.SlowestPhaseId));
        Assert.True(startupBootstrap.SlowestPhaseMs >= 1);
    }

    [Fact]
    public void Constructor_UsesSharedToolingBootstrapCache_WhenProvided() {
        var startupBootstrapField = typeof(ChatServiceSession).GetField("_startupBootstrap", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupWarningsField = typeof(ChatServiceSession).GetField("_startupWarnings", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(startupBootstrapField);
        Assert.NotNull(startupWarningsField);

        var cache = new ChatServiceToolingBootstrapCache();

        var firstSession = new ChatServiceSession(new ServiceOptions(), Stream.Null, cache);
        var firstBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(firstSession));
        Assert.NotEmpty(firstBootstrap.Phases);
        Assert.NotEqual("cache_hit", firstBootstrap.Phases[0].Id);

        var secondSession = new ChatServiceSession(new ServiceOptions(), Stream.Null, cache);
        var secondBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField.GetValue(secondSession));
        Assert.Single(secondBootstrap.Phases);
        Assert.Equal("cache_hit", secondBootstrap.Phases[0].Id);

        var secondWarnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(secondSession));
        Assert.Contains(secondWarnings, static warning => warning.Contains("tooling bootstrap cache hit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildToolingBootstrapCacheKey_IncludesResolvedSmtpProbePolicyDimensions() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyMethod);

        var options = new ServiceOptions();
        var runtimePolicyOptions = new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RequireAuthenticationRuntime = true
        };

        var strictResolved = new ToolRuntimePolicyResolvedOptions {
            Options = runtimePolicyOptions,
            RequireSuccessfulSmtpProbeForSend = true,
            SmtpProbeMaxAgeSeconds = 600
        };
        var relaxedResolved = strictResolved with {
            RequireSuccessfulSmtpProbeForSend = false,
            SmtpProbeMaxAgeSeconds = 60
        };

        var strictKey = Assert.IsType<string>(keyMethod!.Invoke(null, new object?[] { options, runtimePolicyOptions, strictResolved }));
        var relaxedKey = Assert.IsType<string>(keyMethod.Invoke(null, new object?[] { options, runtimePolicyOptions, relaxedResolved }));

        Assert.Contains("require_smtp_probe=1;", strictKey, StringComparison.Ordinal);
        Assert.Contains("smtp_probe_max_age_seconds=600;", strictKey, StringComparison.Ordinal);
        Assert.Contains("require_smtp_probe=0;", relaxedKey, StringComparison.Ordinal);
        Assert.Contains("smtp_probe_max_age_seconds=60;", relaxedKey, StringComparison.Ordinal);
        Assert.NotEqual(strictKey, relaxedKey);
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesAndSortsTopEntries() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] path_not_found path='C:\\plugins\\missing'",
            "[plugin] load_timing plugin='delta' elapsed_ms='650' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='alpha' elapsed_ms='1400' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='beta' elapsed_ms='900' entry_assemblies='1' candidate_types='1' loaded='0' disabled='1' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='gamma' elapsed_ms='1200' entry_assemblies='1' candidate_types='1' loaded='0' disabled='0' duplicate='0' failed='1'",
            "[plugin] load_timing plugin='alpha' elapsed_ms='1100' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(warnings, static w => w.StartsWith("[plugin] load_timing", StringComparison.OrdinalIgnoreCase));

        var summary = Assert.Single(warnings, static w => w.StartsWith("[startup] slow plugin loads top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("alpha=1400ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gamma=1200ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta=900ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delta=650ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(warnings, static w => w.StartsWith("[startup] additional slow plugins omitted: 1.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_NoTimingWarnings_LeavesCollectionUntouched() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] path_not_found path='C:\\plugins\\missing'",
            "[plugin] init_failed plugin='alpha' error='missing dep'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Equal(2, warnings.Count);
        Assert.Contains("[plugin] path_not_found path='C:\\plugins\\missing'", warnings);
        Assert.Contains("[plugin] init_failed plugin='alpha' error='missing dep'", warnings);
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPluginProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] load_progress plugin='alpha' phase='begin' index='1' total='3'",
            "[plugin] load_progress plugin='alpha' phase='end' index='1' total='3' elapsed_ms='800' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='beta' phase='begin' index='2' total='3'",
            "[plugin] load_progress plugin='beta' phase='end' index='2' total='3' elapsed_ms='300' loaded='0' disabled='1' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='gamma' phase='begin' index='3' total='3'",
            "[plugin] load_progress plugin='gamma' phase='end' index='3' total='3' elapsed_ms='400' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[plugin] load_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings,
            static w => w.StartsWith("[startup] plugin load progress: processed 3/3 plugin folders (begin=3, end=3).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPackProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_load_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_load_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='120' failed='0'",
            "[startup] pack_load_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_load_progress pack='active_directory' phase='end' index='2' total='3' elapsed_ms='1400' failed='0'",
            "[startup] pack_load_progress pack='plugins' phase='begin' index='3' total='3'",
            "[startup] pack_load_progress pack='plugins' phase='end' index='3' total='3' elapsed_ms='900' failed='1'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[startup] pack_load_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[startup] pack load progress: processed 3/3 bootstrap steps (begin=3, end=3).", StringComparison.OrdinalIgnoreCase));

        var slowPacks = Assert.Single(warnings, static w => w.StartsWith("[startup] slow pack loads top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("active_directory=1400ms", slowPacks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugins=900ms", slowPacks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_PluginProcessedProgress_UsesCompletedEndEvents() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] load_progress plugin='alpha' phase='begin' index='1' total='3'",
            "[plugin] load_progress plugin='alpha' phase='end' index='1' total='3' elapsed_ms='100' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='beta' phase='begin' index='2' total='3'",
            "[plugin] load_progress plugin='beta' phase='end' index='2' total='3' elapsed_ms='120' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='gamma' phase='begin' index='3' total='3'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(
            warnings,
            static w => w.StartsWith("[startup] plugin load progress: processed 2/3 plugin folders (begin=3, end=2).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_PackProcessedProgress_UsesCompletedEndEvents() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_load_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_load_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='120' failed='0'",
            "[startup] pack_load_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_load_progress pack='plugins' phase='begin' index='3' total='3'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(
            warnings,
            static w => w.StartsWith("[startup] pack load progress: processed 1/3 bootstrap steps (begin=3, end=1).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPackRegistrationProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_register_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_register_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='1200' tools_registered='10' total_tools='10' failed='0'",
            "[startup] pack_register_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_register_progress pack='active_directory' phase='end' index='2' total='3' elapsed_ms='220' tools_registered='14' total_tools='24' failed='0'",
            "[startup] pack_register_progress pack='plugins' phase='begin' index='3' total='3'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[startup] pack_register_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[startup] pack registration progress: processed 2/3 packs (begin=3, end=2).", StringComparison.OrdinalIgnoreCase));

        var slowRegistrations = Assert.Single(warnings, static w => w.StartsWith("[startup] slow pack registrations top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("eventlog=1200ms", slowRegistrations, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tools=10", slowRegistrations, StringComparison.OrdinalIgnoreCase);
    }
}
