using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Analysis;

internal static class AnalyzeHotspotsCommand {
    public static Task<int> RunAsync(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return Task.FromResult(0);
        }

        // `--help` must short-circuit reliably (no filesystem reads/writes), regardless of flag position.
        if (args.Any(IsHelp)) {
            var command = args[0].ToLowerInvariant();
            if (command == "sync-state") {
                PrintSyncHelp();
            } else if (command == "set") {
                PrintSetHelp();
            } else {
                PrintHelp();
            }
            return Task.FromResult(0);
        }

        var commandName = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return commandName switch {
            "sync-state" => Task.FromResult(SyncState(rest)),
            "set" => Task.FromResult(SetStatus(rest)),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    public static void PrintHelp() {
        PrintSyncHelp();
        PrintSetHelp();
    }

    private static void PrintSyncHelp() {
        Console.WriteLine("  intelligencex analyze hotspots sync-state [--workspace <path>] [--config <path>] [--state <path>] [--new-status <status>] [--prune-fixed] [--dry-run] [--check]");
        Console.WriteLine();
        Console.WriteLine("Hotspot statuses: to-review, safe, fixed, accepted-risk, wont-fix, suppress");
    }

    private static void PrintSetHelp() {
        Console.WriteLine("  intelligencex analyze hotspots set --key <key> [--keys <k1,k2>] --status <status> [--note <text>] [--workspace <path>] [--config <path>] [--state <path>]");
        Console.WriteLine();
        Console.WriteLine("Hotspot statuses: to-review, safe, fixed, accepted-risk, wont-fix, suppress");
    }

    private static int SyncState(string[] args) {
        var options = ParseSyncArgs(args, out var error);
        if (options.ShowHelp) {
            PrintSyncHelp();
            return 0;
        }
        if (error is not null) {
            Console.WriteLine(error);
            return 1;
        }

        var workspace = AnalyzeRunner.ResolveWorkspace(options.Workspace);
        var configPath = AnalyzeRunner.ResolveConfigPath(options.ConfigPath, workspace);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            Console.WriteLine($"Config not found: {configPath ?? "<null>"}");
            return 1;
        }

        if (!TryLoadAnalysisSettings(configPath, out var settings, out var parseError)) {
            Console.WriteLine(parseError);
            return 1;
        }

        var resolvedStatePath = ResolveStatePath(workspace, options.StatePath ?? settings.Hotspots.StatePath);
        var desiredDefaultStatus = AnalysisHotspots.NormalizeStatus(options.NewStatus ?? "to-review");
        if (string.IsNullOrWhiteSpace(desiredDefaultStatus)) {
            desiredDefaultStatus = "to-review";
        }

        // Use the reviewer loader so SARIF parsing + rule id mapping stays consistent.
        var reviewSettings = new ReviewSettings();
        reviewSettings.Analysis.Enabled = true;
        reviewSettings.Analysis.Results.Inputs = settings.Results.Inputs;
        reviewSettings.Analysis.Results.MinSeverity = settings.Results.MinSeverity;
        reviewSettings.Analysis.DisabledRules = settings.DisabledRules;
        reviewSettings.Analysis.SeverityOverrides = settings.SeverityOverrides;

        var originalCwd = Environment.CurrentDirectory;
        AnalysisLoadResult load;
        try {
            Environment.CurrentDirectory = workspace;
            load = AnalysisFindingsLoader.LoadWithReport(reviewSettings, Array.Empty<PullRequestFile>());
        } finally {
            Environment.CurrentDirectory = originalCwd;
        }
        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        var hotspotFindings = load.Findings
            .Where(f => !string.IsNullOrWhiteSpace(f.RuleId) &&
                        catalog.TryGetRule(f.RuleId!, out var rule) &&
                        string.Equals(rule.Type, "security-hotspot", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var keys = hotspotFindings.Select(AnalysisHotspots.ComputeHotspotKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stateFile = HotspotStateStore.TryLoad(resolvedStatePath);
        var existing = stateFile.Items;
        var existingMap = HotspotStateStore.ToMap(existing);
        var missingKeys = keys.Where(key => !existingMap.ContainsKey(key)).ToList();

        var next = HotspotStateStore.MergeMissing(existing, missingKeys, desiredDefaultStatus);
        if (options.PruneFixed) {
            var presentSet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            next = HotspotStateStore.PruneMissing(next, presentSet, entry =>
                string.Equals(AnalysisHotspots.NormalizeStatus(entry.Status), "fixed", StringComparison.OrdinalIgnoreCase));
        }

        Console.WriteLine($"Hotspots detected: {keys.Count}");
        Console.WriteLine($"State entries: {existing.Count} existing, {missingKeys.Count} missing, {next.Count} total");
        Console.WriteLine($"State file: {TryGetRelative(workspace, resolvedStatePath) ?? resolvedStatePath}");

        if (options.Check) {
            if (missingKeys.Count == 0) {
                Console.WriteLine("State is up-to-date.");
                return 0;
            }
            Console.WriteLine("State is missing entries for current hotspots.");
            Console.WriteLine();
            Console.WriteLine("Suggested entries:");
            Console.WriteLine(HotspotStateStore.BuildSuggestedStateSnippet(missingKeys, desiredDefaultStatus));
            return 1;
        }

        if (options.DryRun) {
            if (missingKeys.Count > 0) {
                Console.WriteLine();
                Console.WriteLine("Suggested entries:");
                Console.WriteLine(HotspotStateStore.BuildSuggestedStateSnippet(missingKeys, desiredDefaultStatus));
            }
            return 0;
        }

        HotspotStateStore.Save(resolvedStatePath, next);
        Console.WriteLine("State file updated.");
        return 0;
    }

    private static int SetStatus(string[] args) {
        var options = ParseSetArgs(args, out var error);
        if (options.ShowHelp) {
            PrintSetHelp();
            return 0;
        }
        if (error is not null) {
            Console.WriteLine(error);
            return 1;
        }

        var workspace = AnalyzeRunner.ResolveWorkspace(options.Workspace);
        var configPath = AnalyzeRunner.ResolveConfigPath(options.ConfigPath, workspace);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) {
            Console.WriteLine($"Config not found: {configPath ?? "<null>"}");
            return 1;
        }
        if (!TryLoadAnalysisSettings(configPath, out var settings, out var parseError)) {
            Console.WriteLine(parseError);
            return 1;
        }

        var resolvedStatePath = ResolveStatePath(workspace, options.StatePath ?? settings.Hotspots.StatePath);
        var normalizedStatus = AnalysisHotspots.NormalizeStatus(options.Status);
        if (string.IsNullOrWhiteSpace(normalizedStatus)) {
            Console.WriteLine($"Invalid status '{options.Status}'.");
            return 1;
        }

        var stateFile = HotspotStateStore.TryLoad(resolvedStatePath);
        var list = stateFile.Items.ToList();
        var map = HotspotStateStore.ToMap(list);
        var createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var updated = 0;
        foreach (var rawKey in options.Keys) {
            if (string.IsNullOrWhiteSpace(rawKey)) {
                continue;
            }
            var key = rawKey.Trim();
            if (map.TryGetValue(key, out var existing)) {
                var next = existing with {
                    Status = normalizedStatus,
                    Note = options.Note ?? existing.Note
                };
                // Replace existing entry.
                list.RemoveAll(item => item.Key.Equals(existing.Key, StringComparison.OrdinalIgnoreCase));
                list.Add(next);
            } else {
                list.Add(new HotspotStateEntry(key, normalizedStatus, options.Note, createdAt));
            }
            updated++;
        }

        var final = list
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Status))
            .ToList();

        HotspotStateStore.Save(resolvedStatePath, final);
        Console.WriteLine($"Updated {updated} entr(ies). State file: {TryGetRelative(workspace, resolvedStatePath) ?? resolvedStatePath}");
        return 0;
    }

    private static bool TryLoadAnalysisSettings(string configPath, out AnalysisSettings settings, out string error) {
        settings = new AnalysisSettings();
        error = string.Empty;
        JsonObject? root;
        try {
            root = JsonLite.Parse(File.ReadAllText(configPath))?.AsObject();
        } catch (Exception ex) {
            error = $"Failed to parse config: {ex.Message}";
            return false;
        }
        if (root is null) {
            error = "Config root must be a JSON object.";
            return false;
        }
        var reviewObj = root.GetObject("review") ?? root;
        AnalysisConfigReader.Apply(root, reviewObj, settings);
        return true;
    }

    private static string ResolveStatePath(string workspace, string configured) {
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = ".intelligencex/hotspots.json";
        }
        var trimmed = configured.Trim();
        return Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(workspace, trimmed);
    }

    private static string? TryGetRelative(string workspace, string path) {
        try {
            var fullWorkspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            return Path.GetRelativePath(fullWorkspace, fullPath).Replace('\\', '/');
        } catch {
            return null;
        }
    }

    private static bool IsHelp(string arg) {
        return arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("help", StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private sealed class SyncOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? StatePath { get; set; }
        public string? NewStatus { get; set; }
        public bool PruneFixed { get; set; }
        public bool DryRun { get; set; }
        public bool Check { get; set; }
    }

    private static SyncOptions ParseSyncArgs(string[] args, out string? error) {
        error = null;
        var options = new SyncOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelp(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.ConfigPath = args[++i];
                continue;
            }
            if (arg.Equals("--state", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.StatePath = args[++i];
                continue;
            }
            if (arg.Equals("--new-status", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.NewStatus = args[++i];
                continue;
            }
            if (arg.Equals("--prune-fixed", StringComparison.OrdinalIgnoreCase)) {
                options.PruneFixed = true;
                continue;
            }
            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) {
                options.DryRun = true;
                continue;
            }
            if (arg.Equals("--check", StringComparison.OrdinalIgnoreCase)) {
                options.Check = true;
                continue;
            }
            error = $"Unknown argument: {arg}";
            return options;
        }
        return options;
    }

    private sealed class SetOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? StatePath { get; set; }
        public List<string> Keys { get; } = new();
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private static SetOptions ParseSetArgs(string[] args, out string? error) {
        error = null;
        var options = new SetOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelp(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.ConfigPath = args[++i];
                continue;
            }
            if (arg.Equals("--state", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.StatePath = args[++i];
                continue;
            }
            if (arg.Equals("--key", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Keys.Add(args[++i]);
                continue;
            }
            if (arg.Equals("--keys", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                var raw = args[++i] ?? string.Empty;
                options.Keys.AddRange(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }
            if (arg.Equals("--status", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Status = args[++i];
                continue;
            }
            if (arg.Equals("--note", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                options.Note = args[++i];
                continue;
            }
            error = $"Unknown argument: {arg}";
            return options;
        }
        if (options.Keys.Count == 0) {
            error = "Missing --key/--keys.";
        } else if (string.IsNullOrWhiteSpace(options.Status)) {
            error = "Missing --status.";
        }
        return options;
    }
}
