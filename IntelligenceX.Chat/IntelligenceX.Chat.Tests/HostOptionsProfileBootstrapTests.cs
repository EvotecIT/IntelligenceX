using System;
using System.IO;
using System.Reflection;
using System.Threading;
using IntelligenceX.Chat.Profiles;
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
        var options = ParseHostOptions(new[] { "--max-tool-rounds", "256" }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(256, ReadIntProperty(options!, "MaxToolRounds"));
    }

    [Fact]
    public void Parse_MaxToolRoundsRejectsOverSafetyLimit() {
        _ = ParseHostOptions(new[] { "--max-tool-rounds", "257" }, out var error);

        Assert.Equal("--max-tool-rounds must be between 1 and 256.", error);
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
    public void Parse_ProfileDefaultMutatingParallelTrue_IsAppliedWithoutCliOverride() {
        var dbPath = CreateTempProfileDbPath();
        try {
            SeedProfile(dbPath, "default", allowMutatingParallel: true);
            var options = ParseHostOptions(new[] { "--state-db", dbPath, "--profile", "default" }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.True(ReadBoolProperty(options!, "AllowMutatingParallelToolCalls"));
        } finally {
            TryDelete(dbPath);
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
            TryDelete(dbPath);
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
            TryDelete(dbPath);
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

    private static string CreateTempProfileDbPath() {
        return Path.Combine(Path.GetTempPath(), "ix-chat-host-profile-tests-" + Guid.NewGuid().ToString("N") + ".db");
    }

    private static void SeedProfile(string dbPath, string profileName, bool allowMutatingParallel) {
        using var store = new SqliteServiceProfileStore(dbPath);
        var profile = new ServiceProfile {
            Model = "profile-model",
            AllowMutatingParallelToolCalls = allowMutatingParallel
        };
        store.UpsertAsync(profileName, profile, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup for temp profile DB files.
        }
    }
}
