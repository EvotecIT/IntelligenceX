using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using IntelligenceX.Chat.Service.Profiles;

namespace IntelligenceX.Chat.Service;

internal sealed class ServiceOptions {
    public bool ShowHelp { get; set; }
    public string PipeName { get; set; } = "intelligencex.chat";
    public string Model { get; set; } = "gpt-5.3-codex";

    public string? ProfileName { get; set; }
    public string? SaveProfileName { get; set; }
    public string? StateDbPath { get; set; }
    public bool NoStateDb { get; set; }

    public int MaxToolRounds { get; set; } = 3;
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
    public bool EnableDefaultPluginPaths { get; set; } = true;
    public List<string> PluginPaths { get; } = new();

    public string? InstructionsFile { get; set; }
    public int MaxTableRows { get; set; } = 20;
    public int MaxSample { get; set; } = 10;
    public bool Redact { get; set; }
    public bool ExitOnDisconnect { get; set; }
    public int? ParentProcessId { get; set; }

    public static ServiceOptions Parse(string[] args, out string? error) {
        error = null;
        var options = new ServiceOptions();

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
        if (string.IsNullOrWhiteSpace(options.Model)) {
            error = "--model cannot be empty.";
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
        Console.WriteLine("  --profile <NAME>        Load a saved service profile (SQLite-backed) and apply it as defaults.");
        Console.WriteLine("  --save-profile <NAME>   Save the effective options as a named profile (SQLite-backed).");
        Console.WriteLine("  --state-db <PATH>       Override the SQLite state DB path (defaults to LocalAppData).");
        Console.WriteLine("  --no-state-db           Disable SQLite state storage (profiles unavailable).");
        Console.WriteLine("  --allow-root <PATH>     Allow filesystem/evtx operations under PATH (repeatable).");
        Console.WriteLine("  --instructions-file <PATH>  Load system instructions from a file (default: bundled HostSystemPrompt.md).");
        Console.WriteLine("  --max-tool-rounds <N>   Max tool-call rounds per user message (default: 3).");
        Console.WriteLine("  --parallel-tools        Execute tool calls in parallel when possible (default: on).");
        Console.WriteLine("  --no-parallel-tools     Disable parallel tool calls.");
        Console.WriteLine("  --turn-timeout-seconds <N>  Per-turn timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --tool-timeout-seconds <N>  Per-tool timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --ad-domain-controller  Active Directory domain controller host/FQDN (optional).");
        Console.WriteLine("  --ad-search-base        Active Directory base DN (optional; defaultNamingContext used otherwise).");
        Console.WriteLine("  --ad-max-results <N>    Max results returned by AD tools (default: 1000).");
        Console.WriteLine("  --enable-powershell-pack  Enable dangerous IX.PowerShell runtime tools (default: off).");
        Console.WriteLine("  --powershell-allow-write  Allow read_write intent in IX.PowerShell tools (default: off).");
        Console.WriteLine("  --enable-testimox-pack  Enable IX.TestimoX diagnostics tools (default: on).");
        Console.WriteLine("  --disable-testimox-pack Disable IX.TestimoX diagnostics tools.");
        Console.WriteLine("  --plugin-path <PATH>    Additional folder-based plugin path (repeatable).");
        Console.WriteLine("  --no-default-plugin-paths Disable default plugin paths (%LOCALAPPDATA% and app ./plugins).");
        Console.WriteLine("  --max-table-rows <N>    Max rows to show in table-like output (0 = no limit; default: 20).");
        Console.WriteLine("  --max-sample <N>        Max sample items to show from long lists (0 = no limit; default: 10).");
        Console.WriteLine("  --redact                Best-effort redact output for display/logging (default: off).");
        Console.WriteLine("  --exit-on-disconnect    Exit when parent app disconnects (runtime-managed mode).");
        Console.WriteLine("  --parent-pid <PID>      Parent process id used with --exit-on-disconnect.");
        Console.WriteLine("  -h, --help              Show help.");
    }

    internal static string GetDefaultStateDbPath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }
        return Path.Combine(root, "IntelligenceX.Chat", "state.db");
    }

    internal void ApplyProfile(ServiceProfile profile) {
        if (profile == null) {
            return;
        }

        Model = profile.Model ?? Model;
        MaxToolRounds = profile.MaxToolRounds;
        ParallelTools = profile.ParallelTools;
        TurnTimeoutSeconds = profile.TurnTimeoutSeconds;
        ToolTimeoutSeconds = profile.ToolTimeoutSeconds;

        AllowedRoots.Clear();
        if (profile.AllowedRoots != null && profile.AllowedRoots.Count > 0) {
            AllowedRoots.AddRange(profile.AllowedRoots);
        }

        AdDomainController = profile.AdDomainController;
        AdDefaultSearchBaseDn = profile.AdDefaultSearchBaseDn;
        AdMaxResults = profile.AdMaxResults;
        EnablePowerShellPack = profile.EnablePowerShellPack;
        EnableTestimoXPack = profile.EnableTestimoXPack;
        EnableDefaultPluginPaths = profile.EnableDefaultPluginPaths;
        PluginPaths.Clear();
        if (profile.PluginPaths != null && profile.PluginPaths.Count > 0) {
            PluginPaths.AddRange(profile.PluginPaths);
        }

        InstructionsFile = profile.InstructionsFile;
        MaxTableRows = profile.MaxTableRows;
        MaxSample = profile.MaxSample;
        Redact = profile.Redact;
    }

    internal ServiceProfile ToProfile() {
        return new ServiceProfile {
            Model = Model,
            MaxToolRounds = MaxToolRounds,
            ParallelTools = ParallelTools,
            TurnTimeoutSeconds = TurnTimeoutSeconds,
            ToolTimeoutSeconds = ToolTimeoutSeconds,
            AllowedRoots = new List<string>(AllowedRoots),
            AdDomainController = AdDomainController,
            AdDefaultSearchBaseDn = AdDefaultSearchBaseDn,
            AdMaxResults = AdMaxResults,
            EnablePowerShellPack = EnablePowerShellPack,
            EnableTestimoXPack = EnableTestimoXPack,
            EnableDefaultPluginPaths = EnableDefaultPluginPaths,
            PluginPaths = new List<string>(PluginPaths),
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
            error = $"Profile not found: {name}";
            return false;
        }
        options.ApplyProfile(profile);
        return true;
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
