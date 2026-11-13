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
        if (args.Length == 4 && args[0] == "hook" && args[1] == "--config")
            return await LocalReporter.RunAsync(args[2], hook: true, suppliedPayload: args[3]);
        if (args.Length == 4 && args[0] == "notify" && args[1] == "--config")
            return await LocalReporter.RunNotifyChainAsync(args[2], args[3]);
        if (args.Length != 3 || args[1] != "--config" || args[0] is not ("serve" or "report" or "hook")) {
            Console.Error.WriteLine("Usage: IntelligenceX.Ledger serve|report|hook --config <local-json-file>\n" +
                "       IntelligenceX.Ledger backup --config <local-json-file> --out <new-backup-path>\n" +
                "       IntelligenceX.Ledger restore --config <local-json-file> --from <backup-path>");
            return 2;
        }
        if (args[0] is "report" or "hook") return await LocalReporter.RunAsync(args[2], args[0] == "hook");
        var settings = LedgerSettings.Load(args[2]);
        Directory.CreateDirectory(settings.DataDirectory);
        using var writerLease = LedgerBackup.AcquireServiceLease(settings.DataDirectory);
        var coordination = settings.CoordinationConfigPath is null ? null : CoordinationEndpoints.Load(settings.CoordinationConfigPath, settings);
        using var coordinationStore = coordination is null ? null : new CoordinationStore(
            Path.Combine(settings.DataDirectory, "coordination.sqlite"), coordination.InitialPolicy, coordination.Principals);
        if (coordinationStore is not null) await coordinationStore.InitializeAsync();
        using var store = new LedgerStore(Path.Combine(settings.DataDirectory, "ledger.sqlite"), new(settings.Rules));
        await store.InitializeAsync();
        var builder = WebApplication.CreateSlimBuilder();
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
        LeaseEndpoints.Map(app, store, settings);
        if (coordinationStore is not null) CoordinationEndpoints.Map(app, coordinationStore, settings.DataDirectory);
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
        await app.RunAsync();
        return 0;
    }
}
