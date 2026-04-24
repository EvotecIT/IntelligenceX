using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private const int MaxToolRoundsLimit = ChatRequestOptionLimits.MaxToolRounds;

    private sealed class ReplOptions : IToolRuntimePolicySettings, IToolPackRuntimeSettings {
        public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;

        public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.Native;
        public string? OpenAIBaseUrl { get; set; }
        public string? OpenAIApiKey { get; set; }
        public bool OpenAIStreaming { get; set; } = true;
        public bool OpenAIAllowInsecureHttp { get; set; }
        public bool OpenAIAllowInsecureHttpNonLoopback { get; set; }

        public ReasoningEffort? ReasoningEffort { get; set; }
        public ReasoningSummary? ReasoningSummary { get; set; }
        public TextVerbosity? TextVerbosity { get; set; }
        public double? Temperature { get; set; }

        public string? ProfileName { get; set; }
        public string? StateDbPath { get; set; }

        public bool ShowHelp { get; set; }
        public bool ForceLogin { get; set; }
        public bool ParallelToolCalls { get; set; } = true;
        public bool AllowMutatingParallelToolCalls { get; set; }
        public int MaxToolRounds { get; set; } = ChatRequestOptionLimits.DefaultToolRounds;
        public int TurnTimeoutSeconds { get; set; }
        public int ToolTimeoutSeconds { get; set; }
        public List<string> AllowedRoots { get; } = new();
        public bool EchoToolOutputs { get; set; }
        public int MaxConsoleToolOutputChars { get; set; } = 2000;
        public bool ShowToolIds { get; set; }
        public bool LiveProgress { get; set; } = true;
        public int MaxTableRows { get; set; }
        public int MaxSample { get; set; }
        public bool Redact { get; set; }
        public string? AuthPath { get; set; }
        public string? InstructionsFile { get; set; }
        public string? ScenarioFile { get; set; }
        public string? ScenarioOutputFile { get; set; }
        public bool ScenarioContinueOnError { get; set; }
        public string? AdDomainController { get; set; }
        public string? AdDefaultSearchBaseDn { get; set; }
        public int AdMaxResults { get; set; } = 1000;
        public bool PowerShellAllowWrite { get; set; }
        public bool EnableBuiltInPackLoading { get; set; } = true;
        public bool UseDefaultBuiltInToolAssemblyNames { get; set; } = true;
        public List<string> BuiltInToolAssemblyNames { get; } = new();
        public List<string> BuiltInToolProbePaths { get; } = new();
        public bool EnableWorkspaceBuiltInToolOutputProbing { get; set; }
        public bool EnableDefaultPluginPaths { get; set; } = true;
        public List<string> PluginPaths { get; } = new();
        public List<string> DisabledPackIds { get; } = new();
        public List<string> EnabledPackIds { get; } = new();
        public ToolWriteGovernanceMode WriteGovernanceMode { get; set; } = ToolWriteGovernanceMode.Enforced;
        public bool RequireWriteGovernanceRuntime { get; set; } = true;
        public bool RequireWriteAuditSinkForWriteOperations { get; set; }
        public ToolWriteAuditSinkMode WriteAuditSinkMode { get; set; } = ToolWriteAuditSinkMode.None;
        public string? WriteAuditSinkPath { get; set; }
        public ToolAuthenticationRuntimePreset AuthenticationRuntimePreset { get; set; } = ToolAuthenticationRuntimePreset.Default;
        public bool RequireExplicitRoutingMetadata { get; set; } = true;
        public bool RequireAuthenticationRuntime { get; set; }
        public string? RunAsProfilePath { get; set; }
        public string? AuthenticationProfilePath { get; set; }

        ToolAuthenticationRuntimePreset IToolRuntimePolicySettings.AuthenticationRuntimePreset => AuthenticationRuntimePreset;
        IReadOnlyList<string> IToolPackRuntimeSettings.AllowedRoots => AllowedRoots;
        IReadOnlyList<string> IToolPackRuntimeSettings.BuiltInToolAssemblyNames => BuiltInToolAssemblyNames;
        IReadOnlyList<string> IToolPackRuntimeSettings.BuiltInToolProbePaths => BuiltInToolProbePaths;
        bool IToolPackRuntimeSettings.EnableWorkspaceBuiltInToolOutputProbing => EnableWorkspaceBuiltInToolOutputProbing;
        IReadOnlyList<string> IToolPackRuntimeSettings.PluginPaths => PluginPaths;
        IReadOnlyList<string> IToolPackRuntimeSettings.DisabledPackIds => DisabledPackIds;
        IReadOnlyList<string> IToolPackRuntimeSettings.EnabledPackIds => EnabledPackIds;

        public static ReplOptions Parse(string[] args, out string? error) {
            error = null;
            var options = new ReplOptions();
            if (args is null || args.Length == 0) {
                return options;
            }

            // Pre-scan profile flags so we can apply profile defaults before overrides.
            PreScanProfileFlags(args, options, out error);
            if (!string.IsNullOrWhiteSpace(error)) {
                return options;
            }
            if (!string.IsNullOrWhiteSpace(options.ProfileName)) {
                if (!TryLoadProfile(options, out error)) {
                    return options;
                }
            }

            for (var i = 0; i < args.Length; i++) {
                var a = args[i] ?? string.Empty;

                (bool Success, string? Value, string? Error) ConsumeRuntimePolicyValue() {
                    return TryGetValue(args, ref i, out var value, out var valueError)
                        ? (true, value, null)
                        : (false, null, valueError);
                }

                if (!ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
                        argument: a,
                        consumeValue: ConsumeRuntimePolicyValue,
                        setWriteGovernanceMode: mode => options.WriteGovernanceMode = mode,
                        setRequireWriteGovernanceRuntime: required => options.RequireWriteGovernanceRuntime = required,
                        setRequireWriteAuditSinkForWriteOperations: required => options.RequireWriteAuditSinkForWriteOperations = required,
                        setWriteAuditSinkMode: mode => options.WriteAuditSinkMode = mode,
                        setWriteAuditSinkPath: path => options.WriteAuditSinkPath = path,
                        setAuthenticationRuntimePreset: preset => options.AuthenticationRuntimePreset = preset,
                        setRequireExplicitRoutingMetadata: required => options.RequireExplicitRoutingMetadata = required,
                        setRequireAuthenticationRuntime: required => options.RequireAuthenticationRuntime = required,
                        setRunAsProfilePath: path => options.RunAsProfilePath = path,
                        setAuthenticationProfilePath: path => options.AuthenticationProfilePath = path,
                        handled: out var runtimePolicyHandled,
                        error: out error)) {
                    return options;
                }

                if (runtimePolicyHandled) {
                    continue;
                }

                switch (a) {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        return options;
                    case "--model":
                        if (!TryGetValue(args, ref i, out var model, out error)) {
                            return options;
                        }
                        options.Model = model;
                        break;
                    case "--reasoning-effort":
                        if (!TryGetValue(args, ref i, out var effortValue, out error)) {
                            return options;
                        }
                        var effort = ChatEnumParser.ParseReasoningEffort(effortValue);
                        if (!effort.HasValue) {
                            error = "--reasoning-effort must be one of: minimal, low, medium, high, xhigh.";
                            return options;
                        }
                        options.ReasoningEffort = effort;
                        break;
                    case "--reasoning-summary":
                        if (!TryGetValue(args, ref i, out var summaryValue, out error)) {
                            return options;
                        }
                        var summary = ChatEnumParser.ParseReasoningSummary(summaryValue);
                        if (!summary.HasValue) {
                            error = "--reasoning-summary must be one of: auto, concise, detailed, off.";
                            return options;
                        }
                        options.ReasoningSummary = summary;
                        break;
                    case "--text-verbosity":
                        if (!TryGetValue(args, ref i, out var verbosityValue, out error)) {
                            return options;
                        }
                        var verbosity = ChatEnumParser.ParseTextVerbosity(verbosityValue);
                        if (!verbosity.HasValue) {
                            error = "--text-verbosity must be one of: low, medium, high.";
                            return options;
                        }
                        options.TextVerbosity = verbosity;
                        break;
                    case "--temperature":
                        if (!TryGetValue(args, ref i, out var tempValue, out error)) {
                            return options;
                        }
                        if (!double.TryParse(tempValue, out var temp) || double.IsNaN(temp) || double.IsInfinity(temp) || temp < 0d || temp > 2d) {
                            error = "--temperature must be a number between 0 and 2.";
                            return options;
                        }
                        options.Temperature = temp;
                        break;
                    case "--openai-transport":
                        if (!TryGetValue(args, ref i, out var kindValue, out error)) {
                            return options;
                        }
                        if (!TryParseTransport(kindValue, out var kind)) {
                            error = "--openai-transport must be one of: native, appserver, compatible-http, copilot-cli.";
                            return options;
                        }
                        options.OpenAITransport = kind;
                        break;
                    case "--openai-base-url":
                        if (!TryGetValue(args, ref i, out var baseUrl, out error)) {
                            return options;
                        }
                        options.OpenAIBaseUrl = baseUrl;
                        break;
                    case "--openai-api-key":
                        if (!TryGetValue(args, ref i, out var apiKey, out error)) {
                            return options;
                        }
                        options.OpenAIApiKey = apiKey;
                        break;
                    case "--openai-stream":
                        options.OpenAIStreaming = true;
                        break;
                    case "--openai-no-stream":
                        options.OpenAIStreaming = false;
                        break;
                    case "--openai-allow-insecure-http":
                        options.OpenAIAllowInsecureHttp = true;
                        break;
                    case "--openai-allow-insecure-http-non-loopback":
                        options.OpenAIAllowInsecureHttpNonLoopback = true;
                        break;
                    case "--profile":
                        if (!TryGetValue(args, ref i, out var profileName, out error)) {
                            return options;
                        }
                        break;
                    case "--state-db":
                        if (!TryGetValue(args, ref i, out var stateDb, out error)) {
                            return options;
                        }
                        options.StateDbPath = stateDb;
                        break;
                    case "--allow-root":
                        if (!TryGetValue(args, ref i, out var root, out error)) {
                            return options;
                        }
                        options.AllowedRoots.Add(root);
                        break;
                    case "--auth-path":
                        if (!TryGetValue(args, ref i, out var authPath, out error)) {
                            return options;
                        }
                        options.AuthPath = authPath;
                        break;
                    case "--instructions-file":
                        if (!TryGetValue(args, ref i, out var instructionsFile, out error)) {
                            return options;
                        }
                        options.InstructionsFile = instructionsFile;
                        break;
                    case "--scenario-file":
                        if (!TryGetValue(args, ref i, out var scenarioFile, out error)) {
                            return options;
                        }
                        options.ScenarioFile = scenarioFile;
                        break;
                    case "--scenario-output":
                        if (!TryGetValue(args, ref i, out var scenarioOutput, out error)) {
                            return options;
                        }
                        options.ScenarioOutputFile = scenarioOutput;
                        break;
                    case "--scenario-continue-on-error":
                        options.ScenarioContinueOnError = true;
                        break;
                    case "--ad-domain-controller":
                        if (!TryGetValue(args, ref i, out var dc, out error)) {
                            return options;
                        }
                        options.AdDomainController = dc;
                        break;
                    case "--ad-search-base":
                        if (!TryGetValue(args, ref i, out var baseDn, out error)) {
                            return options;
                        }
                        options.AdDefaultSearchBaseDn = baseDn;
                        break;
                    case "--ad-max-results":
                        if (!TryGetValue(args, ref i, out var adMax, out error)) {
                            return options;
                        }
                        if (!int.TryParse(adMax, out var adMaxResults) || adMaxResults <= 0) {
                            error = "Invalid --ad-max-results value.";
                            return options;
                        }
                        options.AdMaxResults = adMaxResults;
                        break;
                    case "--enable-pack-id":
                    case "--disable-pack-id":
                        if (!TryGetValue(args, ref i, out var packId, out error)) {
                            return options;
                        }
                        var enablePack = string.Equals(a, "--enable-pack-id", StringComparison.Ordinal);
                        if (!TryApplyPackEnablement(options, packId, enablePack, a, out error)) {
                            return options;
                        }
                        break;
                    case "--powershell-allow-write":
                        options.PowerShellAllowWrite = true;
                        break;
                    case "--no-built-in-packs":
                        options.EnableBuiltInPackLoading = false;
                        break;
                    case "--built-in-packs":
                        options.EnableBuiltInPackLoading = true;
                        break;
                    case "--built-in-tool-assembly":
                        if (!TryGetValue(args, ref i, out var builtInAssemblyName, out error)) {
                            return options;
                        }
                        options.BuiltInToolAssemblyNames.Add(builtInAssemblyName);
                        break;
                    case "--built-in-tool-probe-path":
                        if (!TryGetValue(args, ref i, out var builtInToolProbePath, out error)) {
                            return options;
                        }
                        options.BuiltInToolProbePaths.Add(builtInToolProbePath);
                        break;
                    case "--enable-workspace-built-in-tool-output-probing":
                        options.EnableWorkspaceBuiltInToolOutputProbing = true;
                        break;
                    case "--disable-workspace-built-in-tool-output-probing":
                        options.EnableWorkspaceBuiltInToolOutputProbing = false;
                        break;
                    case "--no-default-built-in-tool-assemblies":
                        options.UseDefaultBuiltInToolAssemblyNames = false;
                        break;
                    case "--default-built-in-tool-assemblies":
                        options.UseDefaultBuiltInToolAssemblyNames = true;
                        break;
                    case "--plugin-path":
                        if (!TryGetValue(args, ref i, out var pluginPath, out error)) {
                            return options;
                        }
                        options.PluginPaths.Add(pluginPath);
                        break;
                    case "--no-default-plugin-paths":
                        options.EnableDefaultPluginPaths = false;
                        break;
                    case "--max-table-rows":
                        if (!TryGetValue(args, ref i, out var maxRows, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxRows, out var mr) || mr < 0) {
                            error = "Invalid --max-table-rows value.";
                            return options;
                        }
                        options.MaxTableRows = mr;
                        break;
                    case "--max-sample":
                        if (!TryGetValue(args, ref i, out var maxSample, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxSample, out var ms) || ms < 0) {
                            error = "Invalid --max-sample value.";
                            return options;
                        }
                        options.MaxSample = ms;
                        break;
                    case "--redact":
                        options.Redact = true;
                        break;
                    case "--max-tool-rounds":
                        if (!TryGetValue(args, ref i, out var rounds, out error)) {
                            return options;
                        }
                        if (!int.TryParse(rounds, out var n) || n < ChatRequestOptionLimits.MinToolRounds || n > MaxToolRoundsLimit) {
                            error = $"--max-tool-rounds must be between {ChatRequestOptionLimits.MinToolRounds} and {MaxToolRoundsLimit}.";
                            return options;
                        }
                        options.MaxToolRounds = n;
                        break;
                    case "--parallel-tools":
                        options.ParallelToolCalls = true;
                        break;
                    case "--no-parallel-tools":
                        options.ParallelToolCalls = false;
                        break;
                    case "--allow-mutating-parallel-tools":
                        options.AllowMutatingParallelToolCalls = true;
                        break;
                    case "--disallow-mutating-parallel-tools":
                        options.AllowMutatingParallelToolCalls = false;
                        break;
                    case "--turn-timeout-seconds":
                        if (!TryGetValue(args, ref i, out var turnTimeout, out error)) {
                            return options;
                        }
                        if (!int.TryParse(turnTimeout, out var tts) || tts < 0) {
                            error = "Invalid --turn-timeout-seconds value.";
                            return options;
                        }
                        options.TurnTimeoutSeconds = tts;
                        break;
                    case "--tool-timeout-seconds":
                        if (!TryGetValue(args, ref i, out var toolTimeout, out error)) {
                            return options;
                        }
                        if (!int.TryParse(toolTimeout, out var ots) || ots < 0) {
                            error = "Invalid --tool-timeout-seconds value.";
                            return options;
                        }
                        options.ToolTimeoutSeconds = ots;
                        break;
                    case "--login":
                        options.ForceLogin = true;
                        break;
                    case "--echo-tool-outputs":
                        options.EchoToolOutputs = true;
                        break;
                    case "--show-tool-ids":
                        options.ShowToolIds = true;
                        break;
                    case "--no-progress":
                        options.LiveProgress = false;
                        break;
                    case "--max-tool-output":
                        if (!TryGetValue(args, ref i, out var maxOut, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxOut, out var maxChars) || maxChars <= 0) {
                            error = "Invalid --max-tool-output value.";
                            return options;
                        }
                        options.MaxConsoleToolOutputChars = maxChars;
                        break;
                    default:
                        // Allow users to run: `dotnet run -- --model x`
                        if (a.StartsWith("-", StringComparison.Ordinal)) {
                            error = $"Unknown option: {a}";
                            return options;
                        }
                        break;
                }
            }

            if (options.OpenAITransport == OpenAITransportKind.CompatibleHttp) {
                if (!TryValidateCompatibleHttpBaseUrl(options, out error)) {
                    return options;
                }
            }

            return options;
        }

        internal static string GetDefaultStateDbPath() {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) {
                root = ".";
            }
            return Path.Combine(root, "IntelligenceX.Chat", "state.db");
        }

        private static void PreScanProfileFlags(string[] args, ReplOptions options, out string? error) {
            error = null;
            if (args is null || args.Length == 0) {
                return;
            }

            for (var i = 0; i < args.Length; i++) {
                var a = args[i] ?? string.Empty;
                if (a is "--state-db") {
                    if (!TryGetValue(args, ref i, out var path, out error)) {
                        return;
                    }
                    options.StateDbPath = path;
                    continue;
                }
                if (a is "--profile") {
                    if (!TryGetValue(args, ref i, out var name, out error)) {
                        return;
                    }
                    options.ProfileName = name;
                    continue;
                }
            }
        }

        private static bool TryLoadProfile(ReplOptions options, out string? error) {
            error = null;
            var name = (options.ProfileName ?? string.Empty).Trim();
            if (name.Length == 0) {
                return true;
            }

            var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? GetDefaultStateDbPath() : options.StateDbPath!.Trim();
            try {
                using var store = new SqliteServiceProfileStore(dbPath);
                if (ServiceProfilePresets.TryResolveStoredOrBuiltInProfile(
                        name,
                        allowStoredProfiles: true,
                        candidateName => store.GetAsync(candidateName, CancellationToken.None).GetAwaiter().GetResult(),
                        out var resolvedName,
                        out var resolvedProfile,
                        out _)) {
                    options.ApplyProfile(resolvedProfile!);
                    options.ProfileName = resolvedName;
                    return true;
                }
            } catch (Exception ex) {
                error = $"Failed to load profile '{name}': {ex.Message}";
                return false;
            }

            error = $"Profile not found: {name}";
            return false;
        }

        internal void ApplyProfile(ServiceProfile profile) {
            if (profile is null) {
                return;
            }

            Model = profile.Model ?? Model;

            OpenAITransport = profile.OpenAITransport;
            OpenAIBaseUrl = profile.OpenAIBaseUrl;
            OpenAIApiKey = profile.OpenAIApiKey;
            OpenAIStreaming = profile.OpenAIStreaming;
            OpenAIAllowInsecureHttp = profile.OpenAIAllowInsecureHttp;
            OpenAIAllowInsecureHttpNonLoopback = profile.OpenAIAllowInsecureHttpNonLoopback;

            ReasoningEffort = profile.ReasoningEffort;
            ReasoningSummary = profile.ReasoningSummary;
            TextVerbosity = profile.TextVerbosity;
            Temperature = profile.Temperature;

            MaxToolRounds = Math.Clamp(profile.MaxToolRounds, ChatRequestOptionLimits.MinToolRounds, MaxToolRoundsLimit);
            ParallelToolCalls = profile.ParallelTools;
            AllowMutatingParallelToolCalls = profile.AllowMutatingParallelToolCalls;
            TurnTimeoutSeconds = profile.TurnTimeoutSeconds;
            ToolTimeoutSeconds = profile.ToolTimeoutSeconds;

            AllowedRoots.Clear();
            if (profile.AllowedRoots is { Count: > 0 }) {
                AllowedRoots.AddRange(profile.AllowedRoots);
            }

            AdDomainController = profile.AdDomainController;
            AdDefaultSearchBaseDn = profile.AdDefaultSearchBaseDn;
            AdMaxResults = profile.AdMaxResults;
            PowerShellAllowWrite = profile.PowerShellAllowWrite;
            EnableBuiltInPackLoading = profile.EnableBuiltInPackLoading;
            UseDefaultBuiltInToolAssemblyNames = profile.UseDefaultBuiltInToolAssemblyNames;
            BuiltInToolAssemblyNames.Clear();
            if (profile.BuiltInToolAssemblyNames is { Count: > 0 }) {
                BuiltInToolAssemblyNames.AddRange(profile.BuiltInToolAssemblyNames);
            }
            EnableDefaultPluginPaths = profile.EnableDefaultPluginPaths;
            PluginPaths.Clear();
            if (profile.PluginPaths is { Count: > 0 }) {
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

        internal ReplOptions Clone() {
            var clone = new ReplOptions {
                Model = Model,
                OpenAITransport = OpenAITransport,
                OpenAIBaseUrl = OpenAIBaseUrl,
                OpenAIApiKey = OpenAIApiKey,
                OpenAIStreaming = OpenAIStreaming,
                OpenAIAllowInsecureHttp = OpenAIAllowInsecureHttp,
                OpenAIAllowInsecureHttpNonLoopback = OpenAIAllowInsecureHttpNonLoopback,
                ReasoningEffort = ReasoningEffort,
                ReasoningSummary = ReasoningSummary,
                TextVerbosity = TextVerbosity,
                Temperature = Temperature,
                ProfileName = ProfileName,
                StateDbPath = StateDbPath,
                ShowHelp = ShowHelp,
                ForceLogin = ForceLogin,
                ParallelToolCalls = ParallelToolCalls,
                AllowMutatingParallelToolCalls = AllowMutatingParallelToolCalls,
                MaxToolRounds = MaxToolRounds,
                TurnTimeoutSeconds = TurnTimeoutSeconds,
                ToolTimeoutSeconds = ToolTimeoutSeconds,
                EchoToolOutputs = EchoToolOutputs,
                MaxConsoleToolOutputChars = MaxConsoleToolOutputChars,
                ShowToolIds = ShowToolIds,
                LiveProgress = LiveProgress,
                MaxTableRows = MaxTableRows,
                MaxSample = MaxSample,
                Redact = Redact,
                AuthPath = AuthPath,
                InstructionsFile = InstructionsFile,
                ScenarioFile = ScenarioFile,
                ScenarioOutputFile = ScenarioOutputFile,
                ScenarioContinueOnError = ScenarioContinueOnError,
                AdDomainController = AdDomainController,
                AdDefaultSearchBaseDn = AdDefaultSearchBaseDn,
                AdMaxResults = AdMaxResults,
                PowerShellAllowWrite = PowerShellAllowWrite,
                EnableBuiltInPackLoading = EnableBuiltInPackLoading,
                UseDefaultBuiltInToolAssemblyNames = UseDefaultBuiltInToolAssemblyNames,
                EnableWorkspaceBuiltInToolOutputProbing = EnableWorkspaceBuiltInToolOutputProbing,
                EnableDefaultPluginPaths = EnableDefaultPluginPaths,
                WriteGovernanceMode = WriteGovernanceMode,
                RequireWriteGovernanceRuntime = RequireWriteGovernanceRuntime,
                RequireWriteAuditSinkForWriteOperations = RequireWriteAuditSinkForWriteOperations,
                WriteAuditSinkMode = WriteAuditSinkMode,
                WriteAuditSinkPath = WriteAuditSinkPath,
                AuthenticationRuntimePreset = AuthenticationRuntimePreset,
                RequireExplicitRoutingMetadata = RequireExplicitRoutingMetadata,
                RequireAuthenticationRuntime = RequireAuthenticationRuntime,
                RunAsProfilePath = RunAsProfilePath,
                AuthenticationProfilePath = AuthenticationProfilePath
            };

            if (AllowedRoots.Count > 0) {
                clone.AllowedRoots.AddRange(AllowedRoots);
            }
            if (BuiltInToolAssemblyNames.Count > 0) {
                clone.BuiltInToolAssemblyNames.AddRange(BuiltInToolAssemblyNames);
            }
            if (BuiltInToolProbePaths.Count > 0) {
                clone.BuiltInToolProbePaths.AddRange(BuiltInToolProbePaths);
            }
            if (PluginPaths.Count > 0) {
                clone.PluginPaths.AddRange(PluginPaths);
            }
            if (DisabledPackIds.Count > 0) {
                clone.DisabledPackIds.AddRange(DisabledPackIds);
            }
            if (EnabledPackIds.Count > 0) {
                clone.EnabledPackIds.AddRange(EnabledPackIds);
            }

            return clone;
        }

        internal void CopyFrom(ReplOptions source) {
            if (source is null) {
                throw new ArgumentNullException(nameof(source));
            }

            Model = source.Model;
            OpenAITransport = source.OpenAITransport;
            OpenAIBaseUrl = source.OpenAIBaseUrl;
            OpenAIApiKey = source.OpenAIApiKey;
            OpenAIStreaming = source.OpenAIStreaming;
            OpenAIAllowInsecureHttp = source.OpenAIAllowInsecureHttp;
            OpenAIAllowInsecureHttpNonLoopback = source.OpenAIAllowInsecureHttpNonLoopback;
            ReasoningEffort = source.ReasoningEffort;
            ReasoningSummary = source.ReasoningSummary;
            TextVerbosity = source.TextVerbosity;
            Temperature = source.Temperature;
            ProfileName = source.ProfileName;
            StateDbPath = source.StateDbPath;
            ShowHelp = source.ShowHelp;
            ForceLogin = source.ForceLogin;
            ParallelToolCalls = source.ParallelToolCalls;
            AllowMutatingParallelToolCalls = source.AllowMutatingParallelToolCalls;
            MaxToolRounds = source.MaxToolRounds;
            TurnTimeoutSeconds = source.TurnTimeoutSeconds;
            ToolTimeoutSeconds = source.ToolTimeoutSeconds;
            EchoToolOutputs = source.EchoToolOutputs;
            MaxConsoleToolOutputChars = source.MaxConsoleToolOutputChars;
            ShowToolIds = source.ShowToolIds;
            LiveProgress = source.LiveProgress;
            MaxTableRows = source.MaxTableRows;
            MaxSample = source.MaxSample;
            Redact = source.Redact;
            AuthPath = source.AuthPath;
            InstructionsFile = source.InstructionsFile;
            ScenarioFile = source.ScenarioFile;
            ScenarioOutputFile = source.ScenarioOutputFile;
            ScenarioContinueOnError = source.ScenarioContinueOnError;
            AdDomainController = source.AdDomainController;
            AdDefaultSearchBaseDn = source.AdDefaultSearchBaseDn;
            AdMaxResults = source.AdMaxResults;
            PowerShellAllowWrite = source.PowerShellAllowWrite;
            EnableBuiltInPackLoading = source.EnableBuiltInPackLoading;
            UseDefaultBuiltInToolAssemblyNames = source.UseDefaultBuiltInToolAssemblyNames;
            EnableWorkspaceBuiltInToolOutputProbing = source.EnableWorkspaceBuiltInToolOutputProbing;
            EnableDefaultPluginPaths = source.EnableDefaultPluginPaths;
            WriteGovernanceMode = source.WriteGovernanceMode;
            RequireWriteGovernanceRuntime = source.RequireWriteGovernanceRuntime;
            RequireWriteAuditSinkForWriteOperations = source.RequireWriteAuditSinkForWriteOperations;
            WriteAuditSinkMode = source.WriteAuditSinkMode;
            WriteAuditSinkPath = source.WriteAuditSinkPath;
            AuthenticationRuntimePreset = source.AuthenticationRuntimePreset;
            RequireExplicitRoutingMetadata = source.RequireExplicitRoutingMetadata;
            RequireAuthenticationRuntime = source.RequireAuthenticationRuntime;
            RunAsProfilePath = source.RunAsProfilePath;
            AuthenticationProfilePath = source.AuthenticationProfilePath;

            AllowedRoots.Clear();
            if (source.AllowedRoots.Count > 0) {
                AllowedRoots.AddRange(source.AllowedRoots);
            }

            BuiltInToolAssemblyNames.Clear();
            if (source.BuiltInToolAssemblyNames.Count > 0) {
                BuiltInToolAssemblyNames.AddRange(source.BuiltInToolAssemblyNames);
            }

            BuiltInToolProbePaths.Clear();
            if (source.BuiltInToolProbePaths.Count > 0) {
                BuiltInToolProbePaths.AddRange(source.BuiltInToolProbePaths);
            }

            PluginPaths.Clear();
            if (source.PluginPaths.Count > 0) {
                PluginPaths.AddRange(source.PluginPaths);
            }

            DisabledPackIds.Clear();
            if (source.DisabledPackIds.Count > 0) {
                DisabledPackIds.AddRange(source.DisabledPackIds);
            }

            EnabledPackIds.Clear();
            if (source.EnabledPackIds.Count > 0) {
                EnabledPackIds.AddRange(source.EnabledPackIds);
            }
        }

        private static bool TryApplyPackEnablement(
            ReplOptions options,
            string? rawPackId,
            bool enabled,
            string argumentName,
            out string? error) {
            error = null;
            var normalizedPackId = ToolPackBootstrap.NormalizePackId(rawPackId);
            if (normalizedPackId.Length == 0) {
                error = $"{argumentName} requires a non-empty pack id.";
                return false;
            }

            if (enabled) {
                RemovePackId(options.DisabledPackIds, normalizedPackId);
                AddPackIdIfMissing(options.EnabledPackIds, normalizedPackId);
            } else {
                RemovePackId(options.EnabledPackIds, normalizedPackId);
                AddPackIdIfMissing(options.DisabledPackIds, normalizedPackId);
            }

            return true;
        }

        private static void RemovePackId(List<string> packIds, string normalizedPackId) {
            for (var i = packIds.Count - 1; i >= 0; i--) {
                if (string.Equals(ToolPackBootstrap.NormalizePackId(packIds[i]), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                    packIds.RemoveAt(i);
                }
            }
        }

        private static bool ContainsPackId(List<string> packIds, string normalizedPackId) {
            for (var i = 0; i < packIds.Count; i++) {
                if (string.Equals(ToolPackBootstrap.NormalizePackId(packIds[i]), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        private static void AddPackIdIfMissing(List<string> packIds, string normalizedPackId) {
            if (!ContainsPackId(packIds, normalizedPackId)) {
                packIds.Add(normalizedPackId);
            }
        }

        private static bool TryGetValue(string[] args, ref int i, out string value, out string? error) {
            error = null;
            value = string.Empty;
            if (i + 1 >= args.Length) {
                error = $"Missing value for {args[i]}.";
                return false;
            }
            i++;
            value = args[i] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) {
                error = $"Empty value for {args[i - 1]}.";
                return false;
            }
            return true;
        }

        private static bool TryParseTransport(string value, out OpenAITransportKind kind) {
            kind = OpenAITransportKind.Native;
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }
            switch (value.Trim().ToLowerInvariant()) {
                case "native":
                    kind = OpenAITransportKind.Native;
                    return true;
                case "appserver":
                case "app-server":
                case "codex":
                    kind = OpenAITransportKind.AppServer;
                    return true;
                case "compatible-http":
                case "compatiblehttp":
                case "http":
                case "local":
                case "ollama":
                case "lmstudio":
                case "lm-studio":
                    kind = OpenAITransportKind.CompatibleHttp;
                    return true;
                case "copilot":
                case "copilot-cli":
                case "github-copilot":
                case "githubcopilot":
                    kind = OpenAITransportKind.CopilotCli;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryValidateCompatibleHttpBaseUrl(ReplOptions options, out string? error) {
            error = null;
            try {
                var compatible = new OpenAICompatibleHttpOptions {
                    BaseUrl = options.OpenAIBaseUrl,
                    AllowInsecureHttp = options.OpenAIAllowInsecureHttp,
                    AllowInsecureHttpNonLoopback = options.OpenAIAllowInsecureHttpNonLoopback,
                    Streaming = options.OpenAIStreaming,
                    ApiKey = options.OpenAIApiKey
                };
                compatible.Validate();
                return true;
            } catch (Exception ex) {
                error = ex.Message;
                return false;
            }
        }
    }
}
