using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DBAClientX;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers profile bootstrap semantics for service startup argument parsing.
/// </summary>
public sealed partial class ServiceOptionsProfileBootstrapTests {
    private static readonly object EnvironmentVariablesSync = new();

    [Fact]
    public void Parse_DefaultsToNoResponseShapingLimits() {
        var options = ServiceOptions.Parse(Array.Empty<string>(), out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0, options.MaxTableRows);
        Assert.Equal(0, options.MaxSample);
        Assert.False(options.AllowMutatingParallelToolCalls);
        Assert.True(options.RequireExplicitRoutingMetadata);
    }

    [Fact]
    public void Parse_AppliesExecutionLaneOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--session-execution-queue-limit", "128",
            "--global-execution-lane-concurrency", "4"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(128, options.SessionExecutionQueueLimit);
        Assert.Equal(4, options.GlobalExecutionLaneConcurrency);
    }

    [Fact]
    public void Parse_AppliesBackgroundSchedulerDaemonOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--background-scheduler-daemon",
            "--background-scheduler-start-paused-seconds", "90",
            "--background-scheduler-maintenance-window", "mon@02:00/60",
            "--background-scheduler-maintenance-window", "daily@23:30/120",
            "--background-scheduler-poll-seconds", "45",
            "--background-scheduler-burst-limit", "6",
            "--background-scheduler-failure-threshold", "7",
            "--background-scheduler-failure-pause-seconds", "120",
            "--background-scheduler-allow-pack-id", "system",
            "--background-scheduler-block-pack-id", "active_directory",
            "--background-scheduler-allow-thread-id", "thread-system",
            "--background-scheduler-block-thread-id", "thread-active-directory"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.True(options.EnableBackgroundSchedulerDaemon);
        Assert.True(options.BackgroundSchedulerStartPaused);
        Assert.Equal(90, options.BackgroundSchedulerStartupPauseSeconds);
        Assert.Equal(new[] { "mon@02:00/60", "daily@23:30/120" }, options.BackgroundSchedulerMaintenanceWindows);
        Assert.Equal(45, options.BackgroundSchedulerPollSeconds);
        Assert.Equal(6, options.BackgroundSchedulerBurstLimit);
        Assert.Equal(7, options.BackgroundSchedulerFailureThreshold);
        Assert.Equal(120, options.BackgroundSchedulerFailurePauseSeconds);
        Assert.Contains("system", options.BackgroundSchedulerAllowedPackIds);
        Assert.Contains("active_directory", options.BackgroundSchedulerBlockedPackIds);
        Assert.Contains("thread-system", options.BackgroundSchedulerAllowedThreadIds);
        Assert.Contains("thread-active-directory", options.BackgroundSchedulerBlockedThreadIds);
    }

    [Fact]
    public void Parse_AppliesBackgroundSchedulerPackFilterPrecedence() {
        var options = ServiceOptions.Parse(new[] {
            "--background-scheduler-allow-pack-id", "system",
            "--background-scheduler-allow-pack-id", "active_directory",
            "--background-scheduler-block-pack-id", "active_directory"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("system", options.BackgroundSchedulerAllowedPackIds);
        Assert.DoesNotContain("active_directory", options.BackgroundSchedulerAllowedPackIds);
        Assert.Contains("active_directory", options.BackgroundSchedulerBlockedPackIds);
    }

    [Fact]
    public void Parse_AppliesBackgroundSchedulerThreadFilterPrecedence() {
        var options = ServiceOptions.Parse(new[] {
            "--background-scheduler-allow-thread-id", "thread-system",
            "--background-scheduler-allow-thread-id", "thread-active-directory",
            "--background-scheduler-block-thread-id", "thread-active-directory"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("thread-system", options.BackgroundSchedulerAllowedThreadIds);
        Assert.DoesNotContain("thread-active-directory", options.BackgroundSchedulerAllowedThreadIds);
        Assert.Contains("thread-active-directory", options.BackgroundSchedulerBlockedThreadIds);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerPollSeconds() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-poll-seconds", "0"
        }, out var error);

        Assert.Equal("--background-scheduler-poll-seconds must be between 1 and 3600.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerBurstLimit() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-burst-limit", "64"
        }, out var error);

        Assert.Equal("--background-scheduler-burst-limit must be between 1 and 32.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerFailureThreshold() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-failure-threshold", "64"
        }, out var error);

        Assert.Equal("--background-scheduler-failure-threshold must be between 0 and 32.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerFailurePauseSeconds() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-failure-pause-seconds", "0"
        }, out var error);

        Assert.Equal("--background-scheduler-failure-pause-seconds must be between 1 and 3600.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerStartupPauseSeconds() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-start-paused-seconds", "0"
        }, out var error);

        Assert.Equal("--background-scheduler-start-paused-seconds must be between 1 and 3600.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidBackgroundSchedulerMaintenanceWindow() {
        _ = ServiceOptions.Parse(new[] {
            "--background-scheduler-maintenance-window", "weekday@99:00/10"
        }, out var error);

        Assert.Equal("--background-scheduler-maintenance-window must use <day>@HH:mm/<minutes> with day in daily, mon, tue, wed, thu, fri, sat, sun and minutes in 1..1440.", error);
    }

    [Fact]
    public void ApplyProfile_ClampsExecutionLaneValuesToParserBounds() {
        var options = new ServiceOptions();
        var profile = new ServiceProfile {
            SessionExecutionQueueLimit = 99_999,
            GlobalExecutionLaneConcurrency = 99_999
        };

        var method = typeof(ServiceOptions).GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(options, new object[] { profile });

        Assert.Equal(4096, options.SessionExecutionQueueLimit);
        Assert.Equal(512, options.GlobalExecutionLaneConcurrency);
    }

    [Fact]
    public void ToProfile_ClampsExecutionLaneValuesToParserBounds() {
        var options = new ServiceOptions {
            SessionExecutionQueueLimit = 99_999,
            GlobalExecutionLaneConcurrency = 99_999
        };

        var method = typeof(ServiceOptions).GetMethod("ToProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profile = Assert.IsType<ServiceProfile>(method!.Invoke(options, null));

        Assert.Equal(4096, profile.SessionExecutionQueueLimit);
        Assert.Equal(512, profile.GlobalExecutionLaneConcurrency);
    }

    [Fact]
    public void ApplyProfile_ClampsBackgroundSchedulerValuesToParserBounds() {
        var options = new ServiceOptions();
        var profile = new ServiceProfile {
            EnableBackgroundSchedulerDaemon = true,
            BackgroundSchedulerStartPaused = true,
            BackgroundSchedulerStartupPauseSeconds = 99_999,
            BackgroundSchedulerMaintenanceWindows = new List<string> { "monday@02:00/30", "daily@23:30/120" },
            BackgroundSchedulerPollSeconds = 99_999,
            BackgroundSchedulerBurstLimit = 0,
            BackgroundSchedulerFailureThreshold = 99_999,
            BackgroundSchedulerFailurePauseSeconds = 0
        };

        var method = typeof(ServiceOptions).GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(options, new object[] { profile });

        Assert.True(options.EnableBackgroundSchedulerDaemon);
        Assert.True(options.BackgroundSchedulerStartPaused);
        Assert.Equal(3600, options.BackgroundSchedulerStartupPauseSeconds);
        Assert.Equal(new[] { "mon@02:00/30", "daily@23:30/120" }, options.BackgroundSchedulerMaintenanceWindows);
        Assert.Equal(3600, options.BackgroundSchedulerPollSeconds);
        Assert.Equal(1, options.BackgroundSchedulerBurstLimit);
        Assert.Equal(32, options.BackgroundSchedulerFailureThreshold);
        Assert.Equal(1, options.BackgroundSchedulerFailurePauseSeconds);
    }

    [Fact]
    public void ToProfile_ClampsBackgroundSchedulerValuesToParserBounds() {
        var options = new ServiceOptions {
            EnableBackgroundSchedulerDaemon = true,
            BackgroundSchedulerStartPaused = true,
            BackgroundSchedulerStartupPauseSeconds = 99_999,
            BackgroundSchedulerMaintenanceWindows = { "mon@02:00/30", "daily@23:30/120" },
            BackgroundSchedulerPollSeconds = 99_999,
            BackgroundSchedulerBurstLimit = 99_999,
            BackgroundSchedulerFailureThreshold = 99_999,
            BackgroundSchedulerFailurePauseSeconds = 99_999
        };

        var method = typeof(ServiceOptions).GetMethod("ToProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var profile = Assert.IsType<ServiceProfile>(method!.Invoke(options, null));

        Assert.True(profile.EnableBackgroundSchedulerDaemon);
        Assert.True(profile.BackgroundSchedulerStartPaused);
        Assert.Equal(3600, profile.BackgroundSchedulerStartupPauseSeconds);
        Assert.Equal(new[] { "mon@02:00/30", "daily@23:30/120" }, profile.BackgroundSchedulerMaintenanceWindows);
        Assert.Equal(3600, profile.BackgroundSchedulerPollSeconds);
        Assert.Equal(32, profile.BackgroundSchedulerBurstLimit);
        Assert.Equal(32, profile.BackgroundSchedulerFailureThreshold);
        Assert.Equal(3600, profile.BackgroundSchedulerFailurePauseSeconds);
    }

    [Fact]
    public void ApplyProfile_And_ToProfile_PreserveBackgroundSchedulerPackFilters() {
        var options = new ServiceOptions();
        var profile = new ServiceProfile {
            BackgroundSchedulerStartPaused = true,
            BackgroundSchedulerStartupPauseSeconds = 180,
            BackgroundSchedulerMaintenanceWindows = new List<string> { "sun@01:00/180" },
            BackgroundSchedulerAllowedPackIds = new List<string> { "system", "eventlog" },
            BackgroundSchedulerBlockedPackIds = new List<string> { "active_directory" },
            BackgroundSchedulerAllowedThreadIds = new List<string> { "thread-system", "thread-eventlog" },
            BackgroundSchedulerBlockedThreadIds = new List<string> { "thread-active-directory" }
        };

        var applyMethod = typeof(ServiceOptions).GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applyMethod);
        applyMethod!.Invoke(options, new object[] { profile });

        Assert.True(options.BackgroundSchedulerStartPaused);
        Assert.Equal(180, options.BackgroundSchedulerStartupPauseSeconds);
        Assert.Equal(new[] { "sun@01:00/180" }, options.BackgroundSchedulerMaintenanceWindows);
        Assert.Equal(new[] { "system", "eventlog" }, options.BackgroundSchedulerAllowedPackIds);
        Assert.Equal(new[] { "active_directory" }, options.BackgroundSchedulerBlockedPackIds);
        Assert.Equal(new[] { "thread-system", "thread-eventlog" }, options.BackgroundSchedulerAllowedThreadIds);
        Assert.Equal(new[] { "thread-active-directory" }, options.BackgroundSchedulerBlockedThreadIds);

        var toProfileMethod = typeof(ServiceOptions).GetMethod("ToProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(toProfileMethod);
        var roundTrip = Assert.IsType<ServiceProfile>(toProfileMethod!.Invoke(options, null));
        Assert.True(roundTrip.BackgroundSchedulerStartPaused);
        Assert.Equal(180, roundTrip.BackgroundSchedulerStartupPauseSeconds);
        Assert.Equal(new[] { "sun@01:00/180" }, roundTrip.BackgroundSchedulerMaintenanceWindows);
        Assert.Equal(new[] { "system", "eventlog" }, roundTrip.BackgroundSchedulerAllowedPackIds);
        Assert.Equal(new[] { "active_directory" }, roundTrip.BackgroundSchedulerBlockedPackIds);
        Assert.Equal(new[] { "thread-system", "thread-eventlog" }, roundTrip.BackgroundSchedulerAllowedThreadIds);
        Assert.Equal(new[] { "thread-active-directory" }, roundTrip.BackgroundSchedulerBlockedThreadIds);
    }

    [Fact]
    public void Parse_RejectsInvalidSessionExecutionQueueLimit() {
        _ = ServiceOptions.Parse(new[] {
            "--session-execution-queue-limit", "-1"
        }, out var error);

        Assert.Equal("--session-execution-queue-limit must be between 0 and 4096.", error);
    }

    [Fact]
    public void Parse_RejectsInvalidGlobalExecutionLaneConcurrency() {
        _ = ServiceOptions.Parse(new[] {
            "--global-execution-lane-concurrency", "513"
        }, out var error);

        Assert.Equal("--global-execution-lane-concurrency must be between 0 and 512.", error);
    }

    [Fact]
    public void Parse_AppliesExplicitResponseShapingOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--max-table-rows", "25",
            "--max-sample", "12"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(25, options.MaxTableRows);
        Assert.Equal(12, options.MaxSample);
    }

    [Fact]
    public void Parse_AllowsMissingProfile_WhenSaveProfileMatches() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service", ".db");
        try {
            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default",
                "--save-profile", "default"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error));
            Assert.Equal("default", options.ProfileName);
            Assert.Equal("default", options.SaveProfileName);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_RuntimePluginPath_IsAvailableForCurrentRun_ButNotPersistedToProfile() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service", ".db");
        var runtimePluginPath = Path.Combine(
            TempPathTestHelper.CreateTempDirectoryPath("ix-chat-runtime-plugins"),
            "plugins");
        try {
            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default",
                "--save-profile", "default",
                "--plugin-path", runtimePluginPath
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal(new[] { runtimePluginPath }, ((IToolPackRuntimeSettings)options!).PluginPaths);

            var storedPluginPaths = ReadStoredPluginPaths(dbPath, "default");
            Assert.Empty(storedPluginPaths);

            var loaded = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default"
            }, out var loadError);

            Assert.NotNull(loaded);
            Assert.True(string.IsNullOrWhiteSpace(loadError), loadError);
            Assert.Empty(((IToolPackRuntimeSettings)loaded!).PluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_RejectsMissingProfile_WhenNoMatchingSaveProfile() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service", ".db");
        try {
            _ = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default"
            }, out var error);

            Assert.Equal("Profile not found: default", error);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsBuiltInPluginOnlyPreset_WhenNoStoredProfileExists() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-preset", ".db");
        try {
            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "plugin-only"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", options.ProfileName);
            Assert.False(options.EnableBuiltInPackLoading);
            Assert.True(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsSavedProfileNamedPluginOnly_BeforeBuiltInPreset() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-preset-collision", ".db");
        try {
            SeedProfile(dbPath, "plugin-only", "saved-plugin-model", enableBuiltInPackLoading: true, enableDefaultPluginPaths: false);

            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "plugin-only"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", options.ProfileName);
            Assert.Equal("saved-plugin-model", options.Model);
            Assert.True(options.EnableBuiltInPackLoading);
            Assert.False(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsSavedProfileNamedPluginOnlyAlias_BeforeBuiltInPresetAlias() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-preset-alias-collision", ".db");
        try {
            SeedProfile(dbPath, "plugin_only", "saved-plugin-alias-model", enableBuiltInPackLoading: true, enableDefaultPluginPaths: false);

            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "plugin_only"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin_only", options.ProfileName);
            Assert.Equal("saved-plugin-alias-model", options.Model);
            Assert.True(options.EnableBuiltInPackLoading);
            Assert.False(options.EnableDefaultPluginPaths);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_NormalizesBuiltInPluginOnlyPresetAlias() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-preset-alias", ".db");
        try {
            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "plugin_only"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("plugin-only", options.ProfileName);
            Assert.False(options.EnableBuiltInPackLoading);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsBuiltInPluginOnlyPreset_WhenStateDbDisabled() {
        var options = ServiceOptions.Parse(new[] {
            "--pipe", "test.pipe",
            "--no-state-db",
            "--profile", "plugin-only"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.Equal("plugin-only", options.ProfileName);
        Assert.False(options.EnableBuiltInPackLoading);
    }

    [Fact]
    public void Parse_NormalizesBuiltInPluginOnlyPresetAlias_WhenStateDbDisabled() {
        var options = ServiceOptions.Parse(new[] {
            "--pipe", "test.pipe",
            "--no-state-db",
            "--profile", "plugin_only"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.Equal("plugin-only", options.ProfileName);
        Assert.False(options.EnableBuiltInPackLoading);
        Assert.True(options.EnableDefaultPluginPaths);
    }

    [Fact]
    public void Parse_RejectsSavedProfileLookup_WhenStateDbDisabled() {
        _ = ServiceOptions.Parse(new[] {
            "--pipe", "test.pipe",
            "--no-state-db",
            "--profile", "default"
        }, out var error);

        Assert.Equal("State DB is disabled; saved profiles are unavailable.", error);
    }

    [Fact]
    public void Parse_LoadsLegacyProfileSchemaWithoutCompatibleHttpColumns() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-legacy", ".db");
        try {
            SeedLegacyProfileRow(dbPath, profileName: "default", model: "legacy-model");

            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal("default", options.ProfileName);
            Assert.Equal(OpenAICompatibleHttpAuthMode.Bearer, options.OpenAIAuthMode);
            Assert.Contains("testimox", options.DisabledPackIds);
            Assert.Contains("officeimo", options.EnabledPackIds);
            Assert.Contains("powershell", options.EnabledPackIds);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_LoadsLegacyProfileWithOversizedMaxToolRounds_ClampsToSafetyLimit() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service-legacy-rounds", ".db");
        try {
            SeedLegacyProfileRow(
                dbPath,
                profileName: "default",
                model: "legacy-model",
                maxToolRounds: ChatRequestOptionLimits.MaxToolRounds + 244);

            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
            Assert.Equal(ChatRequestOptionLimits.MaxToolRounds, options.MaxToolRounds);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Parse_Allows_Disabling_And_Enabling_OfficeImoPack_ByPackId() {
        var disabled = ServiceOptions.Parse(new[] { "--disable-pack-id", "officeimo" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.Contains("officeimo", disabled.DisabledPackIds);
        Assert.DoesNotContain("officeimo", disabled.EnabledPackIds);

        var enabled = ServiceOptions.Parse(new[] { "--disable-pack-id", "officeimo", "--enable-pack-id", "officeimo" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.DoesNotContain("officeimo", enabled.DisabledPackIds);
        Assert.Contains("officeimo", enabled.EnabledPackIds);
    }

    [Fact]
    public void Parse_Allows_Disabling_And_Enabling_DnsAndDomainDetectivePacks_ByPackId() {
        var disabledDns = ServiceOptions.Parse(new[] { "--disable-pack-id", "dnsclientx" }, out var disabledDnsError);
        Assert.True(string.IsNullOrWhiteSpace(disabledDnsError));
        Assert.Contains("dnsclientx", disabledDns.DisabledPackIds);
        Assert.DoesNotContain("dnsclientx", disabledDns.EnabledPackIds);

        var enabledDns = ServiceOptions.Parse(new[] { "--disable-pack-id", "dnsclientx", "--enable-pack-id", "dnsclientx" }, out var enabledDnsError);
        Assert.True(string.IsNullOrWhiteSpace(enabledDnsError));
        Assert.DoesNotContain("dnsclientx", enabledDns.DisabledPackIds);
        Assert.Contains("dnsclientx", enabledDns.EnabledPackIds);

        var disabledDomainDetective = ServiceOptions.Parse(new[] { "--disable-pack-id", "domaindetective" }, out var disabledDomainDetectiveError);
        Assert.True(string.IsNullOrWhiteSpace(disabledDomainDetectiveError));
        Assert.Contains("domaindetective", disabledDomainDetective.DisabledPackIds);
        Assert.DoesNotContain("domaindetective", disabledDomainDetective.EnabledPackIds);

        var enabledDomainDetective = ServiceOptions.Parse(
            new[] { "--disable-pack-id", "domaindetective", "--enable-pack-id", "domaindetective" },
            out var enabledDomainDetectiveError);
        Assert.True(string.IsNullOrWhiteSpace(enabledDomainDetectiveError));
        Assert.DoesNotContain("domaindetective", enabledDomainDetective.DisabledPackIds);
        Assert.Contains("domaindetective", enabledDomainDetective.EnabledPackIds);
    }

    [Fact]
    public void Parse_Allows_Disabling_And_Enabling_PowerShellPack_ByPackId() {
        var enabled = ServiceOptions.Parse(new[] { "--enable-pack-id", "powershell" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.Contains("powershell", enabled.EnabledPackIds);
        Assert.DoesNotContain("powershell", enabled.DisabledPackIds);

        var disabled = ServiceOptions.Parse(new[] { "--enable-pack-id", "powershell", "--disable-pack-id", "powershell" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.DoesNotContain("powershell", disabled.EnabledPackIds);
        Assert.Contains("powershell", disabled.DisabledPackIds);
    }

    [Fact]
    public void Parse_Disables_UnknownPack_ByPackId() {
        var options = ServiceOptions.Parse(new[] { "--disable-pack-id", "custom_plugin_pack" }, out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("custom_plugin_pack", options.DisabledPackIds);
    }

    [Fact]
    public void Parse_Allows_Toggling_MutatingParallelOverride() {
        var enabled = ServiceOptions.Parse(new[] { "--allow-mutating-parallel-tools" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.True(enabled.AllowMutatingParallelToolCalls);

        var disabled = ServiceOptions.Parse(new[] { "--allow-mutating-parallel-tools", "--disallow-mutating-parallel-tools" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.False(disabled.AllowMutatingParallelToolCalls);
    }

    [Fact]
    public void Parse_Allows_Toggling_BuiltInPackLoading() {
        var disabled = ServiceOptions.Parse(new[] { "--no-built-in-packs" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.False(disabled.EnableBuiltInPackLoading);

        var enabled = ServiceOptions.Parse(new[] { "--no-built-in-packs", "--built-in-packs" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.True(enabled.EnableBuiltInPackLoading);
    }

    [Fact]
    public void Parse_Applies_BuiltInToolAssemblyDiscoveryOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--no-default-built-in-tool-assemblies",
            "--built-in-tool-assembly", "IntelligenceX.Tools.System",
            "--built-in-tool-assembly", "IntelligenceX.Tools.EventLog"
        }, out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.False(options.UseDefaultBuiltInToolAssemblyNames);
        Assert.Contains("IntelligenceX.Tools.System", options.BuiltInToolAssemblyNames);
        Assert.Contains("IntelligenceX.Tools.EventLog", options.BuiltInToolAssemblyNames);
    }

    [Fact]
    public void Parse_Applies_BuiltInToolProbePaths_AsRuntimeOnlyOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--built-in-tool-probe-path", "C:\\tools\\a",
            "--built-in-tool-probe-path", "D:\\shared\\tools"
        }, out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(new[] { "C:\\tools\\a", "D:\\shared\\tools" }, options.BuiltInToolProbePaths);
    }

    [Fact]
    public void Parse_Applies_WorkspaceBuiltInToolOutputProbing_AsRuntimeOnlyOverride() {
        var options = ServiceOptions.Parse(new[] {
            "--enable-workspace-built-in-tool-output-probing"
        }, out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.True(options.EnableWorkspaceBuiltInToolOutputProbing);
    }

    [Fact]
    public void Parse_AcceptsMaxToolRoundsUpperBoundary() {
        var options = ServiceOptions.Parse(
            new[] { "--max-tool-rounds", ChatRequestOptionLimits.MaxToolRounds.ToString() },
            out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(ChatRequestOptionLimits.MaxToolRounds, options.MaxToolRounds);
    }

    [Fact]
    public void Parse_RejectsMaxToolRoundsOverSafetyLimit() {
        _ = ServiceOptions.Parse(
            new[] { "--max-tool-rounds", (ChatRequestOptionLimits.MaxToolRounds + 1).ToString() },
            out var error);
        Assert.Equal(
            $"--max-tool-rounds must be between {ChatRequestOptionLimits.MinToolRounds} and {ChatRequestOptionLimits.MaxToolRounds}.",
            error);
    }

    [Fact]
    public void WriteHelp_MaxToolRoundsLineUsesSharedBoundsAndDefault() {
        Assert.Equal(
            $"--max-tool-rounds <N>   Max tool-call rounds per user message ({ChatRequestOptionLimits.MinToolRounds}..{ChatRequestOptionLimits.MaxToolRounds}; default: {ChatRequestOptionLimits.DefaultToolRounds}).",
            ServiceOptions.BuildMaxToolRoundsHelpLine().TrimStart());
    }

    [Theory]
    [InlineData("copilot-cli")]
    [InlineData("copilot")]
    [InlineData("github-copilot")]
    [InlineData("githubcopilot")]
    public void Parse_AcceptsCopilotTransportAliases(string value) {
        var options = ServiceOptions.Parse(new[] { "--openai-transport", value }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(OpenAITransportKind.CopilotCli, options.OpenAITransport);
    }

    private static void WithTemporaryEnvironmentVariable(string name, string? value, Action action) {
        lock (EnvironmentVariablesSync) {
            var original = Environment.GetEnvironmentVariable(name);
            try {
                Environment.SetEnvironmentVariable(name, value);
                action();
            } finally {
                Environment.SetEnvironmentVariable(name, original);
            }
        }
    }

    private static void SeedProfile(string dbPath, string profileName, string model, bool enableBuiltInPackLoading, bool enableDefaultPluginPaths) {
        using var store = new SqliteServiceProfileStore(dbPath);
        var profile = new ServiceProfile {
            Model = model,
            EnableBuiltInPackLoading = enableBuiltInPackLoading,
            EnableDefaultPluginPaths = enableDefaultPluginPaths
        };
        store.UpsertAsync(profileName, profile, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void SeedLegacyProfileRow(string dbPath, string profileName, string model, int maxToolRounds = 24) {
        var db = new SQLite();
        db.ExecuteNonQuery(dbPath, """
CREATE TABLE IF NOT EXISTS ix_service_profiles (
  name TEXT PRIMARY KEY,
  model TEXT NOT NULL,
  transport_kind TEXT NOT NULL,
  openai_base_url TEXT NULL,
  openai_api_key BLOB NULL,
  openai_streaming INTEGER NOT NULL,
  openai_allow_insecure_http INTEGER NOT NULL,
  openai_allow_insecure_http_non_loopback INTEGER NOT NULL,
  reasoning_effort TEXT NULL,
  reasoning_summary TEXT NULL,
  text_verbosity TEXT NULL,
  temperature REAL NULL,
  max_tool_rounds INTEGER NOT NULL,
  parallel_tools INTEGER NOT NULL,
  turn_timeout_seconds INTEGER NOT NULL,
  tool_timeout_seconds INTEGER NOT NULL,
  instructions_file TEXT NULL,
  max_table_rows INTEGER NOT NULL,
  max_sample INTEGER NOT NULL,
  redact INTEGER NOT NULL,
  ad_domain_controller TEXT NULL,
  ad_default_search_base_dn TEXT NULL,
  ad_max_results INTEGER NOT NULL,
  enable_powershell_pack INTEGER NOT NULL,
  enable_testimox_pack INTEGER NOT NULL,
  powershell_allow_write INTEGER NOT NULL,
  enable_officeimo_pack INTEGER NOT NULL,
  enable_default_plugin_paths INTEGER NOT NULL,
  updated_utc TEXT NOT NULL
);
""");

        db.ExecuteNonQuery(
            dbPath,
            """
INSERT INTO ix_service_profiles (
  name, model, transport_kind, openai_base_url, openai_api_key,
  openai_streaming, openai_allow_insecure_http, openai_allow_insecure_http_non_loopback,
  reasoning_effort, reasoning_summary, text_verbosity, temperature,
  max_tool_rounds, parallel_tools, turn_timeout_seconds, tool_timeout_seconds,
  instructions_file, max_table_rows, max_sample, redact,
  ad_domain_controller, ad_default_search_base_dn, ad_max_results,
  enable_powershell_pack, enable_testimox_pack, powershell_allow_write, enable_officeimo_pack, enable_default_plugin_paths,
  updated_utc
) VALUES (
  @name, @model, @transport_kind, @openai_base_url, @openai_api_key,
  @openai_streaming, @openai_allow_insecure_http, @openai_allow_insecure_http_non_loopback,
  @reasoning_effort, @reasoning_summary, @text_verbosity, @temperature,
  @max_tool_rounds, @parallel_tools, @turn_timeout_seconds, @tool_timeout_seconds,
  @instructions_file, @max_table_rows, @max_sample, @redact,
  @ad_domain_controller, @ad_default_search_base_dn, @ad_max_results,
  @enable_powershell_pack, @enable_testimox_pack, @powershell_allow_write, @enable_officeimo_pack, @enable_default_plugin_paths,
  @updated_utc
);
""",
            parameters: new Dictionary<string, object?> {
                ["@name"] = profileName,
                ["@model"] = model,
                ["@transport_kind"] = "native",
                ["@openai_base_url"] = null,
                ["@openai_api_key"] = null,
                ["@openai_streaming"] = 1,
                ["@openai_allow_insecure_http"] = 0,
                ["@openai_allow_insecure_http_non_loopback"] = 0,
                ["@reasoning_effort"] = null,
                ["@reasoning_summary"] = null,
                ["@text_verbosity"] = null,
                ["@temperature"] = null,
                ["@max_tool_rounds"] = maxToolRounds,
                ["@parallel_tools"] = 1,
                ["@turn_timeout_seconds"] = 0,
                ["@tool_timeout_seconds"] = 0,
                ["@instructions_file"] = null,
                ["@max_table_rows"] = 0,
                ["@max_sample"] = 0,
                ["@redact"] = 0,
                ["@ad_domain_controller"] = null,
                ["@ad_default_search_base_dn"] = null,
                ["@ad_max_results"] = 1000,
                ["@enable_powershell_pack"] = 1,
                ["@enable_testimox_pack"] = 0,
                ["@powershell_allow_write"] = 0,
                ["@enable_officeimo_pack"] = 1,
                ["@enable_default_plugin_paths"] = 1,
                ["@updated_utc"] = DateTime.UtcNow.ToString("O")
            });
    }

    private static IReadOnlyList<string> ReadStoredPluginPaths(string dbPath, string profileName) {
        var db = new SQLite();
        var table = QueryAsTable(db.Query(
            dbPath,
            "SELECT path FROM ix_service_profile_plugin_paths WHERE profile_name = @name ORDER BY ord;",
            parameters: new Dictionary<string, object?> { ["@name"] = profileName }));

        if (table is null || table.Rows.Count == 0) {
            return Array.Empty<string>();
        }

        return table.Rows
            .Cast<DataRow>()
            .Select(static row => row["path"]?.ToString())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .ToArray();
    }

    private static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable table) {
            return table;
        }

        return queryResult is DataSet dataSet && dataSet.Tables.Count > 0
            ? dataSet.Tables[0]
            : null;
    }
}
