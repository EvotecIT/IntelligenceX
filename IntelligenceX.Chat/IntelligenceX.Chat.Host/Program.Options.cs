using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

    private sealed class ReplOptions {
        public string Model { get; set; } = "gpt-5.3-codex";

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
        public bool ParallelToolCalls { get; set; }
        public int MaxToolRounds { get; set; } = 24;
        public int TurnTimeoutSeconds { get; set; }
        public int ToolTimeoutSeconds { get; set; }
        public List<string> AllowedRoots { get; } = new();
        public bool EchoToolOutputs { get; set; }
        public int MaxConsoleToolOutputChars { get; set; } = 2000;
        public bool ShowToolIds { get; set; }
        public bool LiveProgress { get; set; } = true;
        public int MaxTableRows { get; set; } = 20;
        public int MaxSample { get; set; } = 10;
        public bool Redact { get; set; }
        public string? AuthPath { get; set; }
        public string? InstructionsFile { get; set; }
        public string? AdDomainController { get; set; }
        public string? AdDefaultSearchBaseDn { get; set; }
        public int AdMaxResults { get; set; } = 1000;
        public bool EnablePowerShellPack { get; set; }
        public bool EnableTestimoXPack { get; set; } = true;

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
                            error = "--openai-transport must be one of: native, appserver, compatible-http.";
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
                        options.ProfileName = profileName;
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
                    case "--enable-powershell-pack":
                        options.EnablePowerShellPack = true;
                        break;
                    case "--enable-testimox-pack":
                        options.EnableTestimoXPack = true;
                        break;
                    case "--disable-testimox-pack":
                        options.EnableTestimoXPack = false;
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
                        if (!int.TryParse(rounds, out var n) || n <= 0) {
                            error = "Invalid --max-tool-rounds value.";
                            return options;
                        }
                        options.MaxToolRounds = n;
                        break;
                    case "--parallel-tools":
                        options.ParallelToolCalls = true;
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
                var profile = store.GetAsync(name, CancellationToken.None).GetAwaiter().GetResult();
                if (profile is null) {
                    error = $"Profile not found: {name}";
                    return false;
                }
                options.ApplyProfile(profile);
                options.ProfileName = name;
                return true;
            } catch (Exception ex) {
                error = $"Failed to load profile '{name}': {ex.Message}";
                return false;
            }
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

            MaxToolRounds = profile.MaxToolRounds;
            ParallelToolCalls = profile.ParallelTools;
            TurnTimeoutSeconds = profile.TurnTimeoutSeconds;
            ToolTimeoutSeconds = profile.ToolTimeoutSeconds;

            AllowedRoots.Clear();
            if (profile.AllowedRoots is { Count: > 0 }) {
                AllowedRoots.AddRange(profile.AllowedRoots);
            }

            AdDomainController = profile.AdDomainController;
            AdDefaultSearchBaseDn = profile.AdDefaultSearchBaseDn;
            AdMaxResults = profile.AdMaxResults;
            EnablePowerShellPack = profile.EnablePowerShellPack;
            EnableTestimoXPack = profile.EnableTestimoXPack;

            InstructionsFile = profile.InstructionsFile;
            MaxTableRows = profile.MaxTableRows;
            MaxSample = profile.MaxSample;
            Redact = profile.Redact;
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
