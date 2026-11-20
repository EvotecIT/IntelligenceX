using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger;

public static class Program {
    /// <summary>The source commit this build came from, from the informational version the SDK stamps, or null.</summary>
    internal static readonly string? BuildCommit = typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion is { } version &&
        version.IndexOf('+') is var plus and >= 0 ? version[(plus + 1)..] : null;

    public static async Task<int> Main(string[] args) {
        if (args.Length == 5 && args[0] == "backup" && args[1] == "--config" && args[3] == "--out") {
            var configuration = LedgerSettings.Load(args[2]);
            var coordinated = configuration.CoordinationConfigPath is not null ||
                File.Exists(Path.Combine(configuration.DataDirectory, "coordination.sqlite"));
            var manifest = coordinated
                ? await LedgerBackup.CreateStoppedAsync(configuration.DataDirectory, args[4])
                : await LedgerBackup.CreateAsync(Path.Combine(configuration.DataDirectory, "ledger.sqlite"), args[4]);
            Console.WriteLine(JsonSerializer.Serialize(manifest, LedgerJson.Options));
            return 0;
        }
        if (args.Length == 5 && args[0] == "restore" && args[1] == "--config" && args[3] == "--from") {
            var configuration = LedgerSettings.Load(args[2]);
            if (configuration.CoordinationConfigPath is not null && !Directory.Exists(args[4]))
                throw new InvalidDataException("Coordination enrollment requires a complete stopped backup directory.");
            var manifest = await LedgerBackup.RestoreAsync(args[4], configuration.DataDirectory);
            Console.WriteLine(JsonSerializer.Serialize(manifest, LedgerJson.Options));
            return 0;
        }
        if (args.Length == 3 && args[0] == "compact" && args[1] == "--config") return await CompactAsync(args[2]);
        if (args.Length == 4 && args[0] == "hook" && args[1] == "--config")
            return await LocalReporter.RunAsync(args[2], hook: true, suppliedPayload: args[3]);
        if (args.Length == 3 && args[0] == "agent" && args[1] == "--config") {
            // A sign-in task like the service and reporter; helper sessions it starts inherit its priority.
            ProcessPriority.EnsureNormal();
            return await Agents.AgentHost.RunProcessAsync(args[2]);
        }
        if (args.Length == 3 && args[0] == "agent-check" && args[1] == "--config") return await Agents.AgentHost.CheckAsync(Agents.AgentHost.Load(args[2]));
        if (args.Length > 0 && args[0] == "agent-install") return await Agents.AgentInstaller.RunAsync(args);
        if (args.Length > 0 && args[0] == "agent-enroll") return Agents.AgentEnrollment.Run(args);
        if (args.Length == 3 && args[0] == "inbox" && args[1] == "--config")
            return await LedgerInbox.RunAsync(args[2], Console.In, Console.Out);
        if (args.Length == 4 && args[0] == "notify" && args[1] == "--config")
            return await LocalReporter.RunNotifyChainAsync(args[2], args[3]);
        if (LeaseApprovalCommand.Matches(args)) return await LeaseApprovalCommand.RunAsync(args);
        if (args.Length != 3 || args[1] != "--config" || args[0] is not ("serve" or "report" or "hook")) {
            Console.Error.WriteLine("Usage: IntelligenceX.Ledger serve|report|hook --config <local-json-file>\n" +
                "       IntelligenceX.Ledger agent|agent-check|inbox --config <agent-json-file>\n" +
                "       IntelligenceX.Ledger agent-install --reporter <reporter-json-file> --version <installed-version> [--apply]\n" +
                "       IntelligenceX.Ledger agent-enroll --service <service-json-file> --machine <id> --out <folder> [--local] [--apply]\n" +
                "       IntelligenceX.Ledger backup --config <local-json-file> --out <new-backup-path>\n" +
                "       IntelligenceX.Ledger restore --config <local-json-file> --from <backup-path>\n" +
                "       IntelligenceX.Ledger compact --config <local-json-file>\n" + LeaseApprovalCommand.Usage);
            return 2;
        }
        if (args[0] == "report") ReportPriority(ProcessPriority.EnsureNormal());
        if (args[0] is "report" or "hook") return await LocalReporter.RunAsync(args[2], args[0] == "hook");
        var priority = ProcessPriority.EnsureNormal();
        ReportPriority(priority);
        var settings = LedgerSettings.Load(args[2]);
        Directory.CreateDirectory(settings.DataDirectory);
        using var writerLease = LedgerBackup.AcquireServiceLease(settings.DataDirectory);
        var startup = new StartupProgress(settings.DataDirectory);
        startup.Report(new("opening-coordination"));
        var coordination = settings.CoordinationConfigPath is null ? null : CoordinationEndpoints.Load(settings.CoordinationConfigPath, settings);
        using var coordinationStore = coordination is null ? null : new CoordinationStore(
            Path.Combine(settings.DataDirectory, "coordination.sqlite"), coordination.InitialPolicy, coordination.Principals);
        if (coordinationStore is not null) await coordinationStore.InitializeAsync();
        // Session, machine, budget and repository reads need only the committed policy, not the command writer.
        async Task<CoordinationPolicy> ReadPolicy(CancellationToken ct) => coordinationStore is null
            ? new(1, []) : await coordinationStore.ReadAsync(state => state.Policy, ct);
        using var store = new LedgerStore(Path.Combine(settings.DataDirectory, "ledger.sqlite"), new(settings.Rules), ReadPolicy);
        await store.InitializeAsync(progress: startup);
        startup.Report(new("starting-listener"));
        var builder = CreateWebBuilder();
        builder.Logging.ClearProviders(); // Payloads, paths and credentials never enter request logs.
        // Windows Schannel requires a key container for a configured server PFX. The default
        // temporary container is owned by this certificate and removed on disposal; no store is opened.
        using var serverCertificate = settings.CertificatePath is null ? null : X509CertificateLoader.LoadPkcs12FromFile(
            settings.CertificatePath, settings.CertificatePassword,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        builder.WebHost.ConfigureKestrel(server => {
            server.Limits.MaxRequestBodySize = LedgerJson.MaximumBatchBytes;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Listen(IPAddress.Loopback, settings.Port);
            if (settings.LanAddress is not null) server.Listen(IPAddress.Parse(settings.LanAddress), settings.Port, listen =>
                listen.UseHttps(serverCertificate!));
        });
        builder.Services.ConfigureHttpJsonOptions(options => {
            options.SerializerOptions.UnmappedMemberHandling = LedgerJson.Options.UnmappedMemberHandling;
            options.SerializerOptions.MaxDepth = LedgerJson.Options.MaxDepth;
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(settings);
        builder.Services.AddHostedService<ShadowReportWorker>();
        if (coordinationStore is not null) builder.Services.AddHostedService(_ => new SessionLineageBackfill(coordinationStore, store));
        // The service's own GitHub reads are counted here and saved each minute for the github-budget view.
        var githubUsage = new GitHubUsageMeter();
        builder.Services.AddHostedService(_ => new GitHubUsageRecorder(githubUsage, store));
        var catalog = settings.WorkCatalog is null ? null : new Catalog.WorkCatalogService(settings.WorkCatalog, store, settings, usage: githubUsage);
        if (catalog is not null) builder.Services.AddHostedService(_ => catalog);
        var readiness = new PullRequestReadinessService(store, githubUsage);
        if (coordinationStore is not null) builder.Services.AddHostedService(_ => new WakeWatcher(coordinationStore, readiness));
        var app = builder.Build();
        var runtime = new LedgerRuntime(store, settings);
        var contextService = new ProjectContextService(settings.ProjectCatalogPath);
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store";
            var header = context.Request.Headers.Authorization.ToString();
            var bearer = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
            if (CoordinationEndpoints.OwnsRoute(context.Request.Path)) {
                if (coordination is null || !await CoordinationEndpoints.AuthenticateAsync(context, bearer, coordination, coordinationStore!)) {
                    context.Response.StatusCode = 401; return;
                }
            } else {
                var expected = HttpMethods.IsPost(context.Request.Method) ? settings.WriteToken : settings.ReadToken;
                if (!LedgerRuntime.TokenEquals(bearer, expected)) { context.Response.StatusCode = 401; return; }
            }
            try { await next(context); }
            catch (CoordinationConflictException error) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
            catch (EventConflictException) { context.Response.StatusCode = 409; }
            catch (JsonException) { context.Response.StatusCode = 400; }
            catch (ArgumentException) { context.Response.StatusCode = 400; }
        });
        const string prefix = "/api/ledger/v1";
        ProjectContextEndpoints.Map(app, contextService, runtime);
        app.MapGet(prefix + "/pr-ready", async (string repo, int pr, CancellationToken ct) =>
            Results.Ok(await readiness.ReadAsync(repo, pr, ct)));
        app.MapGet(prefix + "/cleanup", async (string? machine, CancellationToken ct) =>
            Results.Ok(await store.CleanupPlanAsync(DateTimeOffset.UtcNow, machine, ct)));
        app.MapGet(prefix + "/session-status", async (string id, CancellationToken ct) => {
            var machines = await runtime.MachinesAsync(DateTimeOffset.UtcNow, ct);
            return Results.Ok(await store.SessionStatusAsync(id, DateTimeOffset.UtcNow, ct,
                machines.Where(machine => machine.State != "healthy").Select(machine => machine.Id).ToHashSet(StringComparer.Ordinal)));
        });
        async Task<HashSet<string>> SilentMachines(DateTimeOffset now, CancellationToken ct) =>
            (await runtime.MachinesAsync(now, ct)).Where(machine => machine.State != "healthy").Select(machine => machine.Id).ToHashSet(StringComparer.Ordinal);
        app.MapGet(prefix + "/session-tree", async (string id, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            var (links, _) = await SessionLineageSource.ReadAsync(coordinationStore, now, ct);
            return Results.Ok(await store.SessionTreeAsync(id, now, links, await SilentMachines(now, ct), ct));
        });
        app.MapGet(prefix + "/session-trail", async (string id, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            var (links, asks) = await SessionLineageSource.ReadAsync(coordinationStore, now, ct);
            return Results.Ok(await store.SessionTrailAsync(id, now, links, asks, ct));
        });
        app.MapPost(prefix + "/session-takeover", async (SessionTakeoverCommand command, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            var (links, asks) = await SessionLineageSource.ReadAsync(coordinationStore, now, ct);
            return Results.Ok(await store.TakeOverSessionAsync(command, now, links, asks, ct));
        });
        LeaseEndpoints.Map(app, store, settings);
        Catalog.WorkCatalogEndpoints.Map(app, catalog, runtime);
        if (coordinationStore is not null) {
            CoordinationEndpoints.Map(app, coordinationStore, settings.DataDirectory, (link, ct) => store.RecordLinksAsync([link], ct));
        }
        app.MapGet(prefix + "/policy", async (CancellationToken ct) => Results.Ok(await ReadPolicy(ct)));
        app.MapPost(prefix + "/events", async (LedgerEvent[] events, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            if (events.Length is 0 or > LedgerJson.MaximumBatchEvents || events.Any(e => !EventAdmission.Valid(e, settings, now)))
                return Results.BadRequest(new { error = "invalid-events" });
            var ids = await store.IngestAsync(events, now, ct);
            runtime.InvalidateWork();
            return Results.Ok(new { acknowledgedEventIds = ids, receivedAtUtc = now });
        });
        app.MapGet(prefix + "/status", async (CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            var items = await runtime.ItemsAsync(now, ct);
            return Results.Ok(new { schemaVersion = 1, rulesVersion = LedgerRules.Version, generatedAtUtc = now,
                machines = await runtime.MachinesAsync(now, ct), coverage = await store.CoverageAsync(ct),
                counts = items.GroupBy(i => i.Current!.Value).ToDictionary(g => g.Key, g => g.Count()),
                pausedCount = items.Count(i => i.Current!.Hold is { Type: "paused-by-user" }) });
        });
        app.MapGet(prefix + "/machines", async (CancellationToken ct) => Results.Ok(await runtime.MachinesAsync(DateTimeOffset.UtcNow, ct)));
        var startedAtUtc = DateTimeOffset.UtcNow;
        app.MapGet(prefix + "/health", async (CancellationToken ct) => {
            // The supervisor restarts the service after several failed probes, so the budget stays well under its timeout.
            var probe = await store.ProbeAsync(HealthBudget, ct);
            var body = new { status = probe.Healthy ? "ok" : "stalled", reason = probe.Reason, probe.GateWaitMs, probe.QueryMs,
                startedAtUtc, priority, commit = BuildCommit };
            return probe.Healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        app.MapGet(prefix + "/items", async (string? cursor, int? limit, string? verdict, string? machineId, string? provider, CancellationToken ct) => {
            var page = await runtime.PageAsync(cursor, Math.Clamp(limit ?? 100, 1, 500), verdict, machineId, provider, ct);
            return page is null ? Results.BadRequest(new { error = "expired-or-invalid-cursor" }) : Results.Ok(new {
                page.SnapshotId, page.AsOfUtc, items = page.Items.Select(LedgerItemView.From), page.NextCursor });
        });
        app.MapGet(prefix + "/items/{id}", async (string id, CancellationToken ct) => {
            var item = (await runtime.ItemsAsync(DateTimeOffset.UtcNow, ct, includeMessageEvidence: true)).FirstOrDefault(i => i.Id == id);
            return item is null ? Results.NotFound() : Results.Ok(LedgerItemView.From(item));
        });
        app.MapGet(prefix + "/items/{id}/events", async (string id, long? cursor, int? limit, CancellationToken ct) =>
            Results.Ok(await store.HistoryAsync(id, cursor ?? 0, Math.Clamp(limit ?? 100, 1, 500), ct)));
        app.MapGet(prefix + "/reports/daily", async (string date, CancellationToken ct) =>
            DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)
                ? Results.Ok(await store.ReportAsync(day, DateTimeOffset.UtcNow, ct))
                : Results.BadRequest(new { error = "invalid-date" }));
        app.Lifetime.ApplicationStarted.Register(startup.Ready);
        await app.RunAsync();
        return 0;
    }

    /// <summary>Time the health probe allows the write gate and one query before it reports the service as stalled.</summary>
    internal static readonly TimeSpan HealthBudget = TimeSpan.FromSeconds(5);

    private static void ReportPriority(ProcessPriority.State state) =>
        Console.Out.WriteLine($"{DateTimeOffset.UtcNow:O} priority cpu={state.Cpu} disk={state.Disk?.ToString() ?? "n/a"} memory={state.Memory?.ToString() ?? "n/a"}");

    /// <summary>
    /// Compacts a stopped service's database. Only the data directory is read from the configuration, so the
    /// command works offline even when the service's LAN interface isn't present.
    /// </summary>
    private static async Task<int> CompactAsync(string configPath) {
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        // Matches LedgerSettings.Load, which reads property names case-insensitively.
        var dataDirectory = config.RootElement.ValueKind == JsonValueKind.Object ? config.RootElement.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals("dataDirectory", StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.String } value
            ? value.GetString() : null : null;
        if (dataDirectory is null || !Path.IsPathFullyQualified(dataDirectory)) { Console.Error.WriteLine("The configuration has no absolute dataDirectory."); return 2; }
        var database = Path.Combine(dataDirectory, "ledger.sqlite");
        if (!File.Exists(database)) { Console.Error.WriteLine("No ledger.sqlite in " + dataDirectory + "."); return 2; }
        // VACUUM writes a full copy and a journal; free pages inside the file don't help.
        var size = new FileInfo(database).Length;
        var free = VolumeOf(Path.GetFullPath(database)).AvailableFreeSpace;
        if (free < size * 2) {
            Console.Error.WriteLine($"Compacting needs up to {size * 2 / 1048576} MB free on the database volume; {free / 1048576} MB is free. Free some space and retry.");
            return 1;
        }
        FileStream serviceLease;
        try { serviceLease = LedgerBackup.AcquireServiceLease(dataDirectory); }
        catch (IOException) { Console.Error.WriteLine("Stop the Ledger service before compacting its database."); return 1; }
        using (serviceLease) {
            var (before, after, checkpointed) = await LedgerStore.CompactAsync(database);
            Console.WriteLine(JsonSerializer.Serialize(new { beforeBytes = before, afterBytes = after, checkpointed }, LedgerJson.Options));
            if (!checkpointed) Console.Error.WriteLine("The database was rewritten, but another reader kept the old copy open; it shrinks once that reader closes.");
            return checkpointed ? 0 : 3;
        }
    }

    /// <summary>The mounted volume that holds a path: the ready drive with the longest root containing it.</summary>
    internal static DriveInfo VolumeOf(string fullPath) {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && Contains(drive.RootDirectory.FullName))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .FirstOrDefault() ?? new DriveInfo(Path.GetPathRoot(fullPath)!);

        // "/mnt/d" must not claim "/mnt/data": a root matches only up to a directory boundary.
        bool Contains(string root) => fullPath.StartsWith(root, comparison) &&
            (root.EndsWith(Path.DirectorySeparatorChar) || fullPath.Length == root.Length || fullPath[root.Length] == Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// The scheduled task starts the service in the runtime root, where the reporter spool changes every few
    /// seconds. Anchor the content root to the application and keep appsettings files static, so the host does
    /// not watch that tree.
    /// </summary>
    internal static WebApplicationBuilder CreateWebBuilder() => WebApplication.CreateSlimBuilder(new WebApplicationOptions {
        ContentRootPath = AppContext.BaseDirectory, Args = ["--hostBuilder:reloadConfigOnChange=false"]
    });
}
