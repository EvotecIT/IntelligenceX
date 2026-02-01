using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Cli.Usage;

internal static class UsageRunner {
    public static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex usage [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --events              Include credit usage events");
        Console.WriteLine("  --json                Print JSON output");
        Console.WriteLine("  --no-cache            Do not write usage cache");
        Console.WriteLine("  --base-url <url>       Override ChatGPT backend base URL");
        Console.WriteLine("  --auth-path <path>     Override auth store path");
        Console.WriteLine("  --auth-key <base64>    Override auth store encryption key");
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = UsageOptions.Parse(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        try {
            var nativeOptions = new OpenAINativeOptions();
            if (!string.IsNullOrWhiteSpace(options.BaseUrl)) {
                nativeOptions.ChatGptApiBaseUrl = options.BaseUrl!;
            }
            if (!string.IsNullOrWhiteSpace(options.AuthPath) || !string.IsNullOrWhiteSpace(options.AuthKey)) {
                nativeOptions.AuthStore = new FileAuthBundleStore(options.AuthPath, options.AuthKey);
            }

            using var service = new ChatGptUsageService(nativeOptions);
            var report = await service.GetReportAsync(options.IncludeEvents, CancellationToken.None).ConfigureAwait(false);

            if (options.Json) {
                var json = JsonLite.Serialize(JsonValue.From(report.ToJson()));
                Console.WriteLine(json);
            } else {
                PrintSummary(report.Snapshot);
                if (options.IncludeEvents) {
                    PrintEvents(report.Events);
                }
            }
            if (!options.NoCache) {
                TrySaveCache(report.Snapshot);
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintSummary(ChatGptUsageSnapshot snapshot) {
        Console.WriteLine("ChatGPT usage");
        if (!string.IsNullOrWhiteSpace(snapshot.PlanType)) {
            Console.WriteLine($"Plan: {snapshot.PlanType}");
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Email)) {
            Console.WriteLine($"Email: {snapshot.Email}");
        }
        if (!string.IsNullOrWhiteSpace(snapshot.AccountId)) {
            Console.WriteLine($"Account: {snapshot.AccountId}");
        }

        PrintRateLimit("Rate limit", snapshot.RateLimit);
        PrintRateLimit("Code review limit", snapshot.CodeReviewRateLimit);

        if (snapshot.Credits is not null) {
            Console.WriteLine("Credits:");
            Console.WriteLine($"  Has credits: {snapshot.Credits.HasCredits}");
            Console.WriteLine($"  Unlimited: {snapshot.Credits.Unlimited}");
            if (snapshot.Credits.Balance.HasValue) {
                Console.WriteLine($"  Balance: {snapshot.Credits.Balance.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            }
            var localRange = FormatRange(snapshot.Credits.ApproxLocalMessages);
            if (!string.IsNullOrWhiteSpace(localRange)) {
                Console.WriteLine($"  Approx local messages: {localRange}");
            }
            var cloudRange = FormatRange(snapshot.Credits.ApproxCloudMessages);
            if (!string.IsNullOrWhiteSpace(cloudRange)) {
                Console.WriteLine($"  Approx cloud messages: {cloudRange}");
            }
        }
    }

    private static void PrintRateLimit(string label, ChatGptRateLimitStatus? status) {
        if (status is null) {
            return;
        }
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  Allowed: {status.Allowed}");
        Console.WriteLine($"  Limit reached: {status.LimitReached}");
        if (status.PrimaryWindow is not null) {
            Console.WriteLine($"  Primary: {FormatWindow(status.PrimaryWindow)}");
        }
        if (status.SecondaryWindow is not null) {
            Console.WriteLine($"  Secondary: {FormatWindow(status.SecondaryWindow)}");
        }
    }

    private static void PrintEvents(IReadOnlyList<ChatGptCreditUsageEvent> events) {
        if (events.Count == 0) {
            Console.WriteLine("Credit usage events: none");
            return;
        }
        Console.WriteLine("Credit usage events:");
        foreach (var evt in events) {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(evt.Date)) {
                parts.Add(evt.Date);
            }
            if (!string.IsNullOrWhiteSpace(evt.ProductSurface)) {
                parts.Add(evt.ProductSurface);
            }
            if (evt.CreditAmount.HasValue) {
                parts.Add(evt.CreditAmount.Value.ToString("0.####", CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(evt.UsageId)) {
                parts.Add(evt.UsageId);
            }
            Console.WriteLine($"  - {string.Join(" | ", parts)}");
        }
    }

    private static string FormatWindow(ChatGptRateLimitWindow window) {
        var parts = new List<string>();
        if (window.UsedPercent.HasValue) {
            parts.Add($"{window.UsedPercent.Value:0.#}% used");
        }
        if (window.LimitWindowSeconds.HasValue) {
            var span = TimeSpan.FromSeconds(Math.Max(0, window.LimitWindowSeconds.Value));
            parts.Add($"window {FormatDuration(span)}");
        }
        var resetIn = ResolveResetIn(window);
        if (resetIn.HasValue) {
            parts.Add($"resets in {FormatDuration(resetIn.Value)}");
        }
        if (window.ResetAtUnixSeconds.HasValue) {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).ToUniversalTime();
            parts.Add($"reset at {resetAt.ToString("u", CultureInfo.InvariantCulture)}");
        }
        return parts.Count == 0 ? "n/a" : string.Join(", ", parts);
    }

    private static TimeSpan? ResolveResetIn(ChatGptRateLimitWindow window) {
        if (window.ResetAfterSeconds.HasValue) {
            return TimeSpan.FromSeconds(Math.Max(0, window.ResetAfterSeconds.Value));
        }
        if (window.ResetAtUnixSeconds.HasValue) {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).ToUniversalTime();
            var delta = resetAt - DateTimeOffset.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }
        return null;
    }

    private static string FormatDuration(TimeSpan span) {
        if (span.TotalSeconds < 1) {
            return "0s";
        }
        var parts = new List<string>();
        if (span.Days > 0) {
            parts.Add($"{span.Days}d");
        }
        if (span.Hours > 0) {
            parts.Add($"{span.Hours}h");
        }
        if (span.Minutes > 0) {
            parts.Add($"{span.Minutes}m");
        }
        if (parts.Count == 0 && span.Seconds > 0) {
            parts.Add($"{span.Seconds}s");
        }
        return string.Join(" ", parts);
    }

    private static string? FormatRange(int[]? values) {
        if (values is null || values.Length == 0) {
            return null;
        }
        if (values.Length == 2) {
            return $"{values[0]}-{values[1]}";
        }
        return string.Join(", ", values);
    }

    private static void TrySaveCache(ChatGptUsageSnapshot snapshot) {
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch {
            // Cache is best-effort.
        }
    }
}

internal sealed class UsageOptions {
    public bool IncludeEvents { get; set; }
    public bool Json { get; set; }
    public bool ShowHelp { get; set; }
    public bool NoCache { get; set; }
    public string? BaseUrl { get; set; }
    public string? AuthPath { get; set; }
    public string? AuthKey { get; set; }

    public static UsageOptions Parse(string[] args) {
        var options = new UsageOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                return options;
            }
            switch (arg) {
                case "--events":
                    options.IncludeEvents = true;
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--no-cache":
                    options.NoCache = true;
                    break;
                case "--base-url":
                    options.BaseUrl = ReadValue(args, ref i);
                    break;
                case "--auth-path":
                    options.AuthPath = ReadValue(args, ref i);
                    break;
                case "--auth-key":
                    options.AuthKey = ReadValue(args, ref i);
                    break;
            }
        }
        return options;
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        return args[index];
    }
}
