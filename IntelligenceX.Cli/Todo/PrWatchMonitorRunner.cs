using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class PrWatchMonitorRunner {
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string PrSpec { get; set; } = string.Empty;
        public int MaxPrs { get; set; } = 100;
        public int MaxFlakyRetries { get; set; } = 3;
        public bool IncludeDrafts { get; set; }
        public string Source { get; set; } = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "manual_cli";
        public bool SourceExplicitlySet { get; set; }
        public string? RunLink { get; set; } = ResolveRunLink();
        public string SnapshotDir { get; set; } = Path.Combine("artifacts", "pr-watch", "snapshots");
        public string RollupPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-rollup.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-summary.md");
        public HashSet<string> ApprovedBots { get; } = new(StringComparer.OrdinalIgnoreCase) {
            "intelligencex-review",
            "intelligencex-review[bot]",
            "chatgpt-codex-connector[bot]"
        };
    }

    public static async Task<int> RunAsync(string[] args) {
        try {
            var options = ParseOptions(args);
            if (options is null) {
                PrintHelp();
                return 0;
            }

            Directory.CreateDirectory(options.SnapshotDir);
            foreach (var file in Directory.EnumerateFiles(options.SnapshotDir)) {
                File.Delete(file);
            }

            var targets = await ResolveTargetsAsync(options).ConfigureAwait(false);
            var rows = new List<JsonObject>();
            foreach (var target in targets) {
                var snapshot = await PrWatchConsolidationRunner.RunPrWatchSnapshotAsync(
                    repo: options.Repo,
                    prSpec: target,
                    maxFlakyRetries: options.MaxFlakyRetries,
                    phase: "observe",
                    source: options.Source,
                    runLink: options.RunLink,
                    approvedBots: options.ApprovedBots).ConfigureAwait(false);

                var pr = snapshot["pr"] as JsonObject ?? new JsonObject();
                var checks = snapshot["checks"] as JsonObject ?? new JsonObject();
                var actions = (snapshot["actions"] as JsonArray ?? new JsonArray())
                    .OfType<JsonObject>()
                    .Select(static action => ReadString(action, "name"))
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();

                var number = ReadInt(pr, "number");
                WriteJson(Path.Combine(options.SnapshotDir, $"pr-{number.ToString(CultureInfo.InvariantCulture)}.json"), snapshot);
                rows.Add(new JsonObject {
                    ["number"] = number,
                    ["url"] = ReadString(pr, "url"),
                    ["headSha"] = ReadString(pr, "headSha"),
                    ["stopReason"] = ReadString(snapshot, "stopReason"),
                    ["actions"] = new JsonArray(actions.Select(static action => (JsonNode)action).ToArray()),
                    ["checks"] = new JsonObject {
                        ["passedCount"] = ReadInt(checks, "passedCount"),
                        ["failedCount"] = ReadInt(checks, "failedCount"),
                        ["pendingCount"] = ReadInt(checks, "pendingCount")
                    }
                });
            }

            var rollup = BuildRollup(options, rows);
            WriteJson(options.RollupPath, rollup);
            WriteText(options.SummaryPath, BuildSummary(options, rollup));
            AppendStepSummary(options.SummaryPath);
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Options? ParseOptions(string[] args) {
        if (args.Length > 0 && IsHelp(args[0])) {
            return null;
        }
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--repo": options.Repo = Next(args, ref i, arg); break;
                case "--pr": options.PrSpec = Next(args, ref i, arg); break;
                case "--max-prs": options.MaxPrs = ParseInt(Next(args, ref i, arg), options.MaxPrs, arg, 1, 2000); break;
                case "--max-flaky-retries": options.MaxFlakyRetries = ParseInt(Next(args, ref i, arg), options.MaxFlakyRetries, arg, 1, 50); break;
                case "--include-drafts": options.IncludeDrafts = ParseBool(Next(args, ref i, arg), options.IncludeDrafts, arg); break;
                case "--approved-bot": options.ApprovedBots.Add(Next(args, ref i, arg)); break;
                case "--approved-bots": AddCsv(options.ApprovedBots, Next(args, ref i, arg)); break;
                case "--source":
                    options.Source = Next(args, ref i, arg);
                    options.SourceExplicitlySet = true;
                    break;
                case "--run-link": options.RunLink = Next(args, ref i, arg); break;
                case "--snapshot-dir": options.SnapshotDir = Next(args, ref i, arg); break;
                case "--rollup-path": options.RollupPath = Next(args, ref i, arg); break;
                case "--summary-path": options.SummaryPath = Next(args, ref i, arg); break;
                default: throw new InvalidOperationException($"Unknown option: {arg}");
            }
        }
        ApplyGitHubEventDefaults(options);
        return options;
    }

    private static void ApplyGitHubEventDefaults(Options options) {
        var eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME");
        var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        var payload = LoadGitHubEventPayload(eventPath);

        options.PrSpec = ResolvePrSpecWithEventDefaults(options.PrSpec, payload);
        options.Source = ResolveSourceWithEventDefaults(options.Source, options.SourceExplicitlySet, payload, eventName);
    }

    internal static string ComposeSourceTag(string source, string action) {
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "manual_cli" : source.Trim();
        var normalizedAction = string.IsNullOrWhiteSpace(action) ? string.Empty : action.Trim();
        return string.IsNullOrWhiteSpace(normalizedAction) ? normalizedSource : $"{normalizedSource}:{normalizedAction}";
    }

    internal static string ResolveSourceWithEventDefaults(string source, bool sourceExplicitlySet, JsonObject? payload, string? eventName) {
        if (sourceExplicitlySet) {
            return source;
        }

        var action = ResolveEventActionFromGitHubEventPayload(payload);
        var fallbackSource = ResolveDefaultSourceBase(source, eventName);
        return ComposeSourceTag(fallbackSource, action);
    }

    internal static string ResolveDefaultSourceBase(string source, string? eventName) {
        if (!string.IsNullOrWhiteSpace(source)) {
            return source.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eventName)) {
            return eventName.Trim();
        }

        return "manual_cli";
    }

    internal static string ResolvePrSpecWithEventDefaults(string prSpec, JsonObject? payload) {
        if (!string.IsNullOrWhiteSpace(prSpec)) {
            return prSpec;
        }

        return ResolvePrSpecFromGitHubEventPayload(payload);
    }

    internal static string ResolveEventActionFromGitHubEventPayload(JsonObject? payload) {
        if (payload is null) {
            return string.Empty;
        }

        return ReadString(payload, "action");
    }

    internal static string ResolvePrSpecFromGitHubEventPayload(JsonObject? payload) {
        if (payload is null || !payload.TryGetPropertyValue("pull_request", out var prNode) || prNode is not JsonObject pr) {
            return string.Empty;
        }

        var number = ReadInt(pr, "number");
        return number > 0 ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static JsonObject? LoadGitHubEventPayload(string? eventPath) {
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath)) {
            return null;
        }

        try {
            return JsonNode.Parse(File.ReadAllText(eventPath)) as JsonObject;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse GITHUB_EVENT_PATH payload '{eventPath}': {ex.Message}");
            return null;
        }
    }

    private static async Task<List<string>> ResolveTargetsAsync(Options options) {
        if (!string.IsNullOrWhiteSpace(options.PrSpec)) {
            return new List<string> { options.PrSpec };
        }
        var (exitCode, stdOut, stdErr) = await GhCli.RunAsync(
            "pr", "list",
            "--repo", options.Repo,
            "--state", "open",
            "--limit", options.MaxPrs.ToString(CultureInfo.InvariantCulture),
            "--json", "number,isDraft").ConfigureAwait(false);
        if (exitCode != 0) {
            throw new InvalidOperationException($"gh pr list failed: {stdErr.Trim()}");
        }
        var list = new List<string>();
        var root = JsonNode.Parse(stdOut) as JsonArray ?? new JsonArray();
        foreach (var node in root.OfType<JsonObject>()) {
            if (!options.IncludeDrafts && ReadBool(node, "isDraft")) {
                continue;
            }
            var number = ReadInt(node, "number");
            if (number > 0) {
                list.Add(number.ToString(CultureInfo.InvariantCulture));
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static JsonObject BuildRollup(Options options, IReadOnlyList<JsonObject> rows) {
        var stopReasons = rows
            .Select(static row => ReadString(row, "stopReason"))
            .Select(static reason => string.IsNullOrWhiteSpace(reason) ? "none" : reason)
            .GroupBy(static reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (JsonNode)new JsonObject { ["reason"] = group.Key, ["count"] = group.Count() }).ToArray();
        var actionCounts = rows
            .SelectMany(static row => (row["actions"] as JsonArray ?? new JsonArray()).Select(static action => action?.ToString() ?? string.Empty))
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .GroupBy(static action => action, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (JsonNode)new JsonObject { ["action"] = group.Key, ["count"] = group.Count() }).ToArray();

        return new JsonObject {
            ["schema"] = "intelligencex.pr-watch.rollup.v1",
            ["repo"] = options.Repo,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["totalSnapshots"] = rows.Count,
            ["stopReasons"] = new JsonArray(stopReasons),
            ["actionCounts"] = new JsonArray(actionCounts),
            ["prs"] = new JsonArray(rows.OrderBy(static row => ReadInt(row, "number")).Select(static row => (JsonNode)row).ToArray())
        };
    }

    private static string BuildSummary(Options options, JsonObject rollup) {
        var builder = new StringBuilder();
        builder.AppendLine("# IX PR Babysit Monitor (Observe)");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        if (!string.IsNullOrWhiteSpace(options.RunLink)) {
            builder.AppendLine($"- Workflow run: {options.RunLink}");
        }
        builder.AppendLine();
        builder.AppendLine("## Snapshot counts");
        builder.AppendLine($"- PRs scanned: {ReadInt(rollup, "totalSnapshots")}");
        return builder.ToString();
    }

    private static string? ResolveRunLink() {
        var server = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        return string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(runId) ? null : $"{server.TrimEnd('/')}/{repo}/actions/runs/{runId}";
    }

    private static string Next(string[] args, ref int index, string optionName) { if (index + 1 >= args.Length) throw new InvalidOperationException($"{optionName} requires a value."); return args[++index]; }
    private static int ParseInt(string raw, int current, string optionName, int min, int max) { if (string.IsNullOrWhiteSpace(raw)) return current; if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < min || v > max) throw new InvalidOperationException($"{optionName} must be between {min} and {max}."); return v; }
    private static bool ParseBool(string raw, bool current, string optionName) { if (string.IsNullOrWhiteSpace(raw)) return current; if (bool.TryParse(raw, out var v)) return v; throw new InvalidOperationException($"{optionName} must be true/false."); }
    private static void AddCsv(HashSet<string> values, string csv) { foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) values.Add(part); }
    private static bool IsHelp(string value) => value.Equals("-h", StringComparison.OrdinalIgnoreCase) || value.Equals("--help", StringComparison.OrdinalIgnoreCase) || value.Equals("help", StringComparison.OrdinalIgnoreCase);
    private static int ReadInt(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<int>(out var v) ? v : 0;
    private static bool ReadBool(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<bool>(out var v) && v;
    private static string ReadString(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;
    private static void WriteJson(string path, JsonNode node) { EnsureDirectory(path); File.WriteAllText(path, node.ToJsonString(CompactJson)); }
    private static void WriteText(string path, string text) { EnsureDirectory(path); File.WriteAllText(path, text, Encoding.UTF8); }
    private static void EnsureDirectory(string path) { var dir = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); }
    private static void AppendStepSummary(string path) { var step = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY"); if (!string.IsNullOrWhiteSpace(step) && File.Exists(path)) File.AppendAllText(step, File.ReadAllText(path) + Environment.NewLine, Encoding.UTF8); }

    private static void PrintHelp() {
        Console.WriteLine("Usage: intelligencex todo pr-watch-monitor [options]");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --pr <number|url>");
        Console.WriteLine("  --max-prs <n>");
        Console.WriteLine("  --max-flaky-retries <n>");
        Console.WriteLine("  --include-drafts <bool>");
        Console.WriteLine("  --approved-bot <login> (repeatable)");
        Console.WriteLine("  --approved-bots <csv>");
        Console.WriteLine("  --source <value>");
        Console.WriteLine("  --run-link <url>");
        Console.WriteLine("  --snapshot-dir <path>");
        Console.WriteLine("  --rollup-path <path>");
        Console.WriteLine("  --summary-path <path>");
    }
}
