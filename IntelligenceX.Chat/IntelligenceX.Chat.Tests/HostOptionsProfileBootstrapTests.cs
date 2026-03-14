using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Verifies host REPL option bootstrap/profile behavior for safety-critical flags.
/// </summary>
public sealed class HostOptionsProfileBootstrapTests {
    [Fact]
    public void Parse_DefaultsToParallelToolCallsEnabled() {
        var options = ParseHostOptions(Array.Empty<string>(), out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.True(ReadBoolProperty(options!, "ParallelToolCalls"));
        Assert.True(ReadBoolProperty(options!, "RequireExplicitRoutingMetadata"));
    }

    [Fact]
    public void Parse_NoParallelTools_DisablesParallelExecution() {
        var options = ParseHostOptions(new[] { "--no-parallel-tools" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.False(ReadBoolProperty(options!, "ParallelToolCalls"));
    }

    [Fact]
    public void Parse_MaxToolRoundsAcceptsUpperBoundary() {
        var options = ParseHostOptions(
            new[] { "--max-tool-rounds", ChatRequestOptionLimits.MaxToolRounds.ToString() },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(ChatRequestOptionLimits.MaxToolRounds, ReadIntProperty(options!, "MaxToolRounds"));
    }

    [Fact]
    public void Parse_MaxToolRoundsRejectsOverSafetyLimit() {
        _ = ParseHostOptions(
            new[] { "--max-tool-rounds", (ChatRequestOptionLimits.MaxToolRounds + 1).ToString() },
            out var error);

        Assert.Equal(
            $"--max-tool-rounds must be between {ChatRequestOptionLimits.MinToolRounds} and {ChatRequestOptionLimits.MaxToolRounds}.",
            error);
    }

    [Fact]
    public void WriteHelp_MaxToolRoundsLineUsesSharedBoundsAndDefault() {
        var helpLine = InvokeHostBuildMaxToolRoundsHelpLine();

        Assert.Equal(
            $"--max-tool-rounds <N>   Max tool-call rounds per user message ({ChatRequestOptionLimits.MinToolRounds}..{ChatRequestOptionLimits.MaxToolRounds}; default: {ChatRequestOptionLimits.DefaultToolRounds}).",
            helpLine.TrimStart());
    }

    [Fact]
    public void ApplyProfile_PropagatesMutatingParallelOverride() {
        var options = CreateHostOptionsInstance();
        Assert.NotNull(options);

        var replOptionsType = options!.GetType();
        var applyProfile = replOptionsType.GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(applyProfile);

        var profile = new ServiceProfile {
            AllowMutatingParallelToolCalls = true
        };

        applyProfile!.Invoke(options, new object?[] { profile });
        Assert.True(ReadBoolProperty(options, "AllowMutatingParallelToolCalls"));
    }

    [Fact]
    public void ApplyProfile_PropagatesExplicitRoutingMetadataRequirement() {
        var options = CreateHostOptionsInstance();
        Assert.NotNull(options);

        var replOptionsType = options!.GetType();
        var applyProfile = replOptionsType.GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(applyProfile);

        var profile = new ServiceProfile {
            RequireExplicitRoutingMetadata = true
        };

        applyProfile!.Invoke(options, new object?[] { profile });
        Assert.True(ReadBoolProperty(options, "RequireExplicitRoutingMetadata"));
    }

    [Fact]
    public void ApplyProfile_PropagatesPackToggleLists() {
        var options = CreateHostOptionsInstance();
        Assert.NotNull(options);

        var replOptionsType = options!.GetType();
        var applyProfile = replOptionsType.GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(applyProfile);

        var profile = new ServiceProfile {
            DisabledPackIds = new List<string> { "custom_plugin_pack" },
            EnabledPackIds = new List<string> { "powershell" }
        };

        applyProfile!.Invoke(options, new object?[] { profile });
        Assert.Contains("custom_plugin_pack", ReadStringListProperty(options, "DisabledPackIds"));
        Assert.Contains("powershell", ReadStringListProperty(options, "EnabledPackIds"));
    }

    [Fact]
    public void ApplyProfile_PropagatesBuiltInAssemblyDiscoverySettings() {
        var options = CreateHostOptionsInstance();
        Assert.NotNull(options);

        var replOptionsType = options!.GetType();
        var applyProfile = replOptionsType.GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(applyProfile);

        var profile = new ServiceProfile {
            UseDefaultBuiltInToolAssemblyNames = false,
            BuiltInToolAssemblyNames = new List<string> { "IntelligenceX.Tools.System", "IntelligenceX.Tools.EventLog" }
        };

        applyProfile!.Invoke(options, new object?[] { profile });
        Assert.False(ReadBoolProperty(options, "UseDefaultBuiltInToolAssemblyNames"));
        var assemblyNames = ReadStringListProperty(options, "BuiltInToolAssemblyNames");
        Assert.Contains("IntelligenceX.Tools.System", assemblyNames);
        Assert.Contains("IntelligenceX.Tools.EventLog", assemblyNames);
    }

    [Fact]
    public void Parse_ProfileDefaultMutatingParallelTrue_IsAppliedWithoutCliOverride() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "default", allowMutatingParallel: true);
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "default" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.True(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_CliDisableMutatingParallel_OverridesProfileDefaultTrue() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "default", allowMutatingParallel: true);
            var options = ParseHostOptions(
                new[] { "--state-db", dbPath, "--profile", "default", "--disallow-mutating-parallel-tools" },
                out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.False(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_CliEnableMutatingParallel_OverridesProfileDefaultFalse() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "default", allowMutatingParallel: false);
            var options = ParseHostOptions(
                new[] { "--state-db", dbPath, "--profile", "default", "--allow-mutating-parallel-tools" },
                out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.True(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_CliMutatingParallelFlags_EnableThenDisable_LastFlagWins() {
        var options = ParseHostOptions(new[] { "--allow-mutating-parallel-tools", "--disallow-mutating-parallel-tools" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.False(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
    }

    [Fact]
    public void Parse_CliMutatingParallelFlags_DisableThenEnable_LastFlagWins() {
        var options = ParseHostOptions(new[] { "--disallow-mutating-parallel-tools", "--allow-mutating-parallel-tools" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.True(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
    }

    [Fact]
    public void Parse_CliBuiltInPackFlags_DisableThenEnable_LastFlagWins() {
        var options = ParseHostOptions(new[] { "--no-built-in-packs", "--built-in-packs" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.True(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
    }

    [Fact]
    public void Parse_CliNoBuiltInPacks_DisablesBuiltInPackLoading() {
        var options = ParseHostOptions(new[] { "--no-built-in-packs" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.False(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
    }

    [Fact]
    public void Parse_LoadsBuiltInPluginOnlyPreset_WhenNoStoredProfileExists() {
        var dbPath = CreateTempProfileDbPath();
        try {
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "plugin-only" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", ReadStringProperty(options!, "ProfileName"));
            Assert.False(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
            Assert.True(ReadBoolProperty(options!, "EnableDefaultPluginPaths"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsSavedProfileNamedPluginOnly_BeforeBuiltInPreset() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(
                dbPath,
                "plugin-only",
                allowMutatingParallel: false,
                model: "saved-plugin-model",
                enableBuiltInPackLoading: true,
                enableDefaultPluginPaths: false);
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "plugin-only" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", ReadStringProperty(options!, "ProfileName"));
            Assert.Equal("saved-plugin-model", ReadStringProperty(options!, "Model"));
            Assert.True(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
            Assert.False(ReadBoolProperty(options!, "EnableDefaultPluginPaths"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsSavedProfileNamedPluginOnlyAlias_BeforeBuiltInPresetAlias() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(
                dbPath,
                "plugin_only",
                allowMutatingParallel: false,
                model: "saved-plugin-alias-model",
                enableBuiltInPackLoading: true,
                enableDefaultPluginPaths: false);
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "plugin_only" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin_only", ReadStringProperty(options!, "ProfileName"));
            Assert.Equal("saved-plugin-alias-model", ReadStringProperty(options!, "Model"));
            Assert.True(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
            Assert.False(ReadBoolProperty(options!, "EnableDefaultPluginPaths"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_NormalizesBuiltInPluginOnlyPresetAlias() {
        var dbPath = CreateTempProfileDbPath();
        try {
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "plugin_only" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", ReadStringProperty(options!, "ProfileName"));
            Assert.False(ReadBoolProperty(options!, "EnableBuiltInPackLoading"));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_BuiltInToolAssemblyFlags_AreApplied() {
        var options = ParseHostOptions(
            new[] {
                "--no-default-built-in-tool-assemblies",
                "--built-in-tool-assembly", "IntelligenceX.Tools.System",
                "--built-in-tool-assembly", "IntelligenceX.Tools.EventLog"
            },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.False(ReadBoolProperty(options!, "UseDefaultBuiltInToolAssemblyNames"));
        var assemblyNames = ReadStringListProperty(options, "BuiltInToolAssemblyNames");
        Assert.Contains("IntelligenceX.Tools.System", assemblyNames);
        Assert.Contains("IntelligenceX.Tools.EventLog", assemblyNames);
    }

    [Fact]
    public void BuildPacks_AllowsToollessMode_WhenPluginOnlyModeLoadsNoPacks() {
        var options = ParseHostOptions(
            new[] { "--no-built-in-packs", "--no-default-plugin-paths" },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);

        var warnings = new List<string>();
        var packs = InvokeHostBuildPacks(options!, warning => warnings.Add(warning));

        Assert.Empty(packs);
        Assert.Contains(
            warnings,
            static warning => warning.Contains("no_tool_packs_loaded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_CliRequireExplicitRoutingMetadata_SetsFlag() {
        var options = ParseHostOptions(new[] { "--require-explicit-routing-metadata" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.True(ReadBoolProperty(options!, "RequireExplicitRoutingMetadata"));
    }

    [Fact]
    public void Parse_CliAllowInferredRoutingMetadata_AfterRequire_LastFlagWins() {
        var options = ParseHostOptions(
            new[] { "--require-explicit-routing-metadata", "--allow-inferred-routing-metadata" },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.False(ReadBoolProperty(options!, "RequireExplicitRoutingMetadata"));
    }

    [Fact]
    public void Parse_DisablePackId_DisablesKnownPackAndTracksId() {
        var options = ParseHostOptions(new[] { "--disable-pack-id", "testimox" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.Contains("testimox", ReadStringListProperty(options!, "DisabledPackIds"));
        Assert.DoesNotContain("testimox", ReadStringListProperty(options!, "EnabledPackIds"));
    }

    [Fact]
    public void Parse_EnablePackId_AfterDisable_ReEnablesKnownPackAndClearsTrackedId() {
        var options = ParseHostOptions(
            new[] { "--disable-pack-id", "testimox", "--enable-pack-id", "testimox" },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.DoesNotContain("testimox", ReadStringListProperty(options!, "DisabledPackIds"));
        Assert.Contains("testimox", ReadStringListProperty(options!, "EnabledPackIds"));
    }

    [Fact]
    public void Parse_DisablePackId_TracksUnknownPackId() {
        var options = ParseHostOptions(new[] { "--disable-pack-id", "custom_plugin_pack" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.Contains("custom_plugin_pack", ReadStringListProperty(options!, "DisabledPackIds"));
    }

    [Fact]
    public void Parse_ScenarioFileAndOutput_AreApplied() {
        var options = ParseHostOptions(
            new[] { "--scenario-file", "scenario.json", "--scenario-output", "artifacts\\scenario-report.md" },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.Equal("scenario.json", ReadStringProperty(options!, "ScenarioFile"));
        Assert.Equal("artifacts\\scenario-report.md", ReadStringProperty(options!, "ScenarioOutputFile"));
    }

    [Fact]
    public void Parse_ScenarioContinueOnError_SetsFlag() {
        var options = ParseHostOptions(new[] { "--scenario-continue-on-error" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.True(ReadBoolProperty(options!, "ScenarioContinueOnError"));
    }

    private static object? ParseHostOptions(string[] args, out string? error) {
        var replOptionsType = ResolveHostOptionsType();
        var parse = replOptionsType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(parse);

        var parameters = new object?[] { args, null };
        var parsed = parse!.Invoke(null, parameters);
        error = parameters[1] as string;
        return parsed;
    }

    private static object? CreateHostOptionsInstance() {
        var replOptionsType = ResolveHostOptionsType();
        return Activator.CreateInstance(replOptionsType, nonPublic: true);
    }

    private static Type ResolveHostOptionsType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replOptionsType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplOptions", throwOnError: true);
        Assert.NotNull(replOptionsType);
        return replOptionsType!;
    }

    private static bool ReadBoolProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsType<bool>(value);
        return (bool)value!;
    }

    private static int ReadIntProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsType<int>(value);
        return (int)value!;
    }

    private static string? ReadStringProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        return value as string;
    }

    private static IReadOnlyList<string> ReadStringListProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(value);
        return (IReadOnlyList<string>)value!;
    }

    private static string CreateTempProfileDbPath() {
        return TempPathTestHelper.CreateTempFilePath("ix-chat-host-profile-tests", ".db");
    }

    private static string InvokeHostBuildMaxToolRoundsHelpLine() {
        var hostProgramType = ResolveHostProgramType();
        var buildHelpLine = hostProgramType.GetMethod("BuildMaxToolRoundsHelpLine", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildHelpLine);
        var value = buildHelpLine!.Invoke(null, null);
        Assert.IsType<string>(value);
        return (string)value!;
    }

    private static IReadOnlyList<IToolPack> InvokeHostBuildPacks(object options, Action<string>? onBootstrapWarning = null) {
        var hostProgramType = ResolveHostProgramType();

        var buildRuntimePolicyOptions = hostProgramType.GetMethod("BuildRuntimePolicyOptions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildRuntimePolicyOptions);
        var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(buildRuntimePolicyOptions!.Invoke(null, new[] { options }));
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(runtimePolicyOptions);

        var buildPacks = hostProgramType.GetMethod("BuildPacks", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildPacks);
        var packs = buildPacks!.Invoke(null, new object?[] { options, runtimePolicyContext, onBootstrapWarning });
        Assert.IsAssignableFrom<IReadOnlyList<IToolPack>>(packs);
        return (IReadOnlyList<IToolPack>)packs!;
    }

    private static Type ResolveHostProgramType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var hostProgramType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program", throwOnError: true);
        Assert.NotNull(hostProgramType);
        return hostProgramType!;
    }

    private static void SeedProfile(
        string dbPath,
        string profileName,
        bool allowMutatingParallel,
        string model = "profile-model",
        bool enableBuiltInPackLoading = true,
        bool enableDefaultPluginPaths = true) {
        using var store = new SqliteServiceProfileStore(dbPath);
        var profile = new ServiceProfile {
            Model = model,
            AllowMutatingParallelToolCalls = allowMutatingParallel,
            EnableBuiltInPackLoading = enableBuiltInPackLoading,
            EnableDefaultPluginPaths = enableDefaultPluginPaths
        };
        store.UpsertAsync(profileName, profile, CancellationToken.None).GetAwaiter().GetResult();
    }

}
