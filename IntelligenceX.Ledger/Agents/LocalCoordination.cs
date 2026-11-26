using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;

namespace IntelligenceX.Ledger.Agents;

/// <summary>A coordination store for this machine alone, which the agent serves on loopback so that sessions here keep
/// asking each other for work while the Ledger can't be reached. It runs the same engine and endpoints as the Ledger, with
/// the last policy the Ledger gave this host, narrowed to this machine. A second agent instance works its feeds with the
/// usual helper and delivery code. Requests it finishes are imported into the Ledger under their own ids once it answers.
/// Its two principals (this host and this machine's sessions) exist only here and grant nothing on the Ledger.</summary>
internal sealed class LocalCoordination : IAsyncDisposable {
    internal const string ClientFileName = "local-client.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CoordinationPrincipal Operator = new("local-operator", "operator", "", []);

    private readonly WebApplication _app;
    private readonly CoordinationStore _store;
    private readonly CoordinationArtifactStore _artifacts;
    private readonly CoordinationImageStore _images;
    private readonly string _folder;

    private readonly string _clientFile;
    private readonly string[] _projects;
    private int _disposed;

    private LocalCoordination(WebApplication app, CoordinationStore store, string folder, string clientFile, string[] projects,
        string endpoint, string hostToken) {
        _app = app;
        _projects = projects;
        _clientFile = clientFile;
        _store = store;
        _folder = folder;
        _artifacts = new CoordinationArtifactStore(Path.Combine(folder, "coordination-results"));
        _images = new CoordinationImageStore(Path.Combine(folder, "coordination-images"));
        Endpoint = endpoint;
        HostToken = hostToken;
    }

    internal string Endpoint { get; }
    internal string HostToken { get; }

    internal static string Folder(AgentSettings settings) => Path.Combine(settings.StateDirectory, "local-coordination");
    private static string PolicyFile(AgentSettings settings) => Path.Combine(Folder(settings), "policy.json");

    /// <summary>The policy and projects the Ledger last gave this host.</summary>
    internal sealed record Snapshot(CoordinationPolicy Policy, string[] Projects);

    internal static Snapshot? Cached(AgentSettings settings) {
        try { return File.Exists(PolicyFile(settings)) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(PolicyFile(settings)), Json) : null; }
        catch (JsonException) { return null; }
    }

    internal static void Cache(AgentSettings settings, CoordinationPolicy policy, string[] projects) {
        Directory.CreateDirectory(Folder(settings));
        LocalFiles.WriteAtomic(PolicyFile(settings), JsonSerializer.Serialize(new Snapshot(policy, projects), Json));
    }

    /// <summary>The Ledger's policy for this machine only: nothing is admitted elsewhere, and a finished request leaves the
    /// live state after <see cref="AgentSettings.LocalFinishedRetention"/> (or the Ledger's own retention, when shorter), so
    /// it can be imported.</summary>
    internal static CoordinationPolicy LocalPolicy(CoordinationPolicy policy, AgentSettings settings, long revision) {
        var local = policy with { Revision = revision, Machines = policy.Machines.Where(m => m.Id == settings.MachineId).ToArray() };
        local.RetainFinishedDays = Math.Min(policy.RetainFinishedDays, settings.LocalFinishedRetention.TotalDays);
        return local;
    }

    /// <summary>Opens the store and serves it on loopback. Null while no policy for this machine has been cached yet.</summary>
    internal static async Task<LocalCoordination?> StartAsync(AgentSettings settings, CancellationToken token) {
        if (settings.LocalCoordinationPort <= 0 || Cached(settings) is not { } snapshot ||
            !snapshot.Policy.Machines.Any(m => m.Id == settings.MachineId)) return null;
        // A store left from an earlier run that the Ledger no longer allows stays closed; its file keeps its ids local.
        var folder = Folder(settings);
        Directory.CreateDirectory(folder);
        var tokens = Tokens(Path.Combine(folder, "principals.json"));
        var principals = new[] {
            new CoordinationPrincipal(settings.MachineId + "-host", "host", tokens.Host, snapshot.Projects, settings.MachineId),
            new CoordinationPrincipal(settings.MachineId + "-sessions", "client", tokens.Sessions, snapshot.Projects, settings.MachineId)
        };
        var store = new CoordinationStore(Path.Combine(folder, "coordination.sqlite"), LocalPolicy(snapshot.Policy, settings, 1), principals);
        WebApplication? app = null;
        try {
            return await ServeAsync(settings, snapshot, folder, tokens, principals, store, started => app = started, token).ConfigureAwait(false);
        } catch {
            // Nothing owns the store or the listener until it is returned: release both, so a later start can take them.
            if (app is not null) await app.DisposeAsync().ConfigureAwait(false);
            store.Dispose();
            throw;
        }
    }

    private static async Task<LocalCoordination> ServeAsync(AgentSettings settings, Snapshot snapshot, string folder, LocalTokens tokens,
        CoordinationPrincipal[] principals, CoordinationStore store, Action<WebApplication> built, CancellationToken token) {
        await store.InitializeAsync(token).ConfigureAwait(false);
        await ApplyPolicyAsync(store, snapshot.Policy, settings, token).ConfigureAwait(false);
        var configuration = new CoordinationConfiguration { Principals = principals, InitialPolicy = LocalPolicy(snapshot.Policy, settings, 1) };
        var builder = Program.CreateWebBuilder();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options => {
            options.SerializerOptions.UnmappedMemberHandling = LedgerJson.Options.UnmappedMemberHandling;
            options.SerializerOptions.MaxDepth = LedgerJson.Options.MaxDepth;
        });
        builder.WebHost.ConfigureKestrel(server => {
            server.Limits.MaxRequestBodySize = 1024 * 1024;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Listen(IPAddress.Loopback, settings.LocalCoordinationPort);
        });
        var app = builder.Build();
        built(app);
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store";
            var header = context.Request.Headers.Authorization.ToString();
            var bearer = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
            if (!CoordinationEndpoints.OwnsRoute(context.Request.Path) ||
                !await CoordinationEndpoints.AuthenticateAsync(context, bearer, configuration, store).ConfigureAwait(false)) {
                context.Response.StatusCode = 401;
                return;
            }
            try { await next(context).ConfigureAwait(false); }
            catch (CoordinationConflictException error) {
                context.Response.StatusCode = 409;
                await context.Response.WriteAsJsonAsync(new { error = error.Message }).ConfigureAwait(false);
            } catch (Exception error) when (error is JsonException or ArgumentException or BadHttpRequestException) {
                context.Response.StatusCode = 400;
            }
        });
        CoordinationEndpoints.Map(app, store, folder);
        await app.StartAsync(token).ConfigureAwait(false);
        var endpoint = "http://127.0.0.1:" + settings.LocalCoordinationPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
        // The sessions' client file sits beside the Ledger's, where the ask tools look for it.
        var clientFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.CoordinationConfig))!, ClientFileName);
        LocalFiles.WriteAtomic(clientFile, JsonSerializer.Serialize(new CoordinationClientSettings(endpoint, tokens.Sessions) {
            MachineId = settings.MachineId
        }, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(clientFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new LocalCoordination(app, store, folder, clientFile, snapshot.Projects, endpoint, tokens.Host);
    }

    /// <summary>True when the store must restart to match the Ledger: the host has other projects than the store's
    /// principals were created with (those scopes are fixed for the life of the store), or the policy no longer has this
    /// machine, in which case the store doesn't start again.</summary>
    internal bool Stale(AgentSettings settings) =>
        Cached(settings) is { } snapshot && (!snapshot.Policy.Machines.Any(m => m.Id == settings.MachineId) ||
            !snapshot.Projects.Order(StringComparer.Ordinal).SequenceEqual(_projects.Order(StringComparer.Ordinal), StringComparer.Ordinal));

    /// <summary>Brings the local policy up to the cached one when it changed.</summary>
    internal Task RefreshPolicyAsync(AgentSettings settings, CancellationToken token) =>
        Cached(settings) is { } snapshot ? ApplyPolicyAsync(_store, snapshot.Policy, settings, token) : Task.CompletedTask;

    private static Task ApplyPolicyAsync(CoordinationStore store, CoordinationPolicy cached, AgentSettings settings, CancellationToken token) =>
        store.ExecuteAsync((engine, state) => {
            var wanted = LocalPolicy(cached, settings, state.Policy.Revision);
            // A policy without this machine can't be applied (a policy needs a machine); the agent stops the store instead.
            if (JsonSerializer.Serialize(wanted, Json) == JsonSerializer.Serialize(state.Policy, Json) ||
                !wanted.Machines.Any(m => m.Id == settings.MachineId)) return false;
            engine.ReplacePolicy(LocalPolicy(cached, settings, state.Policy.Revision + 1), Operator, state.Policy.Revision);
            return true;
        }, DateTimeOffset.UtcNow, token);

    private sealed record LocalTokens(string Host, string Sessions);

    private static LocalTokens Tokens(string file) {
        if (File.Exists(file) && JsonSerializer.Deserialize<LocalTokens>(File.ReadAllText(file), Json) is { Host.Length: >= 32, Sessions.Length: >= 32 } stored)
            return stored;
        var created = new LocalTokens(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        LocalFiles.WriteAtomic(file, JsonSerializer.Serialize(created, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return created;
    }

    private string ExportedFile => Path.Combine(_folder, "exported.json");

    private Dictionary<string, string> Exported() => File.Exists(ExportedFile)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ExportedFile), Json) ?? new(StringComparer.Ordinal)
        : new(StringComparer.Ordinal);

    /// <summary>Outcomes after which the Ledger holds the id itself, so its record answers for it.</summary>
    private static bool Transferred(string outcome) => outcome is "imported" or "import-conflict";

    /// <summary>True while the store holds a request the Ledger doesn't: live work, or archived and not imported.</summary>
    private async Task<bool> HoldsUnsentAsync() {
        if (await _store.ReadAsync(state => state.Work.Count).ConfigureAwait(false) > 0) return true;
        var done = Exported();
        return (await ArchiveAsync(CancellationToken.None).ConfigureAwait(false))
            .Any(entry => !done.TryGetValue(entry.RequestId, out var outcome) || !Transferred(outcome));
    }

    /// <summary>The whole local archive, oldest first. The store reads at most 10,000 entries at a time.</summary>
    private async Task<List<CoordinationArchiveEntry>> ArchiveAsync(CancellationToken token) {
        var all = new List<CoordinationArchiveEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var since = DateTimeOffset.MinValue;
        while (true) {
            var page = await _store.ArchivedSinceAsync(since, token).ConfigureAwait(false);
            all.AddRange(page.Where(entry => seen.Add(entry.RequestId)));
            if (page.Length < CoordinationStore.ArchivePageSize || page[^1].FinishedAtUtc == since) return all;
            since = page[^1].FinishedAtUtc;
        }
    }

    /// <summary>Imports every finished local request into the Ledger, oldest first, with the images it names and its title. An
    /// imported, conflicting or oversized id is remembered so it is sent once; one the Ledger didn't take stays in the local
    /// archive for someone to look at, and keeps the local route. Stops at the first
    /// transport failure, since the Ledger is still unreachable.</summary>
    internal async Task<int> ExportAsync(LedgerReadClient ledger, CancellationToken token) {
        var done = Exported();
        var imported = 0;
        foreach (var entry in await ArchiveAsync(token).ConfigureAwait(false)) {
            if (done.ContainsKey(entry.RequestId)) continue;
            var content = entry.Work.ResultRef is { } resultRef ? _artifacts.ReadResult(resultRef)?.Content : null;
            // The images the ask and its result name go first, under the same content ids, so the Ledger's copy reads whole.
            // Until they are there, the request isn't recorded and the next pass tries it again.
            if (!await UploadImagesAsync(ledger, entry, content, token).ConfigureAwait(false)) continue;
            string outcome;
            try {
                await ledger.ImportAsync(new CoordinationImport(JsonSerializer.SerializeToElement(entry, Json), content) {
                    Title = await _store.AskTitleAsync(entry.RequestId, token).ConfigureAwait(false)
                }, token).ConfigureAwait(false);
                outcome = "imported";
                imported++;
            } catch (CoordinationCommandRejectedException error) {
                outcome = error.ErrorCode;
                AgentService.Log("local-import-rejected", entry.RequestId + " " + error.ErrorCode);
            } catch (InvalidDataException) {
                // Larger than the Ledger takes: it stays in the local archive, and the entries after it still go.
                outcome = "too-large";
                AgentService.Log("local-import-rejected", entry.RequestId + " too-large");
            }
            done[entry.RequestId] = outcome;
            LocalFiles.WriteAtomic(ExportedFile, JsonSerializer.Serialize(done, Json));
        }
        return imported;
    }

    /// <summary>Uploads the images a request names. False while the Ledger's image store is full, which clears as old
    /// images expire. An image the Ledger never takes (an unsupported or oversized file) is logged and left out, so the
    /// request itself is still imported, with that reference unresolved.</summary>
    private async Task<bool> UploadImagesAsync(LedgerReadClient ledger, CoordinationArchiveEntry entry, string? content, CancellationToken token) {
        foreach (var id in (entry.Work.Request.Helper?.Images ?? []).Concat(CoordinationImage.References(content)).Distinct(StringComparer.Ordinal)) {
            if (_images.Find(id) is not { } image) continue;
            try { await ledger.UploadImageAsync(await File.ReadAllBytesAsync(image.Path, token).ConfigureAwait(false), token).ConfigureAwait(false); }
            catch (CoordinationCommandRejectedException error) when (error.ErrorCode == "image-store-capacity") {
                AgentService.Log("local-import-deferred", entry.RequestId + " image-store-capacity");
                return false;
            } catch (Exception error) when (error is CoordinationCommandRejectedException or InvalidDataException) {
                AgentService.Log("local-image-skipped", entry.RequestId + " " + id);
            }
        }
        return true;
    }

    /// <summary>Stops serving. The sessions' client file is removed only when the Ledger has every request this store
    /// held, so their tools stop looking here. While any request is still here (or after a crash) the file stays, and a
    /// retry or request call for a local id is reported unreachable rather than sent to the Ledger.</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _app.StopAsync().ConfigureAwait(false);
        try { if (!await HoldsUnsentAsync().ConfigureAwait(false)) File.Delete(_clientFile); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        await _app.DisposeAsync().ConfigureAwait(false);
        _store.Dispose();
    }
}
