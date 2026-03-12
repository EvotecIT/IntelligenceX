using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Cli.Heatmap;

internal static class HeatmapRunner {
    public static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex heatmap chatgpt [options]");
        Console.WriteLine("  intelligencex heatmap github --user <login> [options]");
        Console.WriteLine("  intelligencex heatmap usage --db <path> [options]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --out <path>          Write SVG or JSON output to a file");
        Console.WriteLine("  --json                Print normalized heatmap JSON instead of SVG");
        Console.WriteLine();
        Console.WriteLine("ChatGPT options:");
        Console.WriteLine("  --account-id <id>     Select a specific ChatGPT account id");
        Console.WriteLine("  --base-url <url>      Override ChatGPT backend base URL");
        Console.WriteLine("  --auth-path <path>    Override auth store path");
        Console.WriteLine("  --auth-key <base64>   Override auth store encryption key");
        Console.WriteLine();
        Console.WriteLine("GitHub options:");
        Console.WriteLine("  --user <login>        GitHub user login to render");
        Console.WriteLine("  --years <n>           Number of trailing years to include (default: 5)");
        Console.WriteLine("  --from <yyyy-MM-dd>   Explicit start date");
        Console.WriteLine("  --to <yyyy-MM-dd>     Explicit end date (default: today)");
        Console.WriteLine();
        Console.WriteLine("Telemetry usage options:");
        Console.WriteLine("  --db <path>           SQLite telemetry database path");
        Console.WriteLine("  --provider <id>       Filter events to a single provider (ix, codex, claude, ...)");
        Console.WriteLine("  --account <value>     Filter by provider account id or account label");
        Console.WriteLine("  --person <value>      Filter by bound person label");
        Console.WriteLine("  --metric <name>       tokens|cost|duration|events (default: tokens)");
        Console.WriteLine("  --breakdown <name>    surface|provider|account|person|model (default: surface)");
        Console.WriteLine("  --title <text>        Override the rendered title");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  GitHub rendering uses `gh api graphql`, so `gh auth login` should already be configured.");
    }

    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try {
            return command switch {
                "chatgpt" => await RunChatGptAsync(rest).ConfigureAwait(false),
                "github" => await RunGitHubAsync(rest).ConfigureAwait(false),
                "usage" => await RunUsageAsync(rest).ConfigureAwait(false),
                "help" or "-h" or "--help" => PrintHelpReturn(),
                _ => PrintHelpReturn()
            };
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunChatGptAsync(string[] args) {
        var options = ChatGptHeatmapOptions.Parse(args);

        var nativeOptions = new OpenAINativeOptions();
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)) {
            nativeOptions.ChatGptApiBaseUrl = options.BaseUrl!;
        }
        if (!string.IsNullOrWhiteSpace(options.AccountId)) {
            nativeOptions.AuthAccountId = options.AccountId!;
        }
        if (!string.IsNullOrWhiteSpace(options.AuthPath) || !string.IsNullOrWhiteSpace(options.AuthKey)) {
            nativeOptions.AuthStore = new FileAuthBundleStore(options.AuthPath, options.AuthKey);
        }

        using var service = new ChatGptUsageService(nativeOptions);
        var report = await service.GetReportAsync(includeEvents: false, includeDailyBreakdown: true, CancellationToken.None)
            .ConfigureAwait(false);
        if (report.DailyBreakdown is null || report.DailyBreakdown.Data.Count == 0) {
            throw new InvalidOperationException("ChatGPT daily usage breakdown is empty.");
        }

        var document = BuildChatGptDocument(report);
        return WriteOutput(document, options.Json, options.OutputPath);
    }

    private static async Task<int> RunGitHubAsync(string[] args) {
        var options = GitHubHeatmapOptions.Parse(args);
        if (string.IsNullOrWhiteSpace(options.User)) {
            throw new InvalidOperationException("Missing GitHub user. Use --user <login>.");
        }

        var to = options.To ?? DateTimeOffset.UtcNow;
        var years = Math.Max(1, options.Years);
        var from = options.From ?? new DateTimeOffset(new DateTime(to.Year - years + 1, 1, 1), TimeSpan.Zero);
        if (to < from) {
            throw new InvalidOperationException("GitHub heatmap end date must be on or after the start date.");
        }

        var client = new GitHubContributionCalendarClient();
        var calendar = await client.GetUserContributionCalendarAsync(options.User!, from, to).ConfigureAwait(false);
        var document = BuildGitHubDocument(calendar, from, to);
        return WriteOutput(document, options.Json, options.OutputPath);
    }

    private static Task<int> RunUsageAsync(string[] args) {
        var options = UsageHeatmapCliOptions.Parse(args);
        using var eventStore = new SqliteUsageEventStore(options.DatabasePath!);
        var events = eventStore.GetAll()
            .Where(record => MatchesProvider(record, options.ProviderId))
            .Where(record => MatchesAccount(record, options.AccountFilter))
            .Where(record => MatchesPerson(record, options.PersonFilter))
            .ToArray();

        if (events.Length == 0) {
            throw new InvalidOperationException("No telemetry usage events matched the requested filters.");
        }

        var documentBuilder = new UsageTelemetryHeatmapDocumentBuilder();
        var document = documentBuilder.Build(
            events,
            new UsageTelemetryHeatmapOptions {
                Metric = options.Metric,
                Breakdown = options.Breakdown,
                Title = options.Title ?? BuildUsageTitle(options),
                Subtitle = BuildUsageSubtitle(options)
            });
        return Task.FromResult(WriteOutput(document, options.Json, options.OutputPath));
    }

    private static int WriteOutput(HeatmapDocument document, bool asJson, string? outputPath) {
        var content = asJson
            ? JsonLite.Serialize(JsonValue.From(document.ToJson()))
            : HeatmapSvgRenderer.Render(document);

        if (string.IsNullOrWhiteSpace(outputPath)) {
            Console.WriteLine(content);
            return 0;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {fullPath}");
        return 0;
    }

    private static HeatmapDocument BuildChatGptDocument(ChatGptUsageReport report) {
        var breakdown = report.DailyBreakdown!;
        var parsedDays = breakdown.Data
            .Select(day => BuildChatGptHeatmapDay(day, breakdown.Units))
            .Where(static day => day is not null)
            .Select(static day => day!)
            .OrderBy(static day => day.Date)
            .ToArray();

        if (parsedDays.Length == 0) {
            throw new InvalidOperationException("ChatGPT daily usage breakdown did not contain any parseable dates.");
        }

        var globalMax = parsedDays.Max(static day => day.Total);
        var heatmapDays = parsedDays
            .Select(day => day.ToHeatmapDay(globalMax))
            .ToArray();

        var sections = heatmapDays
            .GroupBy(static day => day.Date.Year)
            .OrderByDescending(static group => group.Key)
            .Select(group => {
                var values = group.OrderBy(static day => day.Date).ToArray();
                var activeDays = values.Count(static day => day.Value > 0);
                var peak = values.Max(static day => day.Value);
                var subtitle = $"{activeDays} active day(s), peak {FormatNumber(peak)} {ResolveUnitsLabel(breakdown.Units)}";
                return new HeatmapSection(group.Key.ToString(CultureInfo.InvariantCulture), subtitle, values);
            })
            .Cast<HeatmapSection>()
            .ToArray();

        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.Snapshot.PlanType)) {
            subtitleParts.Add(report.Snapshot.PlanType!);
        }
        if (!string.IsNullOrWhiteSpace(report.Snapshot.Email)) {
            subtitleParts.Add(report.Snapshot.Email!);
        }

        var legendItems = ChatGptSurfaceSwatches
            .Where(swatch => parsedDays.Any(day => day.SurfaceKey.Equals(swatch.Key, StringComparison.OrdinalIgnoreCase)))
            .Select(static swatch => new HeatmapLegendItem(swatch.Label, swatch.Color))
            .ToArray();

        return new HeatmapDocument(
            title: "ChatGPT usage",
            subtitle: subtitleParts.Count == 0 ? "Daily token usage by dominant surface" : string.Join(" | ", subtitleParts),
            palette: HeatmapPalette.ChatGptDark(),
            sections: sections,
            units: breakdown.Units,
            weekStart: DayOfWeek.Sunday,
            showIntensityLegend: true,
            legendLowLabel: "Lower load",
            legendHighLabel: "Higher load",
            legendItems: legendItems);
    }

    private static HeatmapDocument BuildGitHubDocument(
        GitHubContributionCalendar calendar,
        DateTimeOffset from,
        DateTimeOffset to) {
        var heatmapDays = calendar.Days
            .Where(day => day.Date >= from.Date && day.Date <= to.Date)
            .OrderBy(static day => day.Date)
            .Select(static day => new HeatmapDay(
                date: day.Date,
                value: day.ContributionCount,
                level: MapContributionLevel(day.ContributionLevel),
                fillColor: day.Color,
                tooltip: $"{day.Date:yyyy-MM-dd}\n{day.ContributionCount} contribution(s)",
                breakdown: new Dictionary<string, double> { ["contributions"] = day.ContributionCount }))
            .ToArray();

        var sections = heatmapDays
            .GroupBy(static day => day.Date.Year)
            .OrderByDescending(static group => group.Key)
            .Select(group => {
                var ordered = group.OrderBy(static day => day.Date).ToArray();
                var yearTotal = ordered.Sum(static day => day.Value);
                var activeDays = ordered.Count(static day => day.Value > 0);
                var subtitle = $"{FormatInteger(yearTotal)} contribution(s), {activeDays} active day(s)";
                return new HeatmapSection(group.Key.ToString(CultureInfo.InvariantCulture), subtitle, ordered);
            })
            .Cast<HeatmapSection>()
            .ToArray();

        var subtitle = string.IsNullOrWhiteSpace(calendar.Name) || string.Equals(calendar.Name, calendar.Login, StringComparison.OrdinalIgnoreCase)
            ? $"github.com/{calendar.Login}"
            : $"{calendar.Name} | github.com/{calendar.Login}";

        return new HeatmapDocument(
            title: $"@{calendar.Login} on GitHub",
            subtitle: subtitle,
            palette: HeatmapPalette.GitHubLight(),
            sections: sections,
            units: "contributions",
            weekStart: DayOfWeek.Sunday,
            showIntensityLegend: true,
            legendLowLabel: "Less",
            legendHighLabel: "More");
    }

    private static ChatGptHeatmapSourceDay? BuildChatGptHeatmapDay(ChatGptDailyTokenUsageDay day, string? units) {
        if (string.IsNullOrWhiteSpace(day.Date) ||
            !DateTime.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)) {
            return null;
        }

        var nonZero = day.ProductSurfaceUsageValues
            .Where(static pair => Math.Abs(pair.Value) > 0.00001d)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dominantSurface = nonZero.Length == 0 ? "other" : ClassifyChatGptSurface(nonZero[0].Key);
        var tooltip = new StringBuilder();
        tooltip.Append(parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        tooltip.Append('\n');
        tooltip.Append("Total: ");
        tooltip.Append(FormatNumber(day.Total));
        if (!string.IsNullOrWhiteSpace(units)) {
            tooltip.Append(' ');
            tooltip.Append(units!.Trim());
        }
        foreach (var pair in nonZero.Take(4)) {
            tooltip.Append('\n');
            tooltip.Append(DisplayChatGptSurface(pair.Key));
            tooltip.Append(": ");
            tooltip.Append(FormatNumber(pair.Value));
        }

        return new ChatGptHeatmapSourceDay(parsedDate.Date, day.Total, dominantSurface, day.ProductSurfaceUsageValues, tooltip.ToString());
    }

    private static int MapContributionLevel(string? level) {
        if (string.IsNullOrWhiteSpace(level)) {
            return 0;
        }
        return level.Trim() switch {
            "FIRST_QUARTILE" => 1,
            "SECOND_QUARTILE" => 2,
            "THIRD_QUARTILE" => 3,
            "FOURTH_QUARTILE" => 4,
            _ => 0
        };
    }

    private static int QuantizeLevel(double value, double maxValue) {
        if (value <= 0 || maxValue <= 0) {
            return 0;
        }
        var normalized = Math.Sqrt(Math.Min(1d, value / maxValue));
        return 1 + (int)Math.Floor(normalized * 3.999d);
    }

    private static string ResolveUnitsLabel(string? units) {
        return string.IsNullOrWhiteSpace(units) ? "units" : units.Trim();
    }

    private static string FormatNumber(double value) {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatInteger(double value) {
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string ClassifyChatGptSurface(string surface) {
        var value = surface.Trim().Replace('-', '_');
        if (value.Contains("github_code_review", StringComparison.OrdinalIgnoreCase)) {
            return "github_code_review";
        }
        if (value.Contains("desktop", StringComparison.OrdinalIgnoreCase)) {
            return "desktop_app";
        }
        if (value.Contains("cli", StringComparison.OrdinalIgnoreCase)) {
            return "cli";
        }
        if (value.Contains("web", StringComparison.OrdinalIgnoreCase)) {
            return "web";
        }
        return "other";
    }

    private static string DisplayChatGptSurface(string surface) {
        return ClassifyChatGptSurface(surface) switch {
            "github_code_review" => "GitHub Code Review",
            "desktop_app" => "Desktop App",
            "cli" => "CLI",
            "web" => "Web",
            _ => "Other"
        };
    }

    private static string ResolveChatGptSurfaceColor(string surfaceKey, double intensity) {
        var emptyColor = HeatmapPalette.ChatGptDark().EmptyColor;
        var baseColor = ChatGptSurfaceSwatches.FirstOrDefault(swatch => swatch.Key.Equals(surfaceKey, StringComparison.OrdinalIgnoreCase)).Color
            ?? "#9aa4b2";
        var factor = 0.22d + (0.78d * intensity);
        return BlendColor(emptyColor, baseColor, factor);
    }

    private static string BlendColor(string from, string to, double factor) {
        var start = ParseColor(from);
        var end = ParseColor(to);
        var clamped = Math.Max(0d, Math.Min(1d, factor));
        var r = (int)Math.Round(start.R + ((end.R - start.R) * clamped));
        var g = (int)Math.Round(start.G + ((end.G - start.G) * clamped));
        var b = (int)Math.Round(start.B + ((end.B - start.B) * clamped));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (int R, int G, int B) ParseColor(string value) {
        var normalized = value?.Trim() ?? string.Empty;
        if (!normalized.StartsWith("#", StringComparison.Ordinal) || normalized.Length != 7) {
            return (153, 153, 153);
        }

        return (
            ParseHexComponent(normalized, 1),
            ParseHexComponent(normalized, 3),
            ParseHexComponent(normalized, 5));
    }

    private static int ParseHexComponent(string value, int index) {
        return int.TryParse(value.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 153;
    }

    private static readonly (string Key, string Label, string Color)[] ChatGptSurfaceSwatches = {
        ("cli", "CLI", "#f25ca7"),
        ("github_code_review", "GitHub Code Review", "#8ccf1f"),
        ("desktop_app", "Desktop App", "#ef4444"),
        ("web", "Web", "#38bdf8"),
        ("other", "Other", "#94a3b8")
    };

    private static int PrintHelpReturn() {
        PrintHelp();
        return 0;
    }

    private sealed class ChatGptHeatmapSourceDay {
        public ChatGptHeatmapSourceDay(
            DateTime date,
            double total,
            string surfaceKey,
            IReadOnlyDictionary<string, double> breakdown,
            string tooltip) {
            Date = date;
            Total = total;
            SurfaceKey = surfaceKey;
            Breakdown = breakdown;
            Tooltip = tooltip;
        }

        public DateTime Date { get; }
        public double Total { get; }
        public string SurfaceKey { get; }
        public IReadOnlyDictionary<string, double> Breakdown { get; }
        public string Tooltip { get; }

        public HeatmapDay ToHeatmapDay(double globalMax) {
            var normalized = globalMax <= 0 ? 0d : Math.Sqrt(Math.Min(1d, Total / globalMax));
            return new HeatmapDay(
                date: Date,
                value: Total,
                level: QuantizeLevel(Total, globalMax),
                fillColor: Total <= 0 ? HeatmapPalette.ChatGptDark().EmptyColor : ResolveChatGptSurfaceColor(SurfaceKey, normalized),
                tooltip: Tooltip,
                breakdown: Breakdown);
        }
    }

    private sealed class ChatGptHeatmapOptions {
        public bool Json { get; set; }
        public string? OutputPath { get; set; }
        public string? AccountId { get; set; }
        public string? BaseUrl { get; set; }
        public string? AuthPath { get; set; }
        public string? AuthKey { get; set; }

        public static ChatGptHeatmapOptions Parse(string[] args) {
            var options = new ChatGptHeatmapOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--json":
                        options.Json = true;
                        break;
                    case "--out":
                        options.OutputPath = ReadValue(args, ref i);
                        break;
                    case "--account-id":
                        options.AccountId = ReadValue(args, ref i);
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
                    case "-h":
                    case "--help":
                        throw new InvalidOperationException("Run `intelligencex heatmap help` to see supported options.");
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private sealed class GitHubHeatmapOptions {
        public bool Json { get; set; }
        public string? OutputPath { get; set; }
        public string? User { get; set; }
        public int Years { get; set; } = 5;
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }

        public static GitHubHeatmapOptions Parse(string[] args) {
            var options = new GitHubHeatmapOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--json":
                        options.Json = true;
                        break;
                    case "--out":
                        options.OutputPath = ReadValue(args, ref i);
                        break;
                    case "--user":
                    case "--login":
                        options.User = ReadValue(args, ref i);
                        break;
                    case "--years":
                        options.Years = int.Parse(ReadValue(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--from":
                        options.From = ParseDate(ReadValue(args, ref i), "--from");
                        break;
                    case "--to":
                        options.To = ParseDate(ReadValue(args, ref i), "--to");
                        break;
                    case "-h":
                    case "--help":
                        throw new InvalidOperationException("Run `intelligencex heatmap help` to see supported options.");
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private static DateTimeOffset ParseDate(string value, string optionName) {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            throw new InvalidOperationException($"{optionName} expects a date in yyyy-MM-dd format.");
        }
        return new DateTimeOffset(parsed.Date, TimeSpan.Zero);
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        var value = args[index];
        if (value.StartsWith("--", StringComparison.Ordinal)) {
            throw new InvalidOperationException($"Missing value for {args[index - 1]}.");
        }
        return value;
    }

    private static bool MatchesProvider(UsageEventRecord record, string? providerId) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            return true;
        }
        return string.Equals(record.ProviderId, providerId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAccount(UsageEventRecord record, string? accountFilter) {
        if (string.IsNullOrWhiteSpace(accountFilter)) {
            return true;
        }

        var filter = accountFilter.Trim();
        return string.Equals(record.ProviderAccountId, filter, StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.AccountLabel, filter, StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.PersonLabel, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPerson(UsageEventRecord record, string? personFilter) {
        if (string.IsNullOrWhiteSpace(personFilter)) {
            return true;
        }

        var filter = personFilter.Trim();
        return string.Equals(record.PersonLabel, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUsageTitle(UsageHeatmapCliOptions options) {
        if (!string.IsNullOrWhiteSpace(options.ProviderId)) {
            return options.ProviderId!.Trim() + " usage";
        }
        return "Usage Heatmap";
    }

    private static string? BuildUsageSubtitle(UsageHeatmapCliOptions options) {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ProviderId)) {
            parts.Add("provider: " + options.ProviderId!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(options.AccountFilter)) {
            parts.Add("account: " + options.AccountFilter!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(options.PersonFilter)) {
            parts.Add("person: " + options.PersonFilter!.Trim());
        }
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private sealed class UsageHeatmapCliOptions {
        public bool Json { get; set; }
        public string? OutputPath { get; set; }
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? AccountFilter { get; set; }
        public string? PersonFilter { get; set; }
        public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;
        public UsageHeatmapBreakdownDimension Breakdown { get; set; } = UsageHeatmapBreakdownDimension.Surface;
        public string? Title { get; set; }

        public static UsageHeatmapCliOptions Parse(string[] args) {
            var options = new UsageHeatmapCliOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--json":
                        options.Json = true;
                        break;
                    case "--out":
                        options.OutputPath = ReadValue(args, ref i);
                        break;
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--account":
                        options.AccountFilter = ReadValue(args, ref i);
                        break;
                    case "--person":
                        options.PersonFilter = ReadValue(args, ref i);
                        break;
                    case "--metric":
                        options.Metric = ParseMetric(ReadValue(args, ref i));
                        break;
                    case "--breakdown":
                        options.Breakdown = ParseBreakdown(ReadValue(args, ref i));
                        break;
                    case "--title":
                        options.Title = ReadValue(args, ref i);
                        break;
                    case "-h":
                    case "--help":
                        throw new InvalidOperationException("Run `intelligencex heatmap help` to see supported options.");
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            if (string.IsNullOrWhiteSpace(options.DatabasePath)) {
                throw new InvalidOperationException("Missing telemetry database path. Use --db <path>.");
            }

            return options;
        }

        private static UsageSummaryMetric ParseMetric(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "tokens" or "token" => UsageSummaryMetric.TotalTokens,
                "cost" or "usd" => UsageSummaryMetric.CostUsd,
                "duration" or "ms" => UsageSummaryMetric.DurationMs,
                "events" or "event" => UsageSummaryMetric.EventCount,
                _ => throw new InvalidOperationException("Unsupported metric. Use tokens, cost, duration, or events.")
            };
        }

        private static UsageHeatmapBreakdownDimension ParseBreakdown(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "provider" => UsageHeatmapBreakdownDimension.Provider,
                "account" => UsageHeatmapBreakdownDimension.Account,
                "person" => UsageHeatmapBreakdownDimension.Person,
                "model" => UsageHeatmapBreakdownDimension.Model,
                "surface" => UsageHeatmapBreakdownDimension.Surface,
                _ => throw new InvalidOperationException("Unsupported breakdown. Use surface, provider, account, person, or model.")
            };
        }
    }
}
