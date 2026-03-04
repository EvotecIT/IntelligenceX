using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ServiceOptions : IToolRuntimePolicySettings, IToolPackRuntimeSettings {
    internal void ApplyProfile(ServiceProfile profile) {
        if (profile == null) {
            return;
        }

        Model = profile.Model ?? Model;

        OpenAITransport = profile.OpenAITransport;
        OpenAIBaseUrl = profile.OpenAIBaseUrl;
        OpenAIAuthMode = profile.OpenAIAuthMode;
        OpenAIApiKey = profile.OpenAIApiKey;
        OpenAIBasicUsername = profile.OpenAIBasicUsername;
        OpenAIBasicPassword = profile.OpenAIBasicPassword;
        OpenAIAccountId = profile.OpenAIAccountId;
        OpenAIStreaming = profile.OpenAIStreaming;
        OpenAIAllowInsecureHttp = profile.OpenAIAllowInsecureHttp;
        OpenAIAllowInsecureHttpNonLoopback = profile.OpenAIAllowInsecureHttpNonLoopback;

        ReasoningEffort = profile.ReasoningEffort;
        ReasoningSummary = profile.ReasoningSummary;
        TextVerbosity = profile.TextVerbosity;
        Temperature = profile.Temperature;

        MaxToolRounds = Math.Clamp(profile.MaxToolRounds, 1, MaxToolRoundsLimit);
        ParallelTools = profile.ParallelTools;
        AllowMutatingParallelToolCalls = profile.AllowMutatingParallelToolCalls;
        TurnTimeoutSeconds = profile.TurnTimeoutSeconds;
        ToolTimeoutSeconds = profile.ToolTimeoutSeconds;
        SessionExecutionQueueLimit = Math.Clamp(profile.SessionExecutionQueueLimit, 0, MaxSessionExecutionQueueLimit);
        GlobalExecutionLaneConcurrency = Math.Clamp(profile.GlobalExecutionLaneConcurrency, 0, MaxGlobalExecutionLaneConcurrency);

        AllowedRoots.Clear();
        if (profile.AllowedRoots != null && profile.AllowedRoots.Count > 0) {
            AllowedRoots.AddRange(profile.AllowedRoots);
        }

        AdDomainController = profile.AdDomainController;
        AdDefaultSearchBaseDn = profile.AdDefaultSearchBaseDn;
        AdMaxResults = profile.AdMaxResults;
        PowerShellAllowWrite = profile.PowerShellAllowWrite;
        EnableBuiltInPackLoading = profile.EnableBuiltInPackLoading;
        EnableDefaultPluginPaths = profile.EnableDefaultPluginPaths;
        PluginPaths.Clear();
        if (profile.PluginPaths != null && profile.PluginPaths.Count > 0) {
            PluginPaths.AddRange(profile.PluginPaths);
        }
        DisabledPackIds.Clear();
        if (profile.DisabledPackIds is { Count: > 0 }) {
            DisabledPackIds.AddRange(profile.DisabledPackIds);
        }
        EnabledPackIds.Clear();
        if (profile.EnabledPackIds is { Count: > 0 }) {
            EnabledPackIds.AddRange(profile.EnabledPackIds);
        }
        ToolRuntimePolicyBootstrap.ApplyProfileRuntimePolicy(
            writeGovernanceMode: profile.WriteGovernanceMode,
            requireWriteGovernanceRuntime: profile.RequireWriteGovernanceRuntime,
            requireWriteAuditSinkForWriteOperations: profile.RequireWriteAuditSinkForWriteOperations,
            writeAuditSinkMode: profile.WriteAuditSinkMode,
            writeAuditSinkPath: profile.WriteAuditSinkPath,
            authenticationRuntimePreset: profile.AuthenticationRuntimePreset,
            requireExplicitRoutingMetadata: profile.RequireExplicitRoutingMetadata,
            requireAuthenticationRuntime: profile.RequireAuthenticationRuntime,
            runAsProfilePath: profile.RunAsProfilePath,
            authenticationProfilePath: profile.AuthenticationProfilePath,
            setWriteGovernanceMode: mode => WriteGovernanceMode = mode,
            setRequireWriteGovernanceRuntime: required => RequireWriteGovernanceRuntime = required,
            setRequireWriteAuditSinkForWriteOperations: required => RequireWriteAuditSinkForWriteOperations = required,
            setWriteAuditSinkMode: mode => WriteAuditSinkMode = mode,
            setWriteAuditSinkPath: path => WriteAuditSinkPath = path,
            setAuthenticationRuntimePreset: preset => AuthenticationRuntimePreset = preset,
            setRequireExplicitRoutingMetadata: required => RequireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: required => RequireAuthenticationRuntime = required,
            setRunAsProfilePath: path => RunAsProfilePath = path,
            setAuthenticationProfilePath: path => AuthenticationProfilePath = path);

        InstructionsFile = profile.InstructionsFile;
        MaxTableRows = profile.MaxTableRows;
        MaxSample = profile.MaxSample;
        Redact = profile.Redact;
    }

    internal ServiceProfile ToProfile() {
        return new ServiceProfile {
            Model = Model,
            OpenAITransport = OpenAITransport,
            OpenAIBaseUrl = OpenAIBaseUrl,
            OpenAIAuthMode = OpenAIAuthMode,
            OpenAIApiKey = OpenAIApiKey,
            OpenAIBasicUsername = OpenAIBasicUsername,
            OpenAIBasicPassword = OpenAIBasicPassword,
            OpenAIAccountId = OpenAIAccountId,
            OpenAIStreaming = OpenAIStreaming,
            OpenAIAllowInsecureHttp = OpenAIAllowInsecureHttp,
            OpenAIAllowInsecureHttpNonLoopback = OpenAIAllowInsecureHttpNonLoopback,
            ReasoningEffort = ReasoningEffort,
            ReasoningSummary = ReasoningSummary,
            TextVerbosity = TextVerbosity,
            Temperature = Temperature,
            MaxToolRounds = Math.Clamp(MaxToolRounds, 1, MaxToolRoundsLimit),
            ParallelTools = ParallelTools,
            AllowMutatingParallelToolCalls = AllowMutatingParallelToolCalls,
            TurnTimeoutSeconds = TurnTimeoutSeconds,
            ToolTimeoutSeconds = ToolTimeoutSeconds,
            SessionExecutionQueueLimit = Math.Clamp(SessionExecutionQueueLimit, 0, MaxSessionExecutionQueueLimit),
            GlobalExecutionLaneConcurrency = Math.Clamp(GlobalExecutionLaneConcurrency, 0, MaxGlobalExecutionLaneConcurrency),
            AllowedRoots = new List<string>(AllowedRoots),
            AdDomainController = AdDomainController,
            AdDefaultSearchBaseDn = AdDefaultSearchBaseDn,
            AdMaxResults = AdMaxResults,
            PowerShellAllowWrite = PowerShellAllowWrite,
            EnableBuiltInPackLoading = EnableBuiltInPackLoading,
            EnableDefaultPluginPaths = EnableDefaultPluginPaths,
            PluginPaths = new List<string>(PluginPaths),
            DisabledPackIds = new List<string>(DisabledPackIds),
            EnabledPackIds = new List<string>(EnabledPackIds),
            WriteGovernanceMode = ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(WriteGovernanceMode),
            RequireWriteGovernanceRuntime = RequireWriteGovernanceRuntime,
            RequireWriteAuditSinkForWriteOperations = RequireWriteAuditSinkForWriteOperations,
            WriteAuditSinkMode = ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(WriteAuditSinkMode),
            WriteAuditSinkPath = WriteAuditSinkPath,
            AuthenticationRuntimePreset = ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(AuthenticationRuntimePreset),
            RequireExplicitRoutingMetadata = RequireExplicitRoutingMetadata,
            RequireAuthenticationRuntime = RequireAuthenticationRuntime,
            RunAsProfilePath = RunAsProfilePath,
            AuthenticationProfilePath = AuthenticationProfilePath,
            InstructionsFile = InstructionsFile,
            MaxTableRows = MaxTableRows,
            MaxSample = MaxSample,
            Redact = Redact
        };
    }

    private static void PreScanProfileFlags(string[] args, ServiceOptions options, out string? error) {
        error = null;
        if (args == null || args.Length == 0) {
            return;
        }

        for (var i = 0; i < args.Length; i++) {
            var arg = args[i] ?? string.Empty;
            if (arg is "--no-state-db") {
                options.NoStateDb = true;
                continue;
            }
            if (arg is "--state-db") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return;
                }
                options.StateDbPath = value;
                continue;
            }
            if (arg is "--profile") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return;
                }
                options.ProfileName = value;
                continue;
            }
            if (arg is "--save-profile") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return;
                }
                options.SaveProfileName = value;
                continue;
            }
        }
    }

    private static bool TryLoadProfile(ServiceOptions options, out string? error) {
        error = null;
        if (options == null) {
            error = "Internal error: options is null.";
            return false;
        }

        var name = (options.ProfileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) {
            return true;
        }

        var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? GetDefaultStateDbPath() : options.StateDbPath!;
        using var store = new SqliteServiceProfileStore(dbPath);
        var profile = store.GetAsync(name, CancellationToken.None).GetAwaiter().GetResult();
        if (profile == null) {
            if (ShouldBootstrapMissingProfile(options, name)) {
                return true;
            }
            error = $"Profile not found: {name}";
            return false;
        }
        options.ApplyProfile(profile);
        return true;
    }

    private static bool ShouldBootstrapMissingProfile(ServiceOptions options, string profileName) {
        if (options == null || string.IsNullOrWhiteSpace(profileName) || options.NoStateDb) {
            return false;
        }

        var saveName = (options.SaveProfileName ?? string.Empty).Trim();
        if (saveName.Length == 0) {
            return false;
        }

        return string.Equals(saveName, profileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySaveProfile(ServiceOptions options, out string? error) {
        error = null;
        var name = (options.SaveProfileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) {
            return true;
        }

        var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? GetDefaultStateDbPath() : options.StateDbPath!;
        using var store = new SqliteServiceProfileStore(dbPath);
        store.UpsertAsync(name, options.ToProfile(), CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    private static bool TryConsume(string[] args, ref int i, out string? value, out string? error) {
        error = null;
        value = null;
        if (i + 1 >= args.Length) {
            error = $"Missing value for {args[i]}.";
            return false;
        }
        i++;
        value = args[i];
        if (string.IsNullOrWhiteSpace(value)) {
            error = $"Empty value for {args[i - 1]}.";
            return false;
        }
        return true;
    }
}
