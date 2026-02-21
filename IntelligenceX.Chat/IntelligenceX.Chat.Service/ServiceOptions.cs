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
    public bool ShowHelp { get; set; }
    public string PipeName { get; set; } = "intelligencex.chat";
    public string Model { get; set; } = "gpt-5.3-codex";

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

    public int MaxToolRounds { get; set; } = 24;
    public bool ParallelTools { get; set; } = true;
    public int TurnTimeoutSeconds { get; set; }
    public int ToolTimeoutSeconds { get; set; }
    public List<string> AllowedRoots { get; } = new();

    public string? AdDomainController { get; set; }
    public string? AdDefaultSearchBaseDn { get; set; }
    public int AdMaxResults { get; set; } = 1000;
    public bool EnablePowerShellPack { get; set; }
    public bool PowerShellAllowWrite { get; set; }
    public bool EnableTestimoXPack { get; set; } = true;
    public bool EnableOfficeImoPack { get; set; } = true;
    public bool EnableDefaultPluginPaths { get; set; } = true;
    public List<string> PluginPaths { get; } = new();
    public ToolWriteGovernanceMode WriteGovernanceMode { get; set; } = ToolWriteGovernanceMode.Enforced;
    public bool RequireWriteGovernanceRuntime { get; set; } = true;
    public bool RequireWriteAuditSinkForWriteOperations { get; set; }
    public ToolWriteAuditSinkMode WriteAuditSinkMode { get; set; } = ToolWriteAuditSinkMode.None;
    public string? WriteAuditSinkPath { get; set; }
    public ToolAuthenticationRuntimePreset AuthenticationRuntimePreset { get; set; } = ToolAuthenticationRuntimePreset.Default;
    public bool RequireAuthenticationRuntime { get; set; }
    public string? RunAsProfilePath { get; set; }
    public string? AuthenticationProfilePath { get; set; }

    ToolAuthenticationRuntimePreset IToolRuntimePolicySettings.AuthenticationRuntimePreset => AuthenticationRuntimePreset;
    IReadOnlyList<string> IToolPackRuntimeSettings.AllowedRoots => AllowedRoots;
    IReadOnlyList<string> IToolPackRuntimeSettings.PluginPaths => PluginPaths;

    public string? InstructionsFile { get; set; }
    public int MaxTableRows { get; set; }
    public int MaxSample { get; set; }
    public bool Redact { get; set; }
    public bool ExitOnDisconnect { get; set; }
    public int? ParentProcessId { get; set; }

    // Optional override for where the chat service persists pending-action proposals (for /act <id> rehydration).
    // When unset, the service uses a LocalAppData-based default path.
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

        if (!options.NoStateDb && !string.IsNullOrWhiteSpace(options.ProfileName)) {
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
                options.ProfileName = value;
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
                if (!int.TryParse(value, out var n) || n < 1) {
                    error = "--max-tool-rounds must be a positive integer.";
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
            if (arg is "--enable-powershell-pack") {
                options.EnablePowerShellPack = true;
                continue;
            }
            if (arg is "--disable-powershell-pack") {
                options.EnablePowerShellPack = false;
                continue;
            }
            if (arg is "--powershell-allow-write") {
                options.PowerShellAllowWrite = true;
                continue;
            }
            if (arg is "--enable-testimox-pack") {
                options.EnableTestimoXPack = true;
                continue;
            }
            if (arg is "--disable-testimox-pack") {
                options.EnableTestimoXPack = false;
                continue;
            }
            if (arg is "--enable-officeimo-pack") {
                options.EnableOfficeImoPack = true;
                continue;
            }
            if (arg is "--disable-officeimo-pack") {
                options.EnableOfficeImoPack = false;
                continue;
            }
            if (arg is "--plugin-path") {
                if (!TryConsume(args, ref i, out var value, out error)) {
                    return options;
                }
                options.PluginPaths.Add(value!);
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

    public static void WriteHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  IntelligenceX.Chat.Service [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --pipe <NAME>           Named pipe name (default: intelligencex.chat)");
        Console.WriteLine("  --model <NAME>          OpenAI model (default: gpt-5.3-codex)");
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
        Console.WriteLine("  --profile <NAME>        Load a saved service profile (SQLite-backed) and apply it as defaults.");
        Console.WriteLine("  --save-profile <NAME>   Save the effective options as a named profile (SQLite-backed).");
        Console.WriteLine("  --state-db <PATH>       Override the SQLite state DB path (defaults to LocalAppData).");
        Console.WriteLine("  --no-state-db           Disable SQLite state storage (profiles unavailable).");
        Console.WriteLine("  --allow-root <PATH>     Allow filesystem/evtx operations under PATH (repeatable).");
        Console.WriteLine("  --instructions-file <PATH>  Load system instructions from a file (default: bundled HostSystemPrompt.md).");
        Console.WriteLine("  --max-tool-rounds <N>   Max tool-call rounds per user message (default: 24).");
        Console.WriteLine("  --parallel-tools        Execute tool calls in parallel when possible (default: on).");
        Console.WriteLine("  --no-parallel-tools     Disable parallel tool calls.");
        Console.WriteLine("  --turn-timeout-seconds <N>  Per-turn timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --tool-timeout-seconds <N>  Per-tool timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --ad-domain-controller  Active Directory domain controller host/FQDN (optional).");
        Console.WriteLine("  --ad-search-base        Active Directory base DN (optional; defaultNamingContext used otherwise).");
        Console.WriteLine("  --ad-max-results <N>    Max results returned by AD tools (default: 1000).");
        Console.WriteLine("  --enable-powershell-pack  Enable dangerous IX.PowerShell runtime tools (default: off).");
        Console.WriteLine("  --disable-powershell-pack Disable IX.PowerShell runtime tools.");
        Console.WriteLine("  --powershell-allow-write  Allow read_write intent in IX.PowerShell tools (default: off).");
        Console.WriteLine("  --enable-testimox-pack  Enable IX.TestimoX diagnostics tools (default: on).");
        Console.WriteLine("  --disable-testimox-pack Disable IX.TestimoX diagnostics tools.");
        Console.WriteLine("  --enable-officeimo-pack  Enable IX.OfficeIMO document ingestion tools (default: on).");
        Console.WriteLine("  --disable-officeimo-pack Disable IX.OfficeIMO document ingestion tools.");
        Console.WriteLine("  --plugin-path <PATH>    Additional folder-based plugin path (repeatable).");
        Console.WriteLine("  --no-default-plugin-paths Disable default plugin paths (%LOCALAPPDATA% and app ./plugins).");
        ToolRuntimePolicyBootstrap.WriteRuntimePolicyCliHelp(Console.WriteLine);
        Console.WriteLine("  --max-table-rows <N>    Max rows to show in table-like output (0 = no limit; default: 0).");
        Console.WriteLine("  --max-sample <N>        Max sample items to show from long lists (0 = no limit; default: 0).");
        Console.WriteLine("  --redact                Best-effort redact output for display/logging (default: off).");
        Console.WriteLine("  --exit-on-disconnect    Exit when parent app disconnects (runtime-managed mode).");
        Console.WriteLine("  --parent-pid <PID>      Parent process id used with --exit-on-disconnect.");
        Console.WriteLine("  -h, --help              Show help.");
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

}
