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
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;
using IntelligenceX.Visualization.Heatmaps;
using Spectre.Console;

namespace IntelligenceX.Cli.Telemetry;

internal static class UsageTelemetryCliRunner {
    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try {
            if (string.Equals(command, "roots", StringComparison.OrdinalIgnoreCase)) {
                return await RunRootsAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "accounts", StringComparison.OrdinalIgnoreCase)) {
                return await RunAccountsAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "discover", StringComparison.OrdinalIgnoreCase)) {
                return await RunDiscoverAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "import", StringComparison.OrdinalIgnoreCase)) {
                return await RunImportAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "report", StringComparison.OrdinalIgnoreCase)) {
                return await RunReportAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "overview", StringComparison.OrdinalIgnoreCase)) {
                return await RunOverviewAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "stats", StringComparison.OrdinalIgnoreCase)) {
                return RunStatsAsync(rest);
            }

            return PrintHelpReturn();
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex telemetry usage roots list [options]");
        Console.WriteLine("  intelligencex telemetry usage roots add [options]");
        Console.WriteLine("  intelligencex telemetry usage accounts list [options]");
        Console.WriteLine("  intelligencex telemetry usage accounts bind [options]");
        Console.WriteLine("  intelligencex telemetry usage discover [options]");
        Console.WriteLine("  intelligencex telemetry usage import [options]");
        Console.WriteLine("  intelligencex telemetry usage report [options]");
        Console.WriteLine("  intelligencex telemetry usage overview [options]");
        Console.WriteLine("  intelligencex telemetry usage stats [options]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --db <path>           SQLite telemetry database path");
        Console.WriteLine("                        Defaults to the runtime telemetry path when omitted.");
        Console.WriteLine("  --provider <id>       Limit to a provider (ix, codex, claude, ...)");
        Console.WriteLine("  --json                Emit normalized JSON instead of text");
        Console.WriteLine();
        Console.WriteLine("Roots add options:");
        Console.WriteLine("  --path <path>         Source root path or logical locator");
        Console.WriteLine("  --source-kind <kind>  local|recovered|cli|oauth|web|compatible|ix");
        Console.WriteLine("  --platform <value>    Optional platform hint (windows, wsl, mac, recovered)");
        Console.WriteLine("  --machine <value>     Optional machine label");
        Console.WriteLine("  --account-hint <text> Optional account hint or label");
        Console.WriteLine("  --disabled            Register the root in disabled state");
        Console.WriteLine();
        Console.WriteLine("Accounts bind options:");
        Console.WriteLine("  --source-root <id>                Match one registered source root");
        Console.WriteLine("  --match-provider-account-id <id>  Match imported provider account id");
        Console.WriteLine("  --match-account-label <label>     Match imported raw account label");
        Console.WriteLine("  --provider-account-id <id>        Canonical provider account id");
        Console.WriteLine("  --account-label <label>           Canonical account label");
        Console.WriteLine("  --person-label <label>            Optional person-level grouping label");
        Console.WriteLine("  --disabled                        Register the binding in disabled state");
        Console.WriteLine();
        Console.WriteLine("Import options:");
        Console.WriteLine("  --discover            Run default root discovery before importing");
        Console.WriteLine("  --machine <value>     Override imported machine id");
        Console.WriteLine("  --parser-version <v>  Optional parser version label");
        Console.WriteLine("  --recent-first        Prefer newer artifacts first when crawling large roots");
        Console.WriteLine("  --max-artifacts <n>   Parse at most N source artifacts in this run, then resume later");
        Console.WriteLine("  --force               Reparse artifacts even when the incremental cache says they are unchanged");
        Console.WriteLine();
        Console.WriteLine("Report options:");
        Console.WriteLine("  --path <path>         Additional local or recovered folder/file to scan immediately");
        Console.WriteLine("                        Repeat to include multiple roots (for example Windows.old backups).");
        Console.WriteLine("  --account <value>     Filter by provider account id or account label");
        Console.WriteLine("  --person <value>      Filter by person label");
        Console.WriteLine("  --github-user <login> Add a GitHub contribution section for the specified login");
        Console.WriteLine("  --github-owner <id>   Add a GitHub owner/org scope for repository-impact stats");
        Console.WriteLine("  --metric <name>       tokens|cost|duration|events (default: tokens)");
        Console.WriteLine("  --title <text>        Override the report title");
        Console.WriteLine("  --recent-first        Prefer newer artifacts first during quick scans (default)");
        Console.WriteLine("  --max-artifacts <n>   Parse at most N artifacts during the quick scan");
        Console.WriteLine("  --full-import         Use the durable DB-backed import path instead of the quick scan");
        Console.WriteLine("  --out <path>          Write text or JSON output to a file");
        Console.WriteLine("  --out-dir <path>      Export overview.json plus one SVG/JSON pair per heatmap");
        Console.WriteLine();
        Console.WriteLine("Overview options:");
        Console.WriteLine("  --account <value>     Filter by provider account id or account label");
        Console.WriteLine("  --person <value>      Filter by person label");
        Console.WriteLine("  --github-user <login> Add a GitHub contribution section for the specified login");
        Console.WriteLine("  --github-owner <id>   Add a GitHub owner/org scope for repository-impact stats");
        Console.WriteLine("  --metric <name>       tokens|cost|duration|events (default: tokens)");
        Console.WriteLine("  --title <text>        Override the overview title");
        Console.WriteLine("  --discover            Auto-discover provider roots before rendering");
        Console.WriteLine("  --recent-first        Prefer newer artifacts first during auto-import");
        Console.WriteLine("  --max-artifacts <n>   Parse at most N artifacts during auto-import");
        Console.WriteLine("  --force               Reparse cached artifacts during auto-import");
        Console.WriteLine("  --out <path>          Write text or JSON output to a file");
        Console.WriteLine("  --out-dir <path>      Export overview.json plus one SVG/JSON pair per heatmap");
        Console.WriteLine();
        Console.WriteLine("Stats options:");
        Console.WriteLine("  --account <value>     Filter by provider account id or account label");
    }

    private static Task<int> RunRootsAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return Task.FromResult(command switch {
            "list" => RunRootsList(rest),
            "add" => RunRootsAdd(rest),
            _ => PrintHelpReturn()
        });
    }

    private static Task<int> RunAccountsAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return Task.FromResult(command switch {
            "list" => RunAccountsList(rest),
            "bind" => RunAccountsBind(rest),
            _ => PrintHelpReturn()
        });
    }

    private static int RunRootsList(string[] args) {
        var options = RootsListOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rootStore = new SqliteSourceRootStore(dbPath);
        var roots = rootStore.GetAll()
            .Where(root => MatchesProvider(root.ProviderId, options.ProviderId))
            .OrderBy(root => root.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (options.Json) {
            var rootsArray = new JsonArray();
            foreach (var root in roots) {
                rootsArray.Add(ToJson(root));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("roots", rootsArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Telemetry source roots");
        Console.WriteLine("Database: " + dbPath);
        if (roots.Length == 0) {
            Console.WriteLine("Roots: none");
            return 0;
        }

        Console.WriteLine("Roots:");
        foreach (var root in roots) {
            Console.WriteLine($"- [{root.ProviderId}] {root.SourceKind} | enabled={(root.Enabled ? "yes" : "no")} | {root.Path}");
            if (!string.IsNullOrWhiteSpace(root.PlatformHint)) {
                Console.WriteLine($"  platform: {root.PlatformHint}");
            }
            if (!string.IsNullOrWhiteSpace(root.MachineLabel)) {
                Console.WriteLine($"  machine: {root.MachineLabel}");
            }
            if (!string.IsNullOrWhiteSpace(root.AccountHint)) {
                Console.WriteLine($"  account hint: {root.AccountHint}");
            }
        }

        return 0;
    }

    private static int RunRootsAdd(string[] args) {
        var options = RootAddOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rootStore = new SqliteSourceRootStore(dbPath);
        var root = new SourceRootRecord(
            SourceRootRecord.CreateStableId(options.ProviderId!, options.SourceKind, options.Path!),
            options.ProviderId!,
            options.SourceKind,
            options.Path!) {
            PlatformHint = NormalizeOptional(options.PlatformHint),
            MachineLabel = NormalizeOptional(options.MachineLabel),
            AccountHint = NormalizeOptional(options.AccountHint),
            Enabled = !options.Disabled
        };
        rootStore.Upsert(root);

        if (options.Json) {
            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("root", ToJson(root));
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine($"Registered source root {root.Id} for provider '{root.ProviderId}'.");
        Console.WriteLine(root.Path);
        return 0;
    }

    private static int RunAccountsList(string[] args) {
        var options = AccountsListOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var bindingStore = new SqliteUsageAccountBindingStore(dbPath);
        var bindings = bindingStore.GetAll()
            .Where(binding => MatchesProvider(binding.ProviderId, options.ProviderId))
            .OrderBy(binding => binding.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(binding => binding.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (options.Json) {
            var bindingsArray = new JsonArray();
            foreach (var binding in bindings) {
                bindingsArray.Add(ToJson(binding));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("bindings", bindingsArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Telemetry account bindings");
        Console.WriteLine("Database: " + dbPath);
        if (bindings.Length == 0) {
            Console.WriteLine("Bindings: none");
            return 0;
        }

        Console.WriteLine("Bindings:");
        foreach (var binding in bindings) {
            Console.WriteLine($"- [{binding.ProviderId}] {binding.Id} | enabled={(binding.Enabled ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(binding.SourceRootId)) {
                Console.WriteLine($"  source root: {binding.SourceRootId}");
            }
            if (!string.IsNullOrWhiteSpace(binding.MatchProviderAccountId)) {
                Console.WriteLine($"  match provider account: {binding.MatchProviderAccountId}");
            }
            if (!string.IsNullOrWhiteSpace(binding.MatchAccountLabel)) {
                Console.WriteLine($"  match account label: {binding.MatchAccountLabel}");
            }
            if (!string.IsNullOrWhiteSpace(binding.ProviderAccountId)) {
                Console.WriteLine($"  canonical provider account: {binding.ProviderAccountId}");
            }
            if (!string.IsNullOrWhiteSpace(binding.AccountLabel)) {
                Console.WriteLine($"  canonical account label: {binding.AccountLabel}");
            }
            if (!string.IsNullOrWhiteSpace(binding.PersonLabel)) {
                Console.WriteLine($"  person label: {binding.PersonLabel}");
            }
        }

        return 0;
    }

    private static int RunAccountsBind(string[] args) {
        var options = AccountBindOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var bindingStore = new SqliteUsageAccountBindingStore(dbPath);
        var binding = new UsageAccountBindingRecord(
            UsageAccountBindingRecord.CreateStableId(
                options.ProviderId!,
                options.SourceRootId,
                options.MatchProviderAccountId,
                options.MatchAccountLabel),
            options.ProviderId!) {
            SourceRootId = NormalizeOptional(options.SourceRootId),
            MatchProviderAccountId = NormalizeOptional(options.MatchProviderAccountId),
            MatchAccountLabel = NormalizeOptional(options.MatchAccountLabel),
            ProviderAccountId = NormalizeOptional(options.ProviderAccountId),
            AccountLabel = NormalizeOptional(options.AccountLabel),
            PersonLabel = NormalizeOptional(options.PersonLabel),
            Enabled = !options.Disabled
        };
        bindingStore.Upsert(binding);

        if (options.Json) {
            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("binding", ToJson(binding));
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine($"Registered telemetry account binding {binding.Id} for provider '{binding.ProviderId}'.");
        return 0;
    }

    private static async Task<int> RunDiscoverAsync(string[] args) {
        var options = DiscoverOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rootStore = new SqliteSourceRootStore(dbPath);
        using var eventStore = new SqliteUsageEventStore(dbPath);
        var coordinator = CreateCoordinator(rootStore, eventStore);
        var roots = await coordinator.DiscoverRootsAsync(options.ProviderId, CancellationToken.None).ConfigureAwait(false);

        if (options.Json) {
            var rootsArray = new JsonArray();
            foreach (var root in roots) {
                rootsArray.Add(ToJson(root));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("providerId", options.ProviderId)
                .Add("roots", rootsArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Discovered telemetry roots");
        Console.WriteLine("Database: " + dbPath);
        Console.WriteLine("Count: " + roots.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var root in roots) {
            Console.WriteLine($"- [{root.ProviderId}] {root.Path}");
        }
        return 0;
    }

    private static async Task<int> RunImportAsync(string[] args) {
        var options = ImportOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rootStore = new SqliteSourceRootStore(dbPath);
        using var bindingStore = new SqliteUsageAccountBindingStore(dbPath);
        using var rawArtifactStore = new SqliteRawArtifactStore(dbPath);
        using var eventStore = new SqliteUsageEventStore(dbPath);
        var coordinator = CreateCoordinator(rootStore, eventStore);

        if (options.Discover) {
            await RunUsageStatusAsync(
                "Discovering telemetry roots...",
                "Discovering telemetry roots...",
                static _ => Task.CompletedTask,
                async progress => {
                    progress("Discovering telemetry roots...");
                    var discovered = await coordinator.DiscoverRootsAsync(options.ProviderId, CancellationToken.None).ConfigureAwait(false);
                    progress("Discovered " + discovered.Count.ToString(CultureInfo.InvariantCulture) + " telemetry root(s)");
                }).ConfigureAwait(false);
        }

        var result = new UsageImportBatchResult();
        await RunUsageStatusAsync(
            "Importing telemetry usage...",
            "Importing telemetry usage...",
            static _ => Task.CompletedTask,
            async progress => {
                result = await coordinator.ImportAllAsync(
                    new UsageImportContext {
                        MachineId = options.MachineId,
                        ParserVersion = options.ParserVersion,
                        AccountResolver = new UsageAccountBindingResolver(bindingStore),
                        RawArtifactStore = rawArtifactStore,
                        PreferRecentArtifacts = options.RecentFirst,
                        MaxArtifacts = options.MaxArtifacts,
                        ForceReimport = options.Force,
                        Progress = update => progress(BuildProgressMessage(update))
                    },
                    options.ProviderId,
                    CancellationToken.None).ConfigureAwait(false);
                progress("Imported " + result.EventsInserted.ToString(CultureInfo.InvariantCulture) + " new event(s)");
            }).ConfigureAwait(false);

        if (options.Json) {
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(ToJson(result))));
            return 0;
        }

        Console.WriteLine("Telemetry import");
        Console.WriteLine("Database: " + dbPath);
        Console.WriteLine($"Roots considered: {result.RootsConsidered}");
        Console.WriteLine($"Roots imported: {result.RootsImported}");
        Console.WriteLine($"Artifacts processed: {result.ArtifactsProcessed}");
        Console.WriteLine($"Events read: {result.EventsRead}");
        Console.WriteLine($"Events inserted: {result.EventsInserted}");
        Console.WriteLine($"Events updated: {result.EventsUpdated}");
        if (result.ArtifactBudgetReached) {
            Console.WriteLine("Budget: reached artifact limit; rerun import to continue from cached progress");
        }
        foreach (var root in result.Roots) {
            Console.WriteLine($"- [{root.ProviderId}] {root.RootId} | imported={(root.Imported ? "yes" : "no")} | artifacts={root.ArtifactsProcessed} read={root.EventsRead} inserted={root.EventsInserted} updated={root.EventsUpdated}");
            if (!string.IsNullOrWhiteSpace(root.Message)) {
                Console.WriteLine($"  note: {root.Message}");
            }
        }
        return 0;
    }

    private static int RunStatsAsync(string[] args) {
        var options = StatsOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rootStore = new SqliteSourceRootStore(dbPath);
        using var bindingStore = new SqliteUsageAccountBindingStore(dbPath);
        using var eventStore = new SqliteUsageEventStore(dbPath);

        var roots = rootStore.GetAll()
            .Where(root => MatchesProvider(root.ProviderId, options.ProviderId))
            .ToArray();
        var bindings = bindingStore.GetAll()
            .Where(binding => MatchesProvider(binding.ProviderId, options.ProviderId))
            .ToArray();
        var events = eventStore.GetAll()
            .Where(record => MatchesProvider(record.ProviderId, options.ProviderId))
            .Where(record => MatchesAccount(record, options.AccountFilter))
            .OrderBy(record => record.TimestampUtc)
            .ToArray();

        var providerStats = events
            .GroupBy(record => record.ProviderId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProviderStats(
                group.Key,
                roots.Count(root => string.Equals(root.ProviderId, group.Key, StringComparison.OrdinalIgnoreCase)),
                group.Count(),
                CountDistinctAccounts(group),
                group.Min(record => record.TimestampUtc),
                group.Max(record => record.TimestampUtc),
                group.Sum(record => record.TotalTokens ?? 0L)))
            .ToArray();

        if (options.Json) {
            var providersArray = new JsonArray();
            foreach (var provider in providerStats) {
                providersArray.Add(ToJson(provider));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("rootCount", roots.Length)
                .Add("bindingCount", bindings.Length)
                .Add("eventCount", events.Length)
                .Add("accountCount", CountDistinctAccounts(events))
                .Add("personCount", CountDistinctPersons(events))
                .Add("from", events.Length == 0 ? null : events[0].TimestampUtc.ToString("O", CultureInfo.InvariantCulture))
                .Add("to", events.Length == 0 ? null : events[^1].TimestampUtc.ToString("O", CultureInfo.InvariantCulture))
                .Add("providers", providersArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Telemetry stats");
        Console.WriteLine("Database: " + dbPath);
        Console.WriteLine($"Roots: {roots.Length}");
        Console.WriteLine($"Bindings: {bindings.Length}");
        Console.WriteLine($"Events: {events.Length}");
        Console.WriteLine($"Accounts: {CountDistinctAccounts(events)}");
        Console.WriteLine($"People: {CountDistinctPersons(events)}");
        if (events.Length > 0) {
            Console.WriteLine($"Range: {events[0].TimestampUtc:yyyy-MM-dd} -> {events[^1].TimestampUtc:yyyy-MM-dd}");
        }
        if (providerStats.Length == 0) {
            Console.WriteLine("Providers: none");
            return 0;
        }

        Console.WriteLine("Providers:");
        foreach (var provider in providerStats) {
            Console.WriteLine($"- {provider.ProviderId} | roots={provider.RootCount} events={provider.EventCount} accounts={provider.AccountCount} tokens={provider.TotalTokens}");
            Console.WriteLine($"  range: {provider.FromUtc:yyyy-MM-dd} -> {provider.ToUtc:yyyy-MM-dd}");
        }
        return 0;
    }

    private static async Task<int> RunReportAsync(string[] args) {
        var options = ReportOptions.Parse(args);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory)
            && string.IsNullOrWhiteSpace(options.OutputPath)
            && !options.Json) {
            options.OutputDirectory = Path.Combine("artifacts", "telemetry", "reports", "latest");
        }

        if (options.FullImport) {
            var overviewArgs = new List<string>();
            AddIfValue(overviewArgs, "--db", options.DatabasePath);
            AddIfValue(overviewArgs, "--provider", options.ProviderId);
            AddIfValue(overviewArgs, "--account", options.AccountFilter);
            AddIfValue(overviewArgs, "--person", options.PersonFilter);
            AddIfValue(overviewArgs, "--metric", MetricToCliValue(options.Metric));
            AddIfValue(overviewArgs, "--title", options.Title);
            foreach (var owner in options.GitHubOwners) {
                AddIfValue(overviewArgs, "--github-owner", owner);
            }
            foreach (var user in options.GitHubUsers) {
                AddIfValue(overviewArgs, "--github-user", user);
            }
            if (options.RecentFirst) {
                overviewArgs.Add("--recent-first");
            }
            if (options.MaxArtifacts.HasValue) {
                overviewArgs.Add("--max-artifacts");
                overviewArgs.Add(options.MaxArtifacts.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (options.Force) {
                overviewArgs.Add("--force");
            }
            if (options.Json) {
                overviewArgs.Add("--json");
            }
            AddIfValue(overviewArgs, "--out", options.OutputPath);
            AddIfValue(overviewArgs, "--out-dir", options.OutputDirectory);
            overviewArgs.Add("--discover");
            return await RunOverviewAsync(overviewArgs.ToArray()).ConfigureAwait(false);
        }

        return await RunQuickReportAsync(options).ConfigureAwait(false);
    }

    private static async Task<int> RunQuickReportAsync(ReportOptions options) {
        var sourceRootStore = new InMemorySourceRootStore();
        var eventStore = new InMemoryUsageEventStore();
        var coordinator = CreateCoordinator(sourceRootStore, eventStore);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var rawArtifactStore = new SqliteRawArtifactStore(dbPath);

        var quickScanArtifactBudget = options.MaxArtifacts;
        UsageTelemetryQuickReportResult scanResult = new();
        await RunUsageStatusAsync(
            "Preparing quick telemetry report...",
            "Preparing quick telemetry report...",
            static _ => Task.CompletedTask,
            async progress => {
                progress("Discovering telemetry roots...");
                var discovered = await coordinator.DiscoverRootsAsync(options.ProviderId, CancellationToken.None).ConfigureAwait(false);
                progress("Discovered " + discovered.Count.ToString(CultureInfo.InvariantCulture) + " default telemetry root(s)");

                var adHocRoots = RegisterAdHocReportRoots(sourceRootStore, options);
                if (adHocRoots.Count > 0) {
                    progress("Queued " + adHocRoots.Count.ToString(CultureInfo.InvariantCulture) + " additional path(s) for the quick scan");
                }

                scanResult = await new UsageTelemetryQuickReportScanner().ScanAsync(
                    sourceRootStore.GetAll(),
                    new UsageTelemetryQuickReportOptions {
                        ProviderId = options.ProviderId,
                        MachineId = null,
                        RawArtifactStore = rawArtifactStore,
                        PreferRecentArtifacts = options.RecentFirst,
                        MaxArtifacts = quickScanArtifactBudget,
                        ForceReimport = options.Force,
                        Progress = update => progress(BuildProgressMessage(update))
                    },
                    CancellationToken.None).ConfigureAwait(false);

                progress("Parsed " + scanResult.ArtifactsParsed.ToString(CultureInfo.InvariantCulture)
                         + " artifact(s), reused " + scanResult.ArtifactsReused.ToString(CultureInfo.InvariantCulture)
                         + " cached artifact(s)");
                progress("Rendering usage report...");
            }).ConfigureAwait(false);

        var events = scanResult.Events
            .Where(record => MatchesProvider(record.ProviderId, options.ProviderId))
            .Where(record => MatchesAccount(record, options.AccountFilter))
            .Where(record => MatchesPerson(record, options.PersonFilter))
            .OrderBy(record => record.TimestampUtc)
            .ToArray();

        if (events.Length == 0) {
            throw new InvalidOperationException("No telemetry usage events matched the requested report filters.");
        }

        var effectiveProviderId = NormalizeOptional(options.ProviderId)
                                  ?? InferSingleProviderId(events);
        var overview = new UsageTelemetryOverviewBuilder().Build(
            events,
            new UsageTelemetryOverviewOptions {
                Metric = options.Metric,
                Title = NormalizeOptional(options.Title) ?? BuildOverviewTitle(effectiveProviderId),
                Subtitle = BuildQuickReportSubtitle(options, dbPath, scanResult),
                SourceRootLabels = BuildSourceRootLabels(sourceRootStore.GetAll())
            });
        overview = await AppendGitHubSectionsAsync(overview, options.GitHubUsers, options.GitHubOwners).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory)) {
            var outputDirectory = Path.GetFullPath(options.OutputDirectory!);
            WriteOverviewArtifacts(overview, outputDirectory);
            Console.WriteLine("Wrote " + outputDirectory);
            return 0;
        }

        var content = options.Json
            ? JsonLite.Serialize(JsonValue.From(overview.ToJson()))
            : BuildOverviewText(dbPath, overview);

        if (string.IsNullOrWhiteSpace(options.OutputPath)) {
            Console.WriteLine(content);
            return 0;
        }

        var outputPath = Path.GetFullPath(options.OutputPath!);
        var outDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outDirectory)) {
            Directory.CreateDirectory(outDirectory);
        }

        File.WriteAllText(outputPath, content, new UTF8Encoding(false));
        Console.WriteLine("Wrote " + outputPath);
        return 0;
    }

    private static async Task<int> RunOverviewAsync(string[] args) {
        var options = OverviewOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        if (options.Discover) {
            await DiscoverAndImportForOverviewAsync(dbPath, options).ConfigureAwait(false);
        }

        using var eventStore = new SqliteUsageEventStore(dbPath);
        var events = LoadOverviewEvents(eventStore, options);
        if (events.Length == 0 && !options.SkipAutoImport) {
            await DiscoverAndImportForOverviewAsync(dbPath, options).ConfigureAwait(false);
            events = LoadOverviewEvents(eventStore, options);
        }

        if (events.Length == 0) {
            throw new InvalidOperationException("No telemetry usage events matched the requested overview filters.");
        }

        using var sourceRootStore = new SqliteSourceRootStore(dbPath);
        var overview = new UsageTelemetryOverviewBuilder().Build(
            events,
            new UsageTelemetryOverviewOptions {
                Metric = options.Metric,
                Title = NormalizeOptional(options.Title) ?? BuildOverviewTitle(options),
                Subtitle = BuildOverviewSubtitle(options),
                SourceRootLabels = BuildSourceRootLabels(sourceRootStore.GetAll())
            });
        overview = await AppendGitHubSectionsAsync(overview, options.GitHubUsers, options.GitHubOwners).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory)) {
            var outputDirectory = Path.GetFullPath(options.OutputDirectory!);
            WriteOverviewArtifacts(overview, outputDirectory);
            Console.WriteLine("Wrote " + outputDirectory);
            return 0;
        }

        var content = options.Json
            ? JsonLite.Serialize(JsonValue.From(overview.ToJson()))
            : BuildOverviewText(dbPath, overview);

        if (string.IsNullOrWhiteSpace(options.OutputPath)) {
            Console.WriteLine(content);
            return 0;
        }

        var outputPath = Path.GetFullPath(options.OutputPath!);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, content, new UTF8Encoding(false));
        Console.WriteLine("Wrote " + outputPath);
        return 0;
    }

    private static UsageEventRecord[] LoadOverviewEvents(SqliteUsageEventStore eventStore, OverviewOptions options) {
        return eventStore.GetAll()
            .Where(record => MatchesProvider(record.ProviderId, options.ProviderId))
            .Where(record => MatchesAccount(record, options.AccountFilter))
            .Where(record => MatchesPerson(record, options.PersonFilter))
            .OrderBy(record => record.TimestampUtc)
            .ToArray();
    }

    private static async Task<UsageTelemetryOverviewDocument> AppendGitHubSectionsAsync(
        UsageTelemetryOverviewDocument overview,
        IReadOnlyList<string> githubUsers,
        IReadOnlyList<string> githubOwners) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }
        if (githubUsers is null || githubUsers.Count == 0) {
            return overview;
        }

        var owners = (githubOwners ?? Array.Empty<string>())
            .Select(NormalizeOptional)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        var sections = overview.ProviderSections.ToList();
        foreach (var login in githubUsers
                     .Select(NormalizeOptional)
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)!) {
            sections.Add(await GitHubOverviewSectionBuilder.BuildAsync(login!, owners).ConfigureAwait(false));
        }

        return new UsageTelemetryOverviewDocument(
            overview.Title,
            overview.Subtitle,
            overview.Metric,
            overview.Units,
            overview.Summary,
            overview.Cards,
            overview.Heatmaps,
            sections
                .OrderBy(static section => ResolveProviderSortOrder(section.ProviderId))
                .ThenBy(static section => section.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static int ResolveProviderSortOrder(string providerId) {
        return NormalizeOptional(providerId)?.ToLowerInvariant() switch {
            "codex" => 0,
            "claude" => 1,
            "github" => 2,
            _ => 10
        };
    }

    private static async Task DiscoverAndImportForOverviewAsync(string dbPath, OverviewOptions options) {
        using var rootStore = new SqliteSourceRootStore(dbPath);
        using var bindingStore = new SqliteUsageAccountBindingStore(dbPath);
        using var rawArtifactStore = new SqliteRawArtifactStore(dbPath);
        using var eventStore = new SqliteUsageEventStore(dbPath);
        var coordinator = CreateCoordinator(rootStore, eventStore);
        await RunUsageStatusAsync(
            "Preparing telemetry report...",
            "Preparing telemetry report...",
            static _ => Task.CompletedTask,
            async progress => {
                progress("Discovering telemetry roots...");
                var discovered = await coordinator.DiscoverRootsAsync(options.ProviderId, CancellationToken.None).ConfigureAwait(false);
                progress("Discovered " + discovered.Count.ToString(CultureInfo.InvariantCulture) + " telemetry root(s)");

                await coordinator.ImportAllAsync(
                    new UsageImportContext {
                        AccountResolver = new UsageAccountBindingResolver(bindingStore),
                        RawArtifactStore = rawArtifactStore,
                        PreferRecentArtifacts = options.RecentFirst,
                        MaxArtifacts = options.MaxArtifacts,
                        ForceReimport = options.Force,
                        Progress = update => progress(BuildProgressMessage(update))
                    },
                    options.ProviderId,
                    CancellationToken.None).ConfigureAwait(false);

                progress("Rendering usage report...");
            }).ConfigureAwait(false);
    }

    private static string BuildProgressMessage(UsageImportProgressUpdate update) {
        var message = NormalizeOptional(update.Message);
        if (!string.IsNullOrWhiteSpace(message)) {
            return message!;
        }

        return NormalizeOptional(update.Phase) ?? "Working...";
    }

    private static async Task RunUsageStatusAsync(
        string initialStatus,
        string fallbackStatus,
        Func<string, Task> plainUpdate,
        Func<Action<string>, Task> action) {
        if (Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Ansi) {
            await plainUpdate(fallbackStatus).ConfigureAwait(false);
            await action(_ => { }).ConfigureAwait(false);
            return;
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(initialStatus, async context => {
                void Update(string message) {
                    context.Status(BuildSpectreStatus(message));
                }

                await action(Update).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static string BuildSpectreStatus(string message) {
        return Markup.Escape(NormalizeOptional(message) ?? "Working...");
    }

    private static UsageTelemetryImportCoordinator CreateCoordinator(
        ISourceRootStore sourceRootStore,
        IUsageEventStore usageEventStore) {
        return new UsageTelemetryImportCoordinator(
            sourceRootStore,
            usageEventStore,
            new UsageTelemetryProviderRegistry(new IUsageTelemetryProviderDescriptor[] {
                new CodexUsageTelemetryProviderDescriptor(),
                new ClaudeUsageTelemetryProviderDescriptor()
            }),
            new IUsageTelemetryRootDiscovery[] {
                new CodexDefaultSourceRootDiscovery(),
                new ClaudeDefaultSourceRootDiscovery()
            });
    }

    private static string ResolveDatabasePath(string? explicitPath) {
        var resolved = UsageTelemetryPathResolver.ResolveDatabasePath(explicitPath, enabledByDefault: true);
        if (string.IsNullOrWhiteSpace(resolved)) {
            throw new InvalidOperationException("Unable to resolve telemetry database path.");
        }
        return resolved!;
    }

    private static JsonObject ToJson(SourceRootRecord root) {
        return new JsonObject()
            .Add("id", root.Id)
            .Add("providerId", root.ProviderId)
            .Add("sourceKind", root.SourceKind.ToString())
            .Add("path", root.Path)
            .Add("platformHint", root.PlatformHint)
            .Add("machineLabel", root.MachineLabel)
            .Add("accountHint", root.AccountHint)
            .Add("enabled", root.Enabled);
    }

    private static JsonObject ToJson(UsageAccountBindingRecord binding) {
        return new JsonObject()
            .Add("id", binding.Id)
            .Add("providerId", binding.ProviderId)
            .Add("sourceRootId", binding.SourceRootId)
            .Add("matchProviderAccountId", binding.MatchProviderAccountId)
            .Add("matchAccountLabel", binding.MatchAccountLabel)
            .Add("providerAccountId", binding.ProviderAccountId)
            .Add("accountLabel", binding.AccountLabel)
            .Add("personLabel", binding.PersonLabel)
            .Add("enabled", binding.Enabled);
    }

    private static JsonObject ToJson(ProviderStats stats) {
        return new JsonObject()
            .Add("providerId", stats.ProviderId)
            .Add("rootCount", stats.RootCount)
            .Add("eventCount", stats.EventCount)
            .Add("accountCount", stats.AccountCount)
            .Add("from", stats.FromUtc.ToString("O", CultureInfo.InvariantCulture))
            .Add("to", stats.ToUtc.ToString("O", CultureInfo.InvariantCulture))
            .Add("totalTokens", stats.TotalTokens);
    }

    private static JsonObject ToJson(UsageImportBatchResult result) {
        var rootsArray = new JsonArray();
        foreach (var root in result.Roots) {
            var adapterIdsArray = new JsonArray();
            foreach (var adapterId in root.AdapterIds) {
                adapterIdsArray.Add(adapterId);
            }

            rootsArray.Add(new JsonObject()
                .Add("rootId", root.RootId)
                .Add("providerId", root.ProviderId)
                .Add("adapterIds", adapterIdsArray)
                .Add("artifactsProcessed", root.ArtifactsProcessed)
                .Add("artifactBudgetReached", root.ArtifactBudgetReached)
                .Add("eventsRead", root.EventsRead)
                .Add("eventsInserted", root.EventsInserted)
                .Add("eventsUpdated", root.EventsUpdated)
                .Add("imported", root.Imported)
                .Add("message", root.Message));
        }

        return new JsonObject()
            .Add("rootsConsidered", result.RootsConsidered)
            .Add("rootsImported", result.RootsImported)
            .Add("artifactsProcessed", result.ArtifactsProcessed)
            .Add("artifactBudgetReached", result.ArtifactBudgetReached)
            .Add("eventsRead", result.EventsRead)
            .Add("eventsInserted", result.EventsInserted)
            .Add("eventsUpdated", result.EventsUpdated)
            .Add("roots", rootsArray);
    }

    private static int CountDistinctAccounts(IEnumerable<UsageEventRecord> records) {
        return records
            .Select(record => NormalizeOptional(record.ProviderAccountId) ?? NormalizeOptional(record.AccountLabel))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int CountDistinctPersons(IEnumerable<UsageEventRecord> records) {
        return records
            .Select(record => NormalizeOptional(record.PersonLabel))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string BuildOverviewTitle(string? providerId) {
        if (!string.IsNullOrWhiteSpace(providerId)) {
            return providerId!.Trim() + " overview";
        }

        return "Usage Overview";
    }

    private static string BuildOverviewTitle(OverviewOptions options) {
        return BuildOverviewTitle(options.ProviderId);
    }

    private static string? BuildOverviewSubtitle(OverviewOptions options) {
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

    private static string? BuildQuickReportSubtitle(
        ReportOptions options,
        string? dbPath,
        UsageTelemetryQuickReportResult scanResult) {
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
        parts.Add("quick scan");
        if (options.RecentFirst) {
            parts.Add("recent-first");
        }
        if (options.MaxArtifacts.HasValue) {
            parts.Add("artifact cap: " + options.MaxArtifacts.Value.ToString(CultureInfo.InvariantCulture));
        }
        parts.Add("parsed: " + scanResult.ArtifactsParsed.ToString(CultureInfo.InvariantCulture));
        parts.Add("cached: " + scanResult.ArtifactsReused.ToString(CultureInfo.InvariantCulture));
        if (scanResult.ArtifactBudgetReached) {
            parts.Add("partial");
        }
        if (!string.IsNullOrWhiteSpace(dbPath)) {
            parts.Add("cache: " + dbPath);
        }
        return string.Join(" | ", parts);
    }

    private static string? InferSingleProviderId(IReadOnlyList<UsageEventRecord> events) {
        var providers = events
            .Select(static record => NormalizeOptional(record.ProviderId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return providers.Length == 1 ? providers[0] : null;
    }

    private static IReadOnlyList<SourceRootRecord> RegisterAdHocReportRoots(
        ISourceRootStore sourceRootStore,
        ReportOptions options) {
        var roots = new List<SourceRootRecord>();
        foreach (var rawPath in options.Paths) {
            var normalizedPath = NormalizeOptional(rawPath);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }

            var providerId = NormalizeOptional(options.ProviderId) ?? InferProviderIdFromPath(normalizedPath!);
            if (string.IsNullOrWhiteSpace(providerId)) {
                throw new InvalidOperationException(
                    "Unable to infer the provider for '" + normalizedPath + "'. Use --provider <id> with --path.");
            }

            var sourceKind = InferSourceKindFromPath(normalizedPath!);
            var root = new SourceRootRecord(
                SourceRootRecord.CreateStableId(providerId!, sourceKind, normalizedPath!),
                providerId!,
                sourceKind,
                normalizedPath!);
            sourceRootStore.Upsert(root);
            roots.Add(root);
        }

        return roots;
    }

    private static UsageSourceKind InferSourceKindFromPath(string path) {
        var normalized = UsageTelemetryIdentity.NormalizePath(path);
        if (normalized.IndexOf("windows.old", StringComparison.OrdinalIgnoreCase) >= 0) {
            return UsageSourceKind.RecoveredFolder;
        }

        return UsageSourceKind.LocalLogs;
    }

    private static string? InferProviderIdFromPath(string path) {
        var normalized = UsageTelemetryIdentity.NormalizePath(path);
        if (normalized.IndexOf(".codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf(Path.DirectorySeparatorChar + "sessions", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf(Path.AltDirectorySeparatorChar + "sessions", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "codex";
        }

        if (normalized.IndexOf(".claude", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf(Path.DirectorySeparatorChar + "projects", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf(Path.AltDirectorySeparatorChar + "projects", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "claude";
        }

        return null;
    }

    private static void AddIfValue(List<string> args, string optionName, string? value) {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return;
        }

        args.Add(optionName);
        args.Add(normalized!);
    }

    private static string MetricToCliValue(UsageSummaryMetric metric) {
        return metric switch {
            UsageSummaryMetric.TotalTokens => "tokens",
            UsageSummaryMetric.CostUsd => "cost",
            UsageSummaryMetric.DurationMs => "duration",
            UsageSummaryMetric.EventCount => "events",
            _ => "tokens"
        };
    }

    private static string BuildOverviewText(string dbPath, UsageTelemetryOverviewDocument overview) {
        var lines = new List<string> {
            overview.Title,
            "Database: " + dbPath
        };

        if (!string.IsNullOrWhiteSpace(overview.Subtitle)) {
            lines.Add(overview.Subtitle!);
        }

        lines.Add("Cards:");
        foreach (var card in overview.Cards) {
            var line = "- " + card.Label + ": " + card.Value;
            if (!string.IsNullOrWhiteSpace(card.Subtitle)) {
                line += " (" + card.Subtitle + ")";
            }

            lines.Add(line);
        }

        lines.Add("Heatmaps:");
        foreach (var heatmap in overview.Heatmaps) {
            lines.Add("- " + heatmap.Label + ": " + heatmap.Document.Title);
        }

        if (overview.ProviderSections.Count > 0) {
            lines.Add("Providers:");
            foreach (var section in overview.ProviderSections) {
                lines.Add("- " + section.Title + ": total=" + section.TotalTokens.ToString(CultureInfo.InvariantCulture)
                          + " input=" + section.InputTokens.ToString(CultureInfo.InvariantCulture)
                          + " output=" + section.OutputTokens.ToString(CultureInfo.InvariantCulture));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteOverviewArtifacts(UsageTelemetryOverviewDocument overview, string outputDirectory) {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "overview.json"),
            JsonLite.Serialize(JsonValue.From(overview.ToJson())),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(outputDirectory, "index.html"),
            UsageTelemetryOverviewHtmlRenderer.Render(overview),
            new UTF8Encoding(false));

        foreach (var heatmap in overview.Heatmaps) {
            var lightDocument = CreateThemeVariant(heatmap.Document, darkMode: false);
            var darkDocument = CreateThemeVariant(heatmap.Document, darkMode: true);
            File.WriteAllText(
                Path.Combine(outputDirectory, heatmap.Key + ".json"),
                JsonLite.Serialize(JsonValue.From(heatmap.Document.ToJson())),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, heatmap.Key + ".svg"),
                HeatmapSvgRenderer.Render(lightDocument),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, heatmap.Key + ".light.svg"),
                HeatmapSvgRenderer.Render(lightDocument),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, heatmap.Key + ".dark.svg"),
                HeatmapSvgRenderer.Render(darkDocument),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, heatmap.Key + ".html"),
                UsageTelemetryBreakdownHtmlRenderer.Render(
                    overview.Title,
                    heatmap.Key,
                    heatmap.Label,
                    heatmap.Document.Subtitle),
                new UTF8Encoding(false));
        }

        foreach (var providerSection in overview.ProviderSections) {
            var lightDocument = CreateThemeVariant(providerSection.Heatmap, darkMode: false);
            var darkDocument = CreateThemeVariant(providerSection.Heatmap, darkMode: true);
            File.WriteAllText(
                Path.Combine(outputDirectory, providerSection.Key + ".json"),
                JsonLite.Serialize(JsonValue.From(providerSection.Heatmap.ToJson())),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, providerSection.Key + ".svg"),
                HeatmapSvgRenderer.Render(lightDocument),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, providerSection.Key + ".light.svg"),
                HeatmapSvgRenderer.Render(lightDocument),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, providerSection.Key + ".dark.svg"),
                HeatmapSvgRenderer.Render(darkDocument),
                new UTF8Encoding(false));
        }

        var githubSection = overview.ProviderSections.FirstOrDefault(section =>
            string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase));
        if (githubSection is not null) {
            File.WriteAllText(
                Path.Combine(outputDirectory, "github-wrapped.html"),
                GitHubWrappedHtmlRenderer.Render(githubSection),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "github-wrapped-card.html"),
                GitHubWrappedCardHtmlRenderer.Render(githubSection),
                new UTF8Encoding(false));
        }
    }

    private static HeatmapDocument CreateThemeVariant(HeatmapDocument source, bool darkMode) {
        var themedPalette = CreateThemeVariant(source.Palette, darkMode);
        var sections = source.Sections
            .Select(static section => new HeatmapSection(
                section.Title,
                section.Subtitle,
                section.Days
                    .Select(static day => new HeatmapDay(day.Date, day.Value, day.Level, day.FillColor, day.Tooltip, day.Breakdown))
                    .ToArray()))
            .ToArray();
        var legend = source.LegendItems
            .Select(static item => new HeatmapLegendItem(item.Label, item.Color))
            .ToArray();

        return new HeatmapDocument(
            source.Title,
            source.Subtitle,
            themedPalette,
            sections,
            units: source.Units,
            weekStart: source.WeekStart,
            showIntensityLegend: source.ShowIntensityLegend,
            legendLowLabel: source.LegendLowLabel,
            legendHighLabel: source.LegendHighLabel,
            legendItems: legend,
            showDocumentHeader: source.ShowDocumentHeader,
            showSectionHeaders: source.ShowSectionHeaders,
            compactWeekdayLabels: source.CompactWeekdayLabels);
    }

    private static HeatmapPalette CreateThemeVariant(HeatmapPalette source, bool darkMode) {
        if (darkMode) {
            return new HeatmapPalette(
                backgroundColor: "#0f1115",
                panelColor: "#171b22",
                textColor: "#f5f7fa",
                mutedTextColor: "#9aa4b2",
                emptyColor: "#252b34",
                intensityColors: source.IntensityColors.ToArray());
        }

        return new HeatmapPalette(
            backgroundColor: "#f6f8fa",
            panelColor: "#ffffff",
            textColor: "#24292f",
            mutedTextColor: "#57606a",
            emptyColor: "#ebedf0",
            intensityColors: source.IntensityColors.ToArray());
    }

    private static bool MatchesProvider(string providerId, string? filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }
        return string.Equals(providerId, filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildSourceRootLabels(IEnumerable<SourceRootRecord> roots) {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots ?? Array.Empty<SourceRootRecord>()) {
            if (root is null || string.IsNullOrWhiteSpace(root.Id)) {
                continue;
            }

            labels[root.Id] = BuildSourceRootLabel(root);
        }

        return labels;
    }

    private static string BuildSourceRootLabel(SourceRootRecord root) {
        var provider = BuildProviderTitle(root.ProviderId);
        var path = root.Path ?? string.Empty;
        var normalized = path.Replace('/', '\\');
        var location = normalized.IndexOf("Windows.old", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Windows.old"
            : normalized.StartsWith("ix://", StringComparison.OrdinalIgnoreCase)
                ? "Internal"
                : "Current";

        var leaf = Path.GetFileName(normalized.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(leaf)) {
            leaf = normalized;
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(normalized.TrimEnd('\\')) ?? string.Empty);
        if (string.Equals(leaf, "projects", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parent, ".claude", StringComparison.OrdinalIgnoreCase)) {
            leaf = ".claude/projects";
        } else if (string.Equals(leaf, "sessions", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(parent, ".codex", StringComparison.OrdinalIgnoreCase)) {
            leaf = ".codex/sessions";
        }

        if (normalized.StartsWith("ix://", StringComparison.OrdinalIgnoreCase)) {
            return provider + " · Internal IX";
        }

        return provider + " · " + location + " (" + leaf + ")";
    }

    private static string BuildProviderTitle(string? providerId) {
        return NormalizeOptional(providerId)?.ToLowerInvariant() switch {
            "codex" => "Codex",
            "claude" => "Claude",
            "ix" => "IntelligenceX",
            "chatgpt" => "ChatGPT",
            "github" => "GitHub",
            "lmstudio" => "LM Studio",
            "ollama" => "Ollama",
            _ => string.IsNullOrWhiteSpace(providerId) ? "Unknown" : providerId!.Trim()
        };
    }

    private static bool MatchesAccount(UsageEventRecord record, string? filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }

        var trimmed = filter.Trim();
        return string.Equals(record.ProviderAccountId, trimmed, StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.AccountLabel, trimmed, StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.PersonLabel, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPerson(UsageEventRecord record, string? filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }

        return string.Equals(record.PersonLabel, filter.Trim(), StringComparison.OrdinalIgnoreCase);
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

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase)
               || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
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

    private static UsageSourceKind ParseSourceKind(string value) {
        return value.Trim().ToLowerInvariant() switch {
            "local" or "locallogs" or "logs" => UsageSourceKind.LocalLogs,
            "recovered" or "backup" => UsageSourceKind.RecoveredFolder,
            "cli" or "cliprobe" => UsageSourceKind.CliProbe,
            "oauth" or "oauthapi" => UsageSourceKind.OAuthApi,
            "web" or "websession" => UsageSourceKind.WebSession,
            "compatible" or "compatibleapi" => UsageSourceKind.CompatibleApi,
            "ix" or "internalix" or "internal" => UsageSourceKind.InternalIx,
            _ => throw new InvalidOperationException("Unsupported source kind. Use local, recovered, cli, oauth, web, compatible, or ix.")
        };
    }

    private sealed class RootsListOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public bool Json { get; set; }

        public static RootsListOptions Parse(string[] args) {
            var options = new RootsListOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private sealed class RootAddOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? Path { get; set; }
        public UsageSourceKind SourceKind { get; set; } = UsageSourceKind.LocalLogs;
        public string? PlatformHint { get; set; }
        public string? MachineLabel { get; set; }
        public string? AccountHint { get; set; }
        public bool Disabled { get; set; }
        public bool Json { get; set; }

        public static RootAddOptions Parse(string[] args) {
            var options = new RootAddOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--path":
                        options.Path = ReadValue(args, ref i);
                        break;
                    case "--source-kind":
                        options.SourceKind = ParseSourceKind(ReadValue(args, ref i));
                        break;
                    case "--platform":
                        options.PlatformHint = ReadValue(args, ref i);
                        break;
                    case "--machine":
                        options.MachineLabel = ReadValue(args, ref i);
                        break;
                    case "--account-hint":
                        options.AccountHint = ReadValue(args, ref i);
                        break;
                    case "--disabled":
                        options.Disabled = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            if (string.IsNullOrWhiteSpace(options.ProviderId)) {
                throw new InvalidOperationException("Missing provider id. Use --provider <id>.");
            }
            if (string.IsNullOrWhiteSpace(options.Path)) {
                throw new InvalidOperationException("Missing source root path. Use --path <path>.");
            }

            return options;
        }
    }

    private sealed class DiscoverOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public bool Json { get; set; }

        public static DiscoverOptions Parse(string[] args) {
            var options = new DiscoverOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private sealed class AccountsListOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public bool Json { get; set; }

        public static AccountsListOptions Parse(string[] args) {
            var options = new AccountsListOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            return options;
        }
    }

    private sealed class AccountBindOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? SourceRootId { get; set; }
        public string? MatchProviderAccountId { get; set; }
        public string? MatchAccountLabel { get; set; }
        public string? ProviderAccountId { get; set; }
        public string? AccountLabel { get; set; }
        public string? PersonLabel { get; set; }
        public bool Disabled { get; set; }
        public bool Json { get; set; }

        public static AccountBindOptions Parse(string[] args) {
            var options = new AccountBindOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--source-root":
                        options.SourceRootId = ReadValue(args, ref i);
                        break;
                    case "--match-provider-account-id":
                        options.MatchProviderAccountId = ReadValue(args, ref i);
                        break;
                    case "--match-account-label":
                        options.MatchAccountLabel = ReadValue(args, ref i);
                        break;
                    case "--provider-account-id":
                        options.ProviderAccountId = ReadValue(args, ref i);
                        break;
                    case "--account-label":
                        options.AccountLabel = ReadValue(args, ref i);
                        break;
                    case "--person-label":
                        options.PersonLabel = ReadValue(args, ref i);
                        break;
                    case "--disabled":
                        options.Disabled = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            if (string.IsNullOrWhiteSpace(options.ProviderId)) {
                throw new InvalidOperationException("Missing provider id. Use --provider <id>.");
            }
            if (string.IsNullOrWhiteSpace(options.SourceRootId) &&
                string.IsNullOrWhiteSpace(options.MatchProviderAccountId) &&
                string.IsNullOrWhiteSpace(options.MatchAccountLabel)) {
                throw new InvalidOperationException("Provide at least one matcher: --source-root, --match-provider-account-id, or --match-account-label.");
            }
            if (string.IsNullOrWhiteSpace(options.ProviderAccountId) &&
                string.IsNullOrWhiteSpace(options.AccountLabel) &&
                string.IsNullOrWhiteSpace(options.PersonLabel)) {
                throw new InvalidOperationException("Provide at least one canonical value: --provider-account-id, --account-label, or --person-label.");
            }

            return options;
        }
    }

    private sealed class ImportOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? MachineId { get; set; }
        public string? ParserVersion { get; set; }
        public bool RecentFirst { get; set; }
        public int? MaxArtifacts { get; set; }
        public bool Discover { get; set; }
        public bool Force { get; set; }
        public bool Json { get; set; }

        public static ImportOptions Parse(string[] args) {
            var options = new ImportOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--machine":
                        options.MachineId = ReadValue(args, ref i);
                        break;
                    case "--parser-version":
                        options.ParserVersion = ReadValue(args, ref i);
                        break;
                    case "--recent-first":
                        options.RecentFirst = true;
                        break;
                    case "--max-artifacts":
                        options.MaxArtifacts = ParsePositiveInt32(ReadValue(args, ref i), "--max-artifacts");
                        break;
                    case "--discover":
                        options.Discover = true;
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private sealed class ReportOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? AccountFilter { get; set; }
        public string? PersonFilter { get; set; }
        public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;
        public string? Title { get; set; }
        public string? OutputPath { get; set; }
        public string? OutputDirectory { get; set; }
        public bool RecentFirst { get; set; } = true;
        public int? MaxArtifacts { get; set; }
        public bool Force { get; set; }
        public bool FullImport { get; set; }
        public bool Json { get; set; }
        public List<string> Paths { get; } = new();
        public List<string> GitHubUsers { get; } = new();
        public List<string> GitHubOwners { get; } = new();

        public static ReportOptions Parse(string[] args) {
            var options = new ReportOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--path":
                        options.Paths.Add(ReadValue(args, ref i));
                        break;
                    case "--account":
                        options.AccountFilter = ReadValue(args, ref i);
                        break;
                    case "--person":
                        options.PersonFilter = ReadValue(args, ref i);
                        break;
                    case "--github-user":
                        options.GitHubUsers.Add(ReadValue(args, ref i));
                        break;
                    case "--github-owner":
                        options.GitHubOwners.Add(ReadValue(args, ref i));
                        break;
                    case "--metric":
                        options.Metric = ParseMetric(ReadValue(args, ref i));
                        break;
                    case "--title":
                        options.Title = ReadValue(args, ref i);
                        break;
                    case "--recent-first":
                        options.RecentFirst = true;
                        break;
                    case "--max-artifacts":
                        options.MaxArtifacts = ParsePositiveInt32(ReadValue(args, ref i), "--max-artifacts");
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--full-import":
                        options.FullImport = true;
                        break;
                    case "--out":
                        options.OutputPath = ReadValue(args, ref i);
                        break;
                    case "--out-dir":
                        options.OutputDirectory = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            return options;
        }
    }

    private static int ParsePositiveInt32(string value, string optionName) {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) {
            throw new InvalidOperationException($"{optionName} must be a positive integer.");
        }

        return parsed;
    }

    private sealed class StatsOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? AccountFilter { get; set; }
        public bool Json { get; set; }

        public static StatsOptions Parse(string[] args) {
            var options = new StatsOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = ReadValue(args, ref i);
                        break;
                    case "--provider":
                        options.ProviderId = ReadValue(args, ref i);
                        break;
                    case "--account":
                        options.AccountFilter = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }
            return options;
        }
    }

    private sealed class OverviewOptions {
        public string? DatabasePath { get; set; }
        public string? ProviderId { get; set; }
        public string? AccountFilter { get; set; }
        public string? PersonFilter { get; set; }
        public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;
        public string? Title { get; set; }
        public string? OutputPath { get; set; }
        public string? OutputDirectory { get; set; }
        public bool Discover { get; set; }
        public bool RecentFirst { get; set; }
        public int? MaxArtifacts { get; set; }
        public bool Force { get; set; }
        public bool SkipAutoImport { get; set; }
        public bool Json { get; set; }
        public List<string> GitHubUsers { get; } = new();
        public List<string> GitHubOwners { get; } = new();

        public static OverviewOptions Parse(string[] args) {
            var options = new OverviewOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
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
                    case "--github-user":
                        options.GitHubUsers.Add(ReadValue(args, ref i));
                        break;
                    case "--github-owner":
                        options.GitHubOwners.Add(ReadValue(args, ref i));
                        break;
                    case "--metric":
                        options.Metric = ParseMetric(ReadValue(args, ref i));
                        break;
                    case "--title":
                        options.Title = ReadValue(args, ref i);
                        break;
                    case "--discover":
                        options.Discover = true;
                        break;
                    case "--recent-first":
                        options.RecentFirst = true;
                        break;
                    case "--max-artifacts":
                        options.MaxArtifacts = ParsePositiveInt32(ReadValue(args, ref i), "--max-artifacts");
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--no-auto-import":
                        options.SkipAutoImport = true;
                        break;
                    case "--out":
                        options.OutputPath = ReadValue(args, ref i);
                        break;
                    case "--out-dir":
                        options.OutputDirectory = ReadValue(args, ref i);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown option or unexpected argument: {args[i]}");
                }
            }

            return options;
        }
    }

    private sealed record ProviderStats(
        string ProviderId,
        int RootCount,
        int EventCount,
        int AccountCount,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        long TotalTokens);
}
