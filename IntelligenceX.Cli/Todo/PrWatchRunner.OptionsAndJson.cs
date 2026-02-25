using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class PrWatchRunner {
    private static async Task<IReadOnlyList<JsonElement>> GhApiListPaginatedAsync(string endpoint) {
        const int pageSize = 100;
        var items = new List<JsonElement>();
        for (var page = 1; page <= 20; page++) {
            var delimiter = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var pageEndpoint = $"{endpoint}{delimiter}per_page={pageSize.ToString(CultureInfo.InvariantCulture)}&page={page.ToString(CultureInfo.InvariantCulture)}";
            var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90), "api", pageEndpoint).ConfigureAwait(false);
            if (code != 0) {
                throw new InvalidOperationException(!string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : $"Failed to query GitHub API endpoint: {endpoint}");
            }

            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException($"Unexpected array payload from GitHub API endpoint: {endpoint}");
            }

            var count = 0;
            foreach (var item in doc.RootElement.EnumerateArray()) {
                items.Add(item.Clone());
                count++;
            }

            if (count < pageSize) {
                break;
            }
        }
        return items;
    }

    private static bool IsReadyToMerge(PrState pr, CheckSummary checks, bool hasActionableReviewItems) {
        if (pr.Closed || pr.Merged) {
            return false;
        }

        if (!checks.AllTerminal || checks.FailedCount > 0 || checks.PendingCount > 0) {
            return false;
        }

        if (hasActionableReviewItems) {
            return false;
        }

        if (!pr.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (MergeConflictOrBlockingStates.Contains(pr.MergeStateStatus)) {
            return false;
        }

        if (MergeBlockingReviewDecisions.Contains(pr.ReviewDecision)) {
            return false;
        }

        return true;
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--pr":
                    if (i + 1 < args.Length) {
                        options.PrSpec = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--poll-seconds":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollSeconds) &&
                        pollSeconds > 0) {
                        options.PollSeconds = Math.Min(MaxPollSeconds, pollSeconds);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--max-flaky-retries":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxFlakyRetries) &&
                        maxFlakyRetries >= 0) {
                        options.MaxFlakyRetries = Math.Min(10, maxFlakyRetries);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--retry-cooldown-minutes":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var retryCooldownMinutes) &&
                        retryCooldownMinutes >= 0) {
                        options.RetryCooldownMinutes = Math.Min(MaxRetryCooldownMinutes, retryCooldownMinutes);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--state-file":
                    if (i + 1 < args.Length) {
                        options.StateFilePath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--watch":
                    options.Watch = true;
                    options.Once = false;
                    break;
                case "--once":
                    options.Once = true;
                    options.Watch = false;
                    break;
                case "--approved-bot":
                    if (i + 1 < args.Length) {
                        var value = (args[++i] ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(value)) {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        } else {
                            options.ApprovedBots.Add(value);
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--apply-retry":
                    options.ApplyRetry = true;
                    break;
                case "--confirm-apply-retry":
                    if (i + 1 < args.Length) {
                        options.ConfirmApplyRetry = args[++i] ?? string.Empty;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--phase":
                    if (i + 1 < args.Length) {
                        var phase = args[++i] ?? string.Empty;
                        if (AllowedPhases.Contains(phase.Trim())) {
                            options.Phase = NormalizePhase(phase);
                        } else {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--source":
                    if (i + 1 < args.Length) {
                        options.Source = NormalizeSource(args[++i]);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--run-link":
                    if (i + 1 < args.Length) {
                        options.RunLink = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--audit-log-path":
                    if (i + 1 < args.Length) {
                        options.AuditLogPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ParseFailed = true;
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/', StringComparison.Ordinal)) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (string.IsNullOrWhiteSpace(options.PrSpec)) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (options.Watch && options.Once) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (options.ApplyRetry && options.Watch) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        options.Phase = NormalizePhase(options.Phase);
        options.Source = NormalizeSource(options.Source);
        if (string.IsNullOrWhiteSpace(options.AuditLogPath)) {
            options.AuditLogPath = DefaultAuditLogPath;
        }

        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo pr-watch [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to watch (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --pr <auto|number|url>      Target PR (default: auto from current branch)");
        Console.WriteLine("  --once                      Capture one snapshot and exit (default)");
        Console.WriteLine("  --watch                     Emit continuous snapshots until terminal state");
        Console.WriteLine("  --poll-seconds <n>          Base poll interval in watch mode (default: 60)");
        Console.WriteLine("  --max-flaky-retries <n>     Retry budget for recommendation classification (default: 3)");
        Console.WriteLine("  --retry-cooldown-minutes <n> Suppress repeated retry recommendations during cooldown (default: 15)");
        Console.WriteLine("  --state-file <path>         Optional watcher state file path");
        Console.WriteLine("  --approved-bot <login>      Additional approved bot login (repeatable)");
        Console.WriteLine("  --apply-retry               Execute retry_failed_checks action if eligible (once mode only)");
        Console.WriteLine("  --confirm-apply-retry <v>   Required safety confirmation token (`RETRY_CHECKS`)");
        Console.WriteLine("  --phase <observe|assist|repair> Audit phase marker (default: observe)");
        Console.WriteLine("  --source <value>            Audit source marker (default: manual_cli)");
        Console.WriteLine("  --run-link <url>            Optional workflow/job URL embedded in audit records");
        Console.WriteLine("  --audit-log-path <path>     Audit JSONL output (default: artifacts/pr-watch/ix-pr-watch-audit.jsonl)");
    }

    private static void PrintJson(object value) {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        Console.WriteLine(json);
    }

    private static string ReadString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return string.Empty;
        }
        return node.ValueKind == JsonValueKind.String
            ? (node.GetString() ?? string.Empty)
            : string.Empty;
    }

    private static string ReadLongAsString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return string.Empty;
        }
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var value)) {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        if (node.ValueKind == JsonValueKind.String) {
            return node.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ReadNestedString(JsonElement element, string propertyName, string nestedPropertyName) {
        if (!element.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }

        return ReadString(node, nestedPropertyName);
    }

    private static int ReadInt(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return 0;
        }
        return node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)) {
            return value;
        }
        return null;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }
        return node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }
}
