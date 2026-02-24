using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Todo;

internal static class PrWatchAssistRetryRunner {
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string PrSpec { get; set; } = string.Empty;
        public int MaxFlakyRetries { get; set; } = 3;
        public int RetryCooldownMinutes { get; set; } = 15;
        public string ConfirmApplyRetries { get; set; } = string.Empty;
        public string Source { get; set; } = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "manual_cli";
        public string? RunLink { get; set; } = ResolveRunLink();
        public string OutputPath { get; set; } = Path.Combine("artifacts", "pr-watch", "assist", "ix-pr-watch-assist-pr.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-assist-summary.md");
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

            if (string.IsNullOrWhiteSpace(options.PrSpec)) {
                throw new InvalidOperationException("--pr is required.");
            }
            if (!string.Equals(options.ConfirmApplyRetries, "RETRY_CHECKS", StringComparison.Ordinal)) {
                throw new InvalidOperationException("--confirm-apply-retries must equal RETRY_CHECKS.");
            }

            var snapshot = await PrWatchConsolidationRunner.RunPrWatchSnapshotAsync(
                repo: options.Repo,
                prSpec: options.PrSpec,
                maxFlakyRetries: options.MaxFlakyRetries,
                phase: "assist",
                source: options.Source,
                runLink: options.RunLink,
                approvedBots: options.ApprovedBots,
                applyRetry: true,
                retryCooldownMinutes: options.RetryCooldownMinutes,
                confirmApplyRetry: options.ConfirmApplyRetries).ConfigureAwait(false);

            WriteJson(options.OutputPath, snapshot);
            WriteText(options.SummaryPath, BuildSummary(options, snapshot));
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
                case "--max-flaky-retries": options.MaxFlakyRetries = ParseInt(Next(args, ref i, arg), options.MaxFlakyRetries, arg, 1, 50); break;
                case "--retry-cooldown-minutes": options.RetryCooldownMinutes = ParseInt(Next(args, ref i, arg), options.RetryCooldownMinutes, arg, 1, 1440); break;
                case "--confirm-apply-retries": options.ConfirmApplyRetries = Next(args, ref i, arg); break;
                case "--approved-bot": options.ApprovedBots.Add(Next(args, ref i, arg)); break;
                case "--approved-bots": AddCsv(options.ApprovedBots, Next(args, ref i, arg)); break;
                case "--source": options.Source = Next(args, ref i, arg); break;
                case "--run-link": options.RunLink = Next(args, ref i, arg); break;
                case "--output-path": options.OutputPath = Next(args, ref i, arg); break;
                case "--summary-path": options.SummaryPath = Next(args, ref i, arg); break;
                default: throw new InvalidOperationException($"Unknown option: {arg}");
            }
        }
        return options;
    }

    private static string BuildSummary(Options options, JsonObject snapshot) {
        var pr = snapshot["pr"] as JsonObject ?? new JsonObject();
        var retryState = snapshot["retryState"] as JsonObject ?? new JsonObject();
        var actions = (snapshot["actions"] as JsonArray ?? new JsonArray())
            .OfType<JsonObject>()
            .Select(static action => ReadString(action, "name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var builder = new StringBuilder();
        builder.AppendLine("# IX PR Babysit Assist Retry");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        if (!string.IsNullOrWhiteSpace(options.RunLink)) {
            builder.AppendLine($"- Workflow run: {options.RunLink}");
        }
        builder.AppendLine();
        builder.AppendLine("## Snapshot");
        builder.AppendLine($"- PR: #{ReadInt(pr, "number")} ({ReadString(pr, "url")})");
        builder.AppendLine($"- Head SHA: `{ReadString(pr, "headSha")}`");
        builder.AppendLine($"- Stop reason: `{ReadString(snapshot, "stopReason")}`");
        builder.AppendLine($"- Retry usage: {ReadInt(retryState, "currentShaRetriesUsed")}/{ReadInt(retryState, "maxFlakyRetries")}");
        builder.AppendLine();
        builder.AppendLine("## Planned actions");
        if (actions.Count == 0) {
            builder.AppendLine("- none");
        } else {
            foreach (var action in actions) {
                builder.AppendLine($"- `{action}`");
            }
        }
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
    private static void AddCsv(HashSet<string> values, string csv) { foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) values.Add(part); }
    private static bool IsHelp(string value) => value.Equals("-h", StringComparison.OrdinalIgnoreCase) || value.Equals("--help", StringComparison.OrdinalIgnoreCase) || value.Equals("help", StringComparison.OrdinalIgnoreCase);
    private static int ReadInt(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<int>(out var v) ? v : 0;
    private static string ReadString(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;
    private static void WriteJson(string path, JsonNode node) { EnsureDirectory(path); File.WriteAllText(path, node.ToJsonString(CompactJson)); }
    private static void WriteText(string path, string text) { EnsureDirectory(path); File.WriteAllText(path, text, Encoding.UTF8); }
    private static void EnsureDirectory(string path) { var dir = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); }
    private static void AppendStepSummary(string path) { var step = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY"); if (!string.IsNullOrWhiteSpace(step) && File.Exists(path)) File.AppendAllText(step, File.ReadAllText(path) + Environment.NewLine, Encoding.UTF8); }

    private static void PrintHelp() {
        Console.WriteLine("Usage: intelligencex todo pr-watch-assist-retry [options]");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --pr <number|url>");
        Console.WriteLine("  --max-flaky-retries <n>");
        Console.WriteLine("  --retry-cooldown-minutes <n>");
        Console.WriteLine("  --confirm-apply-retries RETRY_CHECKS");
        Console.WriteLine("  --approved-bot <login> (repeatable)");
        Console.WriteLine("  --approved-bots <csv>");
        Console.WriteLine("  --source <value>");
        Console.WriteLine("  --run-link <url>");
        Console.WriteLine("  --output-path <path>");
        Console.WriteLine("  --summary-path <path>");
    }
}
