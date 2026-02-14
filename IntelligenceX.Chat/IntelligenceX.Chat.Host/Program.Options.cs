using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
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

            return options;
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
    }
}
