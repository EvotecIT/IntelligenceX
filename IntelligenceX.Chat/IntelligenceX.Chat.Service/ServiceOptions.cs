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
    private const int MaxToolRoundsLimit = ChatRequestOptionLimits.MaxToolRounds;
    private const int MaxSessionExecutionQueueLimit = 4096;
    private const int MaxGlobalExecutionLaneConcurrency = 512;
    private const int MinBackgroundSchedulerPollSeconds = 1;
    private const int MaxBackgroundSchedulerPollSeconds = 3600;
    private const int MinBackgroundSchedulerBurstLimit = 1;
    private const int MaxBackgroundSchedulerBurstLimit = 32;
    private const int MinBackgroundSchedulerFailureThreshold = 0;
    private const int MaxBackgroundSchedulerFailureThreshold = 32;
    private const int MinBackgroundSchedulerFailurePauseSeconds = 1;
    private const int MaxBackgroundSchedulerFailurePauseSeconds = 3600;
    private const int MinBackgroundSchedulerStartupPauseSeconds = 1;
    private const int MaxBackgroundSchedulerStartupPauseSeconds = 3600;
    private const int MaxBackgroundSchedulerMaintenanceWindows = 32;

    public bool ShowHelp { get; set; }
    public string PipeName { get; set; } = "intelligencex.chat";
    public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;

    // Provider selection for the underlying IntelligenceXClient used by the service.
    public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.Native;
    public string? OpenAIBaseUrl { get; set; }
    public OpenAICompatibleHttpAuthMode OpenAIAuthMode { get; set; } = OpenAICompatibleHttpAuthMode.Bearer;
    public string? OpenAIApiKey { get; set; }
    public string? OpenAIBasicUsername { get; set; }
    public string? OpenAIBasicPassword { get; set; }
    public string? OpenAIAccountId { get; set; } = Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_ID");
    public bool OpenAIStreaming { get; set; } = true;
    public bool OpenAIAllowInsecureHttp { get; set; }
    public bool OpenAIAllowInsecureHttpNonLoopback { get; set; }

    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public TextVerbosity? TextVerbosity { get; set; }
    public double? Temperature { get; set; }

    public string? ProfileName { get; set; }
    public string? SaveProfileName { get; set; }
    public string? StateDbPath { get; set; }
    public bool NoStateDb { get; set; }

    public int MaxToolRounds { get; set; } = ChatRequestOptionLimits.DefaultToolRounds;
    public bool ParallelTools { get; set; } = true;
    public bool AllowMutatingParallelToolCalls { get; set; }
    public int TurnTimeoutSeconds { get; set; }
    public int ToolTimeoutSeconds { get; set; }
    public int SessionExecutionQueueLimit { get; set; } = 32;
    public int GlobalExecutionLaneConcurrency { get; set; }
    public bool EnableBackgroundSchedulerDaemon { get; set; }
    public int BackgroundSchedulerPollSeconds { get; set; } = 30;
    public int BackgroundSchedulerBurstLimit { get; set; } = 4;
    public int BackgroundSchedulerFailureThreshold { get; set; } = 5;
    public int BackgroundSchedulerFailurePauseSeconds { get; set; } = 300;
    public bool BackgroundSchedulerStartPaused { get; set; }
    public int BackgroundSchedulerStartupPauseSeconds { get; set; }
    public List<string> BackgroundSchedulerMaintenanceWindows { get; } = new();
    public List<string> BackgroundSchedulerAllowedPackIds { get; } = new();
    public List<string> BackgroundSchedulerBlockedPackIds { get; } = new();
    public List<string> BackgroundSchedulerAllowedThreadIds { get; } = new();
    public List<string> BackgroundSchedulerBlockedThreadIds { get; } = new();
    public List<string> AllowedRoots { get; } = new();

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
    internal List<string> RuntimePluginPaths { get; } = new();
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
    IReadOnlyList<string> IToolPackRuntimeSettings.PluginPaths => GetEffectivePluginPaths();
    IReadOnlyList<string> IToolPackRuntimeSettings.DisabledPackIds => DisabledPackIds;
    IReadOnlyList<string> IToolPackRuntimeSettings.EnabledPackIds => EnabledPackIds;

    public string? InstructionsFile { get; set; }
    public int MaxTableRows { get; set; }
    public int MaxSample { get; set; }
    public bool Redact { get; set; }
    public bool ExitOnDisconnect { get; set; }
    public int? ParentProcessId { get; set; }

    // Optional override for where the chat service persists pending-action proposals (for /act <id> rehydration).
    // When unset, the service uses a LocalAppData-based default path.
    // Other file-backed chat-state stores are derived as sibling files under the same directory and
    // assume single-process ownership of that state directory; sharing one state path across multiple
    // chat-service processes is currently unsupported.
    public string? PendingActionsStorePath { get; set; }

    public static ServiceOptions Parse(string[] args, out string? error) {
        error = null;
        var options = new ServiceOptions();
        var clearOpenAIBasicAuthRequested = false;

        // Pre-scan for state/profile flags so we can load defaults before applying overrides.
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
            var arg = args[i] ?? string.Empty;

            (bool Success, string? Value, string? Error) ConsumeRuntimePolicyValue() {
                return TryConsume(args, ref i, out var value, out var valueError)
                    ? (true, value, null)
                    : (false, null, valueError);
            }

            if (!ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
                    argument: arg,
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

            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                continue;
            }

            if (arg is "--pipe") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.PipeName = value!;
                continue;
            }
            if (arg is "--model") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.Model = value!;
                continue;
            }
            if (arg is "--reasoning-effort") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                var parsed = ChatEnumParser.ParseReasoningEffort(value);
                if (!parsed.HasValue) {
                    error = "--reasoning-effort must be one of: minimal, low, medium, high, xhigh.";
                    return options;
                }
                options.ReasoningEffort = parsed;
                continue;
            }
            if (arg is "--reasoning-summary") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                var parsed = ChatEnumParser.ParseReasoningSummary(value);
                if (!parsed.HasValue) {
                    error = "--reasoning-summary must be one of: auto, concise, detailed, off.";
                    return options;
                }
                options.ReasoningSummary = parsed;
                continue;
            }
            if (arg is "--text-verbosity") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                var parsed = ChatEnumParser.ParseTextVerbosity(value);
                if (!parsed.HasValue) {
                    error = "--text-verbosity must be one of: low, medium, high.";
                    return options;
                }
                options.TextVerbosity = parsed;
                continue;
            }
            if (arg is "--temperature") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!double.TryParse(value, out var temp) || double.IsNaN(temp) || double.IsInfinity(temp) || temp < 0d || temp > 2d) {
                    error = "--temperature must be a number between 0 and 2.";
                    return options;
                }
                options.Temperature = temp;
                continue;
            }
            if (arg is "--openai-transport") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryParseTransport(value!, out var kind)) {
                    error = "--openai-transport must be one of: native, appserver, compatible-http, copilot-cli.";
                    return options;
                }
                options.OpenAITransport = kind;
                continue;
            }
            if (arg is "--openai-base-url") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.OpenAIBaseUrl = value;
                continue;
            }
            if (arg is "--openai-api-key") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.OpenAIApiKey = value;
                continue;
            }
            if (arg is "--openai-auth-mode") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryParseCompatibleAuthMode(value, out var authMode)) {
                    error = "--openai-auth-mode must be one of: bearer, basic, none.";
                    return options;
                }
                options.OpenAIAuthMode = authMode;
                continue;
            }
            if (arg is "--openai-basic-username") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.OpenAIBasicUsername = value;
                continue;
            }
            if (arg is "--openai-basic-password") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.OpenAIBasicPassword = value;
                continue;
            }
            if (arg is "--openai-account-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.OpenAIAccountId = value;
                continue;
            }
            if (arg is "--openai-clear-api-key") {
                options.OpenAIApiKey = null;
                continue;
            }
            if (arg is "--openai-clear-basic-auth") {
                options.OpenAIBasicUsername = null;
                options.OpenAIBasicPassword = null;
                clearOpenAIBasicAuthRequested = true;
                continue;
            }
            if (arg is "--openai-stream") {
                options.OpenAIStreaming = true;
                continue;
            }
            if (arg is "--openai-no-stream") {
                options.OpenAIStreaming = false;
                continue;
            }
            if (arg is "--openai-allow-insecure-http") {
                options.OpenAIAllowInsecureHttp = true;
                continue;
            }
            if (arg is "--openai-allow-insecure-http-non-loopback") {
                options.OpenAIAllowInsecureHttpNonLoopback = true;
                continue;
            }
            if (arg is "--profile") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--save-profile") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.SaveProfileName = value;
                continue;
            }
            if (arg is "--state-db") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.StateDbPath = value;
                continue;
            }
            if (arg is "--no-state-db") {
                options.NoStateDb = true;
                continue;
            }
            if (arg is "--allow-root") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.AllowedRoots.Add(value!);
                continue;
            }
            if (arg is "--instructions-file") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.InstructionsFile = value;
                continue;
            }
            if (arg is "--max-tool-rounds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < ChatRequestOptionLimits.MinToolRounds || n > MaxToolRoundsLimit) {
                    error = $"--max-tool-rounds must be between {ChatRequestOptionLimits.MinToolRounds} and {MaxToolRoundsLimit}.";
                    return options;
                }
                options.MaxToolRounds = n;
                continue;
            }
            if (arg is "--parallel-tools") {
                options.ParallelTools = true;
                continue;
            }
            if (arg is "--no-parallel-tools") {
                options.ParallelTools = false;
                continue;
            }
            if (arg is "--allow-mutating-parallel-tools") {
                options.AllowMutatingParallelToolCalls = true;
                continue;
            }
            if (arg is "--disallow-mutating-parallel-tools") {
                options.AllowMutatingParallelToolCalls = false;
                continue;
            }
            if (arg is "--turn-timeout-seconds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0) {
                    error = "--turn-timeout-seconds must be a non-negative integer.";
                    return options;
                }
                options.TurnTimeoutSeconds = n;
                continue;
            }
            if (arg is "--tool-timeout-seconds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0) {
                    error = "--tool-timeout-seconds must be a non-negative integer.";
                    return options;
                }
                options.ToolTimeoutSeconds = n;
                continue;
            }
            if (arg is "--session-execution-queue-limit") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0 || n > MaxSessionExecutionQueueLimit) {
                    error = $"--session-execution-queue-limit must be between 0 and {MaxSessionExecutionQueueLimit}.";
                    return options;
                }
                options.SessionExecutionQueueLimit = n;
                continue;
            }
            if (arg is "--global-execution-lane-concurrency") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0 || n > MaxGlobalExecutionLaneConcurrency) {
                    error = $"--global-execution-lane-concurrency must be between 0 and {MaxGlobalExecutionLaneConcurrency}.";
                    return options;
                }
                options.GlobalExecutionLaneConcurrency = n;
                continue;
            }
            if (arg is "--background-scheduler-daemon") {
                options.EnableBackgroundSchedulerDaemon = true;
                continue;
            }
            if (arg is "--no-background-scheduler-daemon") {
                options.EnableBackgroundSchedulerDaemon = false;
                continue;
            }
            if (arg is "--background-scheduler-start-paused") {
                options.BackgroundSchedulerStartPaused = true;
                continue;
            }
            if (arg is "--no-background-scheduler-start-paused") {
                options.BackgroundSchedulerStartPaused = false;
                options.BackgroundSchedulerStartupPauseSeconds = 0;
                continue;
            }
            if (arg is "--background-scheduler-start-paused-seconds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < MinBackgroundSchedulerStartupPauseSeconds || n > MaxBackgroundSchedulerStartupPauseSeconds) {
                    error = $"--background-scheduler-start-paused-seconds must be between {MinBackgroundSchedulerStartupPauseSeconds} and {MaxBackgroundSchedulerStartupPauseSeconds}.";
                    return options;
                }
                options.BackgroundSchedulerStartPaused = true;
                options.BackgroundSchedulerStartupPauseSeconds = n;
                continue;
            }
            if (arg is "--background-scheduler-maintenance-window") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryApplyBackgroundSchedulerMaintenanceWindow(options, value, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--clear-background-scheduler-maintenance-windows") {
                options.BackgroundSchedulerMaintenanceWindows.Clear();
                continue;
            }
            if (arg is "--background-scheduler-poll-seconds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < MinBackgroundSchedulerPollSeconds || n > MaxBackgroundSchedulerPollSeconds) {
                    error = $"--background-scheduler-poll-seconds must be between {MinBackgroundSchedulerPollSeconds} and {MaxBackgroundSchedulerPollSeconds}.";
                    return options;
                }
                options.BackgroundSchedulerPollSeconds = n;
                continue;
            }
            if (arg is "--background-scheduler-burst-limit") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < MinBackgroundSchedulerBurstLimit || n > MaxBackgroundSchedulerBurstLimit) {
                    error = $"--background-scheduler-burst-limit must be between {MinBackgroundSchedulerBurstLimit} and {MaxBackgroundSchedulerBurstLimit}.";
                    return options;
                }
                options.BackgroundSchedulerBurstLimit = n;
                continue;
            }
            if (arg is "--background-scheduler-failure-threshold") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < MinBackgroundSchedulerFailureThreshold || n > MaxBackgroundSchedulerFailureThreshold) {
                    error = $"--background-scheduler-failure-threshold must be between {MinBackgroundSchedulerFailureThreshold} and {MaxBackgroundSchedulerFailureThreshold}.";
                    return options;
                }
                options.BackgroundSchedulerFailureThreshold = n;
                continue;
            }
            if (arg is "--background-scheduler-failure-pause-seconds") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < MinBackgroundSchedulerFailurePauseSeconds || n > MaxBackgroundSchedulerFailurePauseSeconds) {
                    error = $"--background-scheduler-failure-pause-seconds must be between {MinBackgroundSchedulerFailurePauseSeconds} and {MaxBackgroundSchedulerFailurePauseSeconds}.";
                    return options;
                }
                options.BackgroundSchedulerFailurePauseSeconds = n;
                continue;
            }
            if (arg is "--background-scheduler-allow-pack-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryApplyBackgroundSchedulerPackFilter(options, value, allowed: true, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--background-scheduler-block-pack-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryApplyBackgroundSchedulerPackFilter(options, value, allowed: false, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--background-scheduler-allow-thread-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryApplyBackgroundSchedulerThreadFilter(options, value, allowed: true, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--background-scheduler-block-thread-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!TryApplyBackgroundSchedulerThreadFilter(options, value, allowed: false, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--ad-domain-controller") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.AdDomainController = value;
                continue;
            }
            if (arg is "--ad-search-base") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.AdDefaultSearchBaseDn = value;
                continue;
            }
            if (arg is "--ad-max-results") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 1) {
                    error = "--ad-max-results must be a positive integer.";
                    return options;
                }
                options.AdMaxResults = n;
                continue;
            }
            if (arg is "--enable-pack-id" or "--disable-pack-id") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }

                var enabled = arg is "--enable-pack-id";
                if (!TryApplyPackEnablement(options, value, enabled, arg, out error)) {
                    return options;
                }
                continue;
            }
            if (arg is "--powershell-allow-write") {
                options.PowerShellAllowWrite = true;
                continue;
            }
            if (arg is "--no-built-in-packs") {
                options.EnableBuiltInPackLoading = false;
                continue;
            }
            if (arg is "--built-in-packs") {
                options.EnableBuiltInPackLoading = true;
                continue;
            }
            if (arg is "--built-in-tool-assembly") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.BuiltInToolAssemblyNames.Add(value!);
                continue;
            }
            if (arg is "--built-in-tool-probe-path") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.BuiltInToolProbePaths.Add(value!);
                continue;
            }
            if (arg is "--enable-workspace-built-in-tool-output-probing") {
                options.EnableWorkspaceBuiltInToolOutputProbing = true;
                continue;
            }
            if (arg is "--disable-workspace-built-in-tool-output-probing") {
                options.EnableWorkspaceBuiltInToolOutputProbing = false;
                continue;
            }
            if (arg is "--no-default-built-in-tool-assemblies") {
                options.UseDefaultBuiltInToolAssemblyNames = false;
                continue;
            }
            if (arg is "--default-built-in-tool-assemblies") {
                options.UseDefaultBuiltInToolAssemblyNames = true;
                continue;
            }
            if (arg is "--plugin-path") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.RuntimePluginPaths.Add(value!);
                continue;
            }
            if (arg is "--no-default-plugin-paths") {
                options.EnableDefaultPluginPaths = false;
                continue;
            }
            if (arg is "--max-table-rows") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0) {
                    error = "--max-table-rows must be a non-negative integer.";
                    return options;
                }
                options.MaxTableRows = n;
                continue;
            }
            if (arg is "--max-sample") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var n) || n < 0) {
                    error = "--max-sample must be a non-negative integer.";
                    return options;
                }
                options.MaxSample = n;
                continue;
            }
            if (arg is "--redact") {
                options.Redact = true;
                continue;
            }
            if (arg is "--exit-on-disconnect") {
                options.ExitOnDisconnect = true;
                continue;
            }
            if (arg is "--parent-pid") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                if (!int.TryParse(value, out var pid) || pid <= 0) {
                    error = "--parent-pid must be a positive integer.";
                    return options;
                }
                options.ParentProcessId = pid;
                continue;
            }

            error = $"Unknown argument: {arg}";
            return options;
        }

        if (string.IsNullOrWhiteSpace(options.PipeName)) {
            error = "--pipe cannot be empty.";
        }
        options.OpenAIAccountId = string.IsNullOrWhiteSpace(options.OpenAIAccountId)
            ? null
            : options.OpenAIAccountId.Trim();
        options.OpenAIBasicUsername = string.IsNullOrWhiteSpace(options.OpenAIBasicUsername)
            ? null
            : options.OpenAIBasicUsername.Trim();
        if (string.IsNullOrWhiteSpace(options.OpenAIBasicPassword) && !clearOpenAIBasicAuthRequested) {
            options.OpenAIBasicPassword = Environment.GetEnvironmentVariable(ChatServiceEnvironmentVariables.OpenAIBasicPassword);
        }
        options.OpenAIBasicPassword = string.IsNullOrWhiteSpace(options.OpenAIBasicPassword)
            ? null
            : options.OpenAIBasicPassword.Trim();
        if (string.IsNullOrWhiteSpace(options.Model)) {
            error = "--model cannot be empty.";
        }
        if (string.IsNullOrWhiteSpace(error) && options.OpenAITransport == OpenAITransportKind.CompatibleHttp
            && !TryValidateCompatibleHttpBaseUrl(options, out error)) {
            return options;
        }

        if (string.IsNullOrWhiteSpace(error) && !options.NoStateDb && !string.IsNullOrWhiteSpace(options.SaveProfileName)) {
            if (!TrySaveProfile(options, out error)) {
                return options;
            }
        }

        return options;
    }

    internal IReadOnlyList<string> GetEffectivePluginPaths() {
        if (PluginPaths.Count == 0 && RuntimePluginPaths.Count == 0) {
            return Array.Empty<string>();
        }

        var effective = new List<string>(PluginPaths.Count + RuntimePluginPaths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendDistinctPluginPaths(effective, seen, PluginPaths);
        AppendDistinctPluginPaths(effective, seen, RuntimePluginPaths);
        return effective;
    }

    private static void AppendDistinctPluginPaths(List<string> destination, HashSet<string> seen, IReadOnlyList<string> source) {
        for (var i = 0; i < source.Count; i++) {
            var candidate = (source[i] ?? string.Empty).Trim();
            if (candidate.Length == 0 || !seen.Add(candidate)) {
                continue;
            }

            destination.Add(candidate);
        }
    }

    public static void WriteHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  IntelligenceX.Chat.Service [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --pipe <NAME>           Named pipe name (default: intelligencex.chat)");
        Console.WriteLine($"  --model <NAME>          OpenAI model (default: {OpenAIModelCatalog.DefaultModel})");
        Console.WriteLine("  --reasoning-effort <LEVEL>   Reasoning effort hint: minimal|low|medium|high|xhigh.");
        Console.WriteLine("  --reasoning-summary <LEVEL>  Reasoning summary hint: auto|concise|detailed|off.");
        Console.WriteLine("  --text-verbosity <LEVEL>     Text verbosity hint: low|medium|high.");
        Console.WriteLine("  --temperature <N>       Sampling temperature (0-2).");
        Console.WriteLine("  --openai-transport <KIND>  Underlying provider transport: native|appserver|compatible-http|copilot-cli (default: native).");
        Console.WriteLine("  --openai-base-url <URL> Base URL for compatible-http (example: http://127.0.0.1:11434 or http://127.0.0.1:11434/v1).");
        Console.WriteLine("  --openai-auth-mode <MODE>  Compatible-http auth mode: bearer|basic|none (default: bearer).");
        Console.WriteLine("  --openai-api-key <KEY>  Optional Bearer token for compatible-http.");
        Console.WriteLine("  --openai-basic-username <NAME>  Optional Basic auth username for compatible-http.");
        Console.WriteLine("  --openai-basic-password <SECRET>  Optional Basic auth password for compatible-http (legacy; prefer INTELLIGENCEX_OPENAI_BASIC_PASSWORD env var).");
        Console.WriteLine("  --openai-account-id <ID>  Native ChatGPT account id to pin when multiple auth bundles exist.");
        Console.WriteLine("  --openai-clear-api-key Clear any saved compatible-http API key when profile overrides are saved.");
        Console.WriteLine("  --openai-clear-basic-auth Clear any saved compatible-http basic auth username/password when profile overrides are saved.");
        Console.WriteLine("  --openai-stream         Request streaming responses (default: on).");
        Console.WriteLine("  --openai-no-stream      Disable streaming responses.");
        Console.WriteLine("  --openai-allow-insecure-http  Allow http:// base URLs for loopback hosts (default: off).");
        Console.WriteLine("  --openai-allow-insecure-http-non-loopback  Allow http:// base URLs for non-loopback hosts (dangerous).");
        Console.WriteLine("  --profile <NAME>        Load a saved service profile or built-in preset (for example: plugin-only).");
        Console.WriteLine("  --save-profile <NAME>   Save the effective options as a named profile (SQLite-backed).");
        Console.WriteLine("  --state-db <PATH>       Override the SQLite state DB path (defaults to LocalAppData).");
        Console.WriteLine("  --no-state-db           Disable SQLite state storage (saved profiles unavailable; built-in presets still available).");
        Console.WriteLine("  --allow-root <PATH>     Allow filesystem/evtx operations under PATH (repeatable).");
        Console.WriteLine("  --instructions-file <PATH>  Load system instructions from a file (default: bundled HostSystemPrompt.md).");
        Console.WriteLine(BuildMaxToolRoundsHelpLine());
        Console.WriteLine("  --parallel-tools        Execute tool calls in parallel when possible (default: on).");
        Console.WriteLine("  --no-parallel-tools     Disable parallel tool calls.");
        Console.WriteLine("  --allow-mutating-parallel-tools  Allow mutating/write-capable tool calls to run in parallel (default: off).");
        Console.WriteLine("  --disallow-mutating-parallel-tools  Disable mutating parallel override.");
        Console.WriteLine("  --turn-timeout-seconds <N>  Per-turn timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --tool-timeout-seconds <N>  Per-tool timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --session-execution-queue-limit <N>  Max queued chat turns per session (0 = unlimited; default: 32).");
        Console.WriteLine("  --global-execution-lane-concurrency <N>  Global chat turn concurrency across sessions (0 = disabled; default: 0).");
        Console.WriteLine("  --background-scheduler-daemon  Enable headless read-only background follow-up execution (default: off).");
        Console.WriteLine("  --no-background-scheduler-daemon  Disable headless background follow-up execution.");
        Console.WriteLine("  --background-scheduler-start-paused  Start the scheduler in a manual paused state until resumed.");
        Console.WriteLine($"  --background-scheduler-start-paused-seconds <N>  Start the scheduler paused for a bounded window ({MinBackgroundSchedulerStartupPauseSeconds}..{MaxBackgroundSchedulerStartupPauseSeconds}).");
        Console.WriteLine("  --no-background-scheduler-start-paused  Clear startup manual-pause behavior.");
        Console.WriteLine("  --background-scheduler-maintenance-window <SPEC>  Add a recurring local maintenance window (repeatable; format: daily@HH:mm/<minutes> or mon@HH:mm/<minutes>).");
        Console.WriteLine("  --clear-background-scheduler-maintenance-windows  Clear configured scheduler maintenance windows.");
        Console.WriteLine($"  --background-scheduler-poll-seconds <N>  Idle poll interval for the background scheduler ({MinBackgroundSchedulerPollSeconds}..{MaxBackgroundSchedulerPollSeconds}; default: 30).");
        Console.WriteLine($"  --background-scheduler-burst-limit <N>  Max background follow-up items processed per scheduler cycle ({MinBackgroundSchedulerBurstLimit}..{MaxBackgroundSchedulerBurstLimit}; default: 4).");
        Console.WriteLine($"  --background-scheduler-failure-threshold <N>  Consecutive non-success outcomes before the daemon pauses itself ({MinBackgroundSchedulerFailureThreshold}..{MaxBackgroundSchedulerFailureThreshold}; default: 5; 0 disables auto-pause).");
        Console.WriteLine($"  --background-scheduler-failure-pause-seconds <N>  Auto-pause duration after a background scheduler failure threshold is hit ({MinBackgroundSchedulerFailurePauseSeconds}..{MaxBackgroundSchedulerFailurePauseSeconds}; default: 300).");
        Console.WriteLine("  --background-scheduler-allow-pack-id <ID>  Allow only selected target packs for daemon background execution (repeatable).");
        Console.WriteLine("  --background-scheduler-block-pack-id <ID>  Block selected target packs from daemon background execution (repeatable; takes precedence).");
        Console.WriteLine("  --background-scheduler-allow-thread-id <ID>  Allow only selected thread ids for daemon background execution (repeatable).");
        Console.WriteLine("  --background-scheduler-block-thread-id <ID>  Block selected thread ids from daemon background execution (repeatable; takes precedence).");
        Console.WriteLine("  --ad-domain-controller  Active Directory domain controller host/FQDN (optional).");
        Console.WriteLine("  --ad-search-base        Active Directory base DN (optional; defaultNamingContext used otherwise).");
        Console.WriteLine("  --ad-max-results <N>    Max results returned by AD tools (default: 1000).");
        Console.WriteLine("  --enable-pack-id <ID>   Enable a tool pack by normalized pack id (repeatable).");
        Console.WriteLine("  --disable-pack-id <ID>  Disable a tool pack by normalized pack id (repeatable).");
        Console.WriteLine("                          Pack ids come from runtime metadata (built-in + plugin packs).");
        Console.WriteLine("  --powershell-allow-write  Allow read_write intent in IX.PowerShell tools (default: off).");
        Console.WriteLine("  --no-built-in-packs    Disable built-in pack loading (plugin-only mode).");
        Console.WriteLine("  --built-in-packs       Enable built-in pack loading (default: on).");
        Console.WriteLine("  --built-in-tool-assembly <NAME> Additional built-in tool assembly name to include (repeatable).");
        Console.WriteLine("  --built-in-tool-probe-path <PATH> Runtime-only built-in tool assembly probe root (repeatable; not persisted to profiles).");
        Console.WriteLine("  --no-default-built-in-tool-assemblies Disable built-in discovery from Chat's default assembly allowlist.");
        Console.WriteLine("  --default-built-in-tool-assemblies Re-enable built-in discovery from Chat's default assembly allowlist.");
        Console.WriteLine("  --plugin-path <PATH>    Runtime-only folder-based plugin path (repeatable; not persisted to profiles).");
        Console.WriteLine("  --no-default-plugin-paths Disable default plugin paths (%LOCALAPPDATA% and app ./plugins).");
        ToolRuntimePolicyBootstrap.WriteRuntimePolicyCliHelp(Console.WriteLine);
        Console.WriteLine("  --max-table-rows <N>    Max rows to show in table-like output (0 = no limit; default: 0).");
        Console.WriteLine("  --max-sample <N>        Max sample items to show from long lists (0 = no limit; default: 0).");
        Console.WriteLine("  --redact                Best-effort redact output for display/logging (default: off).");
        Console.WriteLine("  --exit-on-disconnect    Exit when parent app disconnects (runtime-managed mode).");
        Console.WriteLine("  --parent-pid <PID>      Parent process id used with --exit-on-disconnect.");
        Console.WriteLine("  -h, --help              Show help.");
    }

    internal static string BuildMaxToolRoundsHelpLine() {
        return
            $"  --max-tool-rounds <N>   Max tool-call rounds per user message ({ChatRequestOptionLimits.MinToolRounds}..{ChatRequestOptionLimits.MaxToolRounds}; default: {ChatRequestOptionLimits.DefaultToolRounds}).";
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

    private static bool TryParseCompatibleAuthMode(string? value, out OpenAICompatibleHttpAuthMode mode) {
        mode = OpenAICompatibleHttpAuthMode.Bearer;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "":
            case "bearer":
            case "api-key":
            case "apikey":
            case "token":
                mode = OpenAICompatibleHttpAuthMode.Bearer;
                return true;
            case "basic":
                mode = OpenAICompatibleHttpAuthMode.Basic;
                return true;
            case "none":
            case "off":
                mode = OpenAICompatibleHttpAuthMode.None;
                return true;
            default:
                return false;
        }
    }

    private static bool TryValidateCompatibleHttpBaseUrl(ServiceOptions options, out string? error) {
        error = null;
        try {
            // Centralize validation behavior on the transport options, so CLI/config/runtime share the same rules.
            var compatible = new OpenAICompatibleHttpOptions {
                BaseUrl = options.OpenAIBaseUrl,
                AuthMode = options.OpenAIAuthMode,
                BasicUsername = options.OpenAIBasicUsername,
                BasicPassword = options.OpenAIBasicPassword,
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

    internal static string GetDefaultStateDbPath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }
        return Path.Combine(root, "IntelligenceX.Chat", "state.db");
    }

    internal static string GetDefaultToolingBootstrapCachePath(string? stateDbPath = null) {
        var normalizedStateDbPath = (stateDbPath ?? string.Empty).Trim();
        if (normalizedStateDbPath.Length > 0) {
            try {
                var stateDbFullPath = Path.GetFullPath(normalizedStateDbPath);
                var stateDbDirectory = Path.GetDirectoryName(stateDbFullPath);
                if (!string.IsNullOrWhiteSpace(stateDbDirectory)) {
                    return Path.Combine(stateDbDirectory, "tooling-bootstrap-cache-v1.json");
                }
            } catch {
                // Fall through to LocalAppData fallback.
            }
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "tooling-bootstrap-cache-v1.json");
    }

    internal static bool TryApplyPackEnablement(
        ServiceOptions options,
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

    internal static bool TryApplyBackgroundSchedulerPackFilter(
        ServiceOptions options,
        string? rawPackId,
        bool allowed,
        string argumentName,
        out string? error) {
        error = null;
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(rawPackId);
        if (normalizedPackId.Length == 0) {
            error = $"{argumentName} requires a non-empty pack id.";
            return false;
        }

        if (allowed) {
            RemovePackId(options.BackgroundSchedulerBlockedPackIds, normalizedPackId);
            AddPackIdIfMissing(options.BackgroundSchedulerAllowedPackIds, normalizedPackId);
        } else {
            RemovePackId(options.BackgroundSchedulerAllowedPackIds, normalizedPackId);
            AddPackIdIfMissing(options.BackgroundSchedulerBlockedPackIds, normalizedPackId);
        }

        return true;
    }

    internal static bool TryApplyBackgroundSchedulerThreadFilter(
        ServiceOptions options,
        string? rawThreadId,
        bool allowed,
        string argumentName,
        out string? error) {
        error = null;
        var normalizedThreadId = NormalizeBackgroundSchedulerThreadId(rawThreadId);
        if (normalizedThreadId.Length == 0) {
            error = $"{argumentName} requires a non-empty thread id.";
            return false;
        }

        if (allowed) {
            RemoveThreadId(options.BackgroundSchedulerBlockedThreadIds, normalizedThreadId);
            AddThreadIdIfMissing(options.BackgroundSchedulerAllowedThreadIds, normalizedThreadId);
        } else {
            RemoveThreadId(options.BackgroundSchedulerAllowedThreadIds, normalizedThreadId);
            AddThreadIdIfMissing(options.BackgroundSchedulerBlockedThreadIds, normalizedThreadId);
        }

        return true;
    }

    internal static bool TryApplyBackgroundSchedulerMaintenanceWindow(
        ServiceOptions options,
        string? rawSpec,
        string argumentName,
        out string? error) {
        ArgumentNullException.ThrowIfNull(options);

        error = null;
        if (!ChatServiceBackgroundSchedulerControlState.TryNormalizeMaintenanceWindowSpec(rawSpec, out var normalizedSpec, out error)) {
            error = $"{argumentName} {error}";
            return false;
        }

        for (var i = 0; i < options.BackgroundSchedulerMaintenanceWindows.Count; i++) {
            if (string.Equals(options.BackgroundSchedulerMaintenanceWindows[i], normalizedSpec, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        if (options.BackgroundSchedulerMaintenanceWindows.Count >= MaxBackgroundSchedulerMaintenanceWindows) {
            error = $"{argumentName} supports at most {MaxBackgroundSchedulerMaintenanceWindows} windows.";
            return false;
        }

        options.BackgroundSchedulerMaintenanceWindows.Add(normalizedSpec);
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

    private static string NormalizeBackgroundSchedulerThreadId(string? threadId) {
        return (threadId ?? string.Empty).Trim();
    }

    private static void RemoveThreadId(List<string> threadIds, string normalizedThreadId) {
        for (var i = threadIds.Count - 1; i >= 0; i--) {
            if (string.Equals(NormalizeBackgroundSchedulerThreadId(threadIds[i]), normalizedThreadId, StringComparison.Ordinal)) {
                threadIds.RemoveAt(i);
            }
        }
    }

    private static bool ContainsThreadId(List<string> threadIds, string normalizedThreadId) {
        for (var i = 0; i < threadIds.Count; i++) {
            if (string.Equals(NormalizeBackgroundSchedulerThreadId(threadIds[i]), normalizedThreadId, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static void AddThreadIdIfMissing(List<string> threadIds, string normalizedThreadId) {
        if (!ContainsThreadId(threadIds, normalizedThreadId)) {
            threadIds.Add(normalizedThreadId);
        }
    }

}
