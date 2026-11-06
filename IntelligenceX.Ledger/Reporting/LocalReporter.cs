using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Ledger.Reporting;

public sealed record ReporterSettings {
    public required string Endpoint { get; init; }
    public required string Token { get; init; }
    public required string MachineId { get; init; }
    public required string ReporterId { get; init; }
    public required string SpoolDirectory { get; init; }
    public long MaximumSpoolBytes { get; init; } = 64 * 1024 * 1024;
    public string? CertificateSha256 { get; init; }
    public string? EvidenceFile { get; init; }
    public Dictionary<string, string> AccountByRoot { get; init; } = [];
    public bool GitHub { get; init; }
    public ProjectClaimsSettings? ProjectClaims { get; init; }
}

/// <summary>Read-only watcher backstop with local durable delivery, usable on Windows, macOS and Linux.</summary>
public static class LocalReporter {
    public static string ItemId(string machine, string provider, string session) => LedgerScope.SessionItemId(machine, provider, session);

    public static async Task<int> RunNotifyChainAsync(string file, string payload) {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        var root = document.RootElement;
        var incumbent = root.GetProperty("incumbent").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (incumbent.Length == 0) return 2;
        var start = new ProcessStartInfo(incumbent[0]) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in incumbent.Skip(1)) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(payload);
        using var process = Process.Start(start) ?? throw new IOException("Notify incumbent did not start.");
        await process.WaitForExitAsync();
        var result = process.ExitCode;
        try { await RunAsync(root.GetProperty("reporterConfig").GetString()!, hook: true, payload); }
        catch (Exception) { /* Optional reporting must preserve the incumbent callback's outcome. */ }
        return result;
    }

    public static async Task<int> RunAsync(string file, bool hook, string? suppliedPayload = null) {
        var settings = JsonSerializer.Deserialize<ReporterSettings>(File.ReadAllText(file), LedgerJson.Options)!;
        var endpoint = new Uri(settings.Endpoint);
        if ((endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            settings.Token.Length < 32 || settings.MaximumSpoolBytes < 1024 * 1024)
            throw new InvalidDataException("Invalid reporter transport configuration.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        if (settings.CertificateSha256 is { } pin) handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            certificate is not null && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow &&
            certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow &&
            Convert.ToHexString(SHA256.HashData(certificate.RawData)).Equals(pin, StringComparison.OrdinalIgnoreCase);
        using var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var spool = new ReporterSpool(settings.SpoolDirectory, settings.MaximumSpoolBytes, ownsDelivery: !hook);
        var epoch = Guid.NewGuid();
        string sourceVersion;
        using (var ownAssembly = File.OpenRead(typeof(LocalReporter).Assembly.Location))
            sourceVersion = Convert.ToHexStringLower(SHA256.HashData(ownAssembly))[..16];
        long sequence = 0;
        LedgerEvent Event(string item, string provider, SessionFact? session = null, bool heartbeat = false,
            string[]? coverage = null) => new() {
            EventId = Guid.NewGuid(), Epoch = epoch, Sequence = Interlocked.Increment(ref sequence), MachineId = settings.MachineId,
            ReporterId = settings.ReporterId, ItemId = item, Provider = provider, Source = hook ? "hook" : "watcher",
            ObservedAtUtc = DateTimeOffset.UtcNow, SourceVersion = sourceVersion, Session = session, Heartbeat = heartbeat, Coverage = coverage
        };
        if (hook) {
            // Hints only: a Stop or AskUserQuestion call does not establish completion or pending input.
            var buffer = new char[256 * 1024 + 1];
            var length = 0;
            while (suppliedPayload is null && length < buffer.Length) {
                var count = await Console.In.ReadAsync(buffer.AsMemory(length));
                if (count == 0) break;
                length += count;
            }
            if (length == buffer.Length) return 2;
            var input = suppliedPayload ?? new string(buffer, 0, length);
            if (input.Length > 256 * 1024) return 2;
            using var payload = JsonDocument.Parse(input);
            var root = payload.RootElement;
            var provider = root.TryGetProperty("hook_event_name", out _) ? "claude" : "codex";
            var id = root.TryGetProperty("session_id", out var sessionId) ? sessionId.GetString() :
                root.TryGetProperty("thread-id", out var threadId) ? threadId.GetString() : null;
            if (id is null) return 2;
            spool.Append(Event(ItemId(settings.MachineId, provider, id), provider, coverage: ["hook-hint-requires-reconciliation"]));
            return 0;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var credentials = settings.GitHub || settings.ProjectClaims is not null
            ? await GitHubCredentialResolver.ResolveAsync(stop.Token) : null;
        using var github = credentials?.HasToken == true ? new GitHubDashboardService(credentials.Token!) : null;
        using var projectClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        projectClient.DefaultRequestHeaders.UserAgent.ParseAdd("IntelligenceX-Ledger/1");
        if (credentials?.HasToken == true) projectClient.DefaultRequestHeaders.Authorization = new("Bearer", credentials.Token);
        var claimReader = settings.ProjectClaims is not null && credentials?.HasToken == true
            ? new ProjectClaimReader(projectClient, settings.ProjectClaims) : null;
        var prCatalog = github is null || settings.ProjectClaims is null ? null : new PullRequestCatalog(settings.SpoolDirectory, github);
        ClaimSnapshot? claimSnapshot = null;
        AgentSourceDiagnostic[] metadataDiagnostics = [];
        IReadOnlyDictionary<string, (bool Archived, DateTimeOffset Observed)> archives =
            new Dictionary<string, (bool, DateTimeOffset)>(StringComparer.Ordinal);
        await using var watcher = new AgentWorkWatcher(options: new AgentWorkOptions { ActiveWithin = null, LocalSourcesOnly = true },
            readMetadataAsync: async ct => {
                var available = await IntelligenceX.Codex.CodexAgentSessionMetadataReader.ReadAvailableAsync(
                    LocalAgentWorkStateService.DiscoverRoots().Where(r =>
                        r.SourceKind == IntelligenceX.Telemetry.Usage.UsageSourceKind.LocalLogs && r.ProviderId == "codex"), ct);
                Volatile.Write(ref metadataDiagnostics, available.Diagnostics.ToArray());
                var archiveObserved = DateTimeOffset.UtcNow;
                var priorArchives = Volatile.Read(ref archives);
                Volatile.Write(ref archives, available.Sessions.GroupBy(x => x.Id, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => (x.First().IsArchived,
                        priorArchives.TryGetValue(x.Key, out var prior) && prior.Archived == x.First().IsArchived
                            ? prior.Observed : archiveObserved), StringComparer.Ordinal));
                return available.Sessions;
            }, github: settings.GitHub ? github : null);
        var last = new Dictionary<string, string>(StringComparer.Ordinal);
        var lastWork = new Dictionary<string, string>(StringComparer.Ordinal);
        var captureGate = new object();
        void Capture(AgentWorkSnapshot snapshot) {
            lock (captureGate) {
            var archiveFacts = Volatile.Read(ref archives);
            foreach (var session in snapshot.Sessions) {
                var id = ItemId(settings.MachineId, session.Provider, session.Id);
                var pending = session.PendingInputs;
                var blocking = pending.FirstOrDefault(x => x.IsBlocking && x.Id is not null && !x.AnswerIsUnconfirmed && !PriorTurn(x, session));
                var fact = new SessionFact {
                    TurnId = session.TurnId is null ? null : LedgerScope.Id("turn", session.TurnId),
                    InputId = blocking?.Id is { } inputId ? LedgerScope.Id("input", inputId) : null,
                    SourceSessionId = Guid.TryParse(session.Id, out var sourceId) ? sourceId : null,
                    Archived = session.Provider == "codex" && archiveFacts.TryGetValue(session.Id, out var archive)
                        ? archive.Archived : null,
                    ArchiveObservedAtUtc = session.Provider == "codex" && archiveFacts.TryGetValue(session.Id, out var archiveTime)
                        ? archiveTime.Observed : null,
                    TurnOpen = session.TurnOpen, TurnStartedAtUtc = session.TurnStartedAtUtc?.ToUniversalTime(), TurnEndedAtUtc = session.TurnEndedAtUtc?.ToUniversalTime(),
                    LastActivityAtUtc = session.LastTurnActivityAtUtc?.ToUniversalTime(), Outcome = session.LastTurnOutcome.ToString().ToLowerInvariant(),
                    Question = blocking is not null, InputUnconfirmed = pending.Any(x => x.AnswerIsUnconfirmed),
                    QuestionAtUtc = blocking?.RequestedAtUtc?.ToUniversalTime(),
                    UsageLimit = session.FailureCause == AgentFailureCause.UsageLimit,
                    AccountId = session.SourceRootId is { } rootId && settings.AccountByRoot.TryGetValue(rootId, out var account) ? account : null
                    , RepositoryBranchKey = session.Repository is not null && session.Branch is not null
                        ? ItemId("branch", session.Repository.ToLowerInvariant(), session.Branch) : null
                };
                var signature = JsonSerializer.Serialize(fact, LedgerJson.Options);
                if (!last.TryGetValue(id, out var previous) || previous != signature) {
                    if (!spool.TryAppend(Event(id, session.Provider, fact))) continue;
                    last[id] = signature;
                }
                foreach (var input in pending.Where(x => x.Id is not null && (!x.IsBlocking || PriorTurn(x, session)))) {
                    var childInputId = LedgerScope.Id("input", input.Id!);
                    var childId = LedgerScope.InputItemId(id, childInputId);
                    var inputFact = new SessionFact {
                        SourceSessionId = fact.SourceSessionId, InputId = childInputId, TurnId = input.TurnId is null ? null : LedgerScope.Id("turn", input.TurnId),
                        Question = !input.AnswerIsUnconfirmed,
                        InputUnconfirmed = input.AnswerIsUnconfirmed, QuestionAtUtc = input.RequestedAtUtc?.ToUniversalTime()
                    };
                    var inputSignature = JsonSerializer.Serialize(inputFact, LedgerJson.Options);
                    if (last.GetValueOrDefault(childId) == inputSignature) continue;
                    if (spool.TryAppend(Event(childId, session.Provider, inputFact) with { ItemKind = "input", ParentItemId = id }))
                        last[childId] = inputSignature;
                }
                var claims = Volatile.Read(ref claimSnapshot);
                // Claim membership and its freshness are published once in ProjectEvidence.
                // Repeating its check time for every historical session creates a full fan-out each refresh.
                var work = new WorkFact { HadPullRequests = session.PullRequests.Count > 0 || claims?.ItemsWithPullRequests.Contains(id) == true };
                var workSignature = JsonSerializer.Serialize(work, LedgerJson.Options);
                if (!lastWork.TryGetValue(id, out var previousWork) || previousWork != workSignature) {
                    if (spool.TryAppend(Event(id, session.Provider) with { Work = work })) lastWork[id] = workSignature;
                }
            }
            }
        }
        async Task UploadLoop() {
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do {
                await DeliverAsync(client, spool, Event("machine", "machine", heartbeat: true), stop.Token);
            } while (await heartbeatTimer.WaitForNextTickAsync(stop.Token));
        }
        spool.TryAppend(Event("source-health", "machine", coverage: ["collection-starting"]));
        var uploader = UploadLoop();
        _ = uploader.ContinueWith(_ => stop.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
        try {
        await watcher.StartAsync(stop.Token);
        watcher.SnapshotChanged += (_, change) => Capture(change.Snapshot);
        Console.WriteLine(JsonSerializer.Serialize(new { phase = "watching", sessions = watcher.CurrentSnapshot.Sessions.Count,
            sourceDiagnostics = watcher.CurrentSnapshot.Diagnostics.Select(x => x.Code).Distinct().ToArray() }));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var evidenceImporter = settings.EvidenceFile is null ? null : new EvidenceSnapshotImporter(settings.EvidenceFile, settings.SpoolDirectory);
        string? lastGlobalSignature = null;
        var claimsChecked = DateTimeOffset.MinValue;
        var proofsChecked = DateTimeOffset.MinValue;
        PullRequestProof[] proofs = [];
        var proofGap = false;
        var claimFailure = "claims-unavailable";
        do {
            if (claimReader is not null && DateTimeOffset.UtcNow - claimsChecked > TimeSpan.FromMinutes(5)) {
                claimsChecked = DateTimeOffset.UtcNow;
                try { Volatile.Write(ref claimSnapshot, await claimReader.ReadAsync(stop.Token)); }
                catch (Exception error) when (!stop.IsCancellationRequested) {
                    claimFailure = error switch {
                        HttpRequestException http => "claims-http-" + (int?)http.StatusCode,
                        JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException => "claims-schema-unavailable",
                        InvalidDataException => "claims-incomplete",
                        _ => "claims-read-unavailable"
                    };
                    Console.Error.WriteLine(claimFailure);
                    Volatile.Write(ref claimSnapshot, null);
                }
            }
            Capture(watcher.CurrentSnapshot);
            if (prCatalog is not null && DateTimeOffset.UtcNow - proofsChecked > TimeSpan.FromMinutes(5)) {
                proofsChecked = DateTimeOffset.UtcNow;
                try {
                    proofs = await prCatalog.RefreshAsync(watcher.CurrentSnapshot.PullRequests.Select(pr => pr.Url)
                        .Concat(claimSnapshot?.PullRequestUrlsByItem.Values.SelectMany(urls => urls) ?? []), stop.Token);
                    proofGap = prCatalog.ReadIncomplete;
                } catch (Exception error) when (!stop.IsCancellationRequested && error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException) {
                    proofGap = true;
                    Console.Error.WriteLine("pull-request-proof-unavailable");
                }
            }
            if (ProjectEvidence(settings, watcher.CurrentSnapshot, claimSnapshot, proofs) is { } global) {
                var signature = JsonSerializer.Serialize(global, LedgerJson.Options);
                if (signature != lastGlobalSignature) {
                    if (spool.TryAppend(Event("project-evidence", "project") with { GlobalWork = global })) {
                        lastGlobalSignature = signature;
                        try { prCatalog?.RetireAdmittedMerged(proofs); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("pull-request-catalog-retirement-pending"); }
                    }
                }
            }
            string? evidenceGap = null;
            if (evidenceImporter is not null) {
                try { if (!evidenceImporter.Import(spool)) evidenceGap = "evidence-import-pending"; }
                catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
                    evidenceGap = "evidence-import-unavailable";
                    Console.Error.WriteLine(evidenceGap);
                }
            }
            spool.TryAppend(Event("source-health", "machine", coverage: watcher.CurrentSnapshot.Diagnostics.Select(x => x.Code)
                .Concat(Volatile.Read(ref metadataDiagnostics).Select(x => x.Code + ":" + x.SourceRootId))
                .Append("queue-not-visible").Concat(settings.ProjectClaims is not null && claimSnapshot is null ? [claimFailure] : Array.Empty<string>())
                .Concat(settings.GitHub && watcher.CurrentSnapshot.GitHubCheckedAtUtc is null ? ["pull-requests-unavailable"] : Array.Empty<string>())
                .Concat(proofGap ? ["pull-request-proof-unavailable"] : Array.Empty<string>())
                .Concat(settings.EvidenceFile is null ? ["delivery-evidence-not-configured"] :
                    File.Exists(settings.EvidenceFile) ? Array.Empty<string>() : ["coordination-evidence-file-missing"])
                .Concat(evidenceGap is null ? Array.Empty<string>() : [evidenceGap]).Distinct().ToArray()));
        } while (await timer.WaitForNextTickAsync(stop.Token));
        } finally {
            stop.Cancel();
            try { await uploader; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        return 0;
    }

    private sealed record Receipt(Guid[] AcknowledgedEventIds);

    internal static GlobalWorkFact? ProjectEvidence(ReporterSettings settings, AgentWorkSnapshot snapshot, ClaimSnapshot? claims, PullRequestProof[] proofs) {
        if (settings.ProjectClaims is null) return null;
        return new(claims?.CapturedAtUtc, claims?.ActiveItems.Order(StringComparer.Ordinal).ToArray() ?? [], snapshot.GitHubCheckedAtUtc,
            snapshot.PullRequests.Where(pr => pr.HeadRepository is not null).GroupBy(pr =>
                ItemId("branch", pr.HeadRepository!.ToLowerInvariant(), pr.Branch)).ToDictionary(g => g.Key,
                    g => g.Select(pr => ItemId("pr", "github", pr.Url)).Order(StringComparer.Ordinal).ToArray()), proofs,
            claims?.PullRequestUrlsByItem.ToDictionary(pair => pair.Key, pair => new PullRequestLinks(
                pair.Value.Select(url => ItemId("pr", "github", url)).ToArray(), !claims.IncompletePullRequestItems.Contains(pair.Key), claims.CapturedAtUtc)));
    }
    internal static async Task DeliverAsync(HttpClient client, ReporterSpool spool, LedgerEvent heartbeat, CancellationToken token) {
        await FlushAsync(client, spool, token);
        spool.TryAppend(heartbeat);
        await FlushAsync(client, spool, token);
    }

    private static async Task FlushAsync(HttpClient client, ReporterSpool spool, CancellationToken token) {
        while (spool.Pending() is { Length: > 0 } pending) {
            try {
                using var response = await client.PostAsJsonAsync("api/ledger/v1/events", pending, LedgerJson.Options, token);
                if (!response.IsSuccessStatusCode) { Console.Error.WriteLine("ledger-ingress-rejected:" + (int)response.StatusCode); return; }
                var receipt = await response.Content.ReadFromJsonAsync<Receipt>(cancellationToken: token);
                var sent = pending.Select(x => x.EventId).ToHashSet();
                if (receipt is null || receipt.AcknowledgedEventIds.Any(id => !sent.Contains(id))) return;
                spool.Acknowledge(receipt.AcknowledgedEventIds);
                if (receipt.AcknowledgedEventIds.Length == 0) return;
            } catch (HttpRequestException error) {
                var root = error.GetBaseException();
                var code = root is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString() : root.GetType().Name;
                Console.Error.WriteLine("ledger-transport-unavailable:" + error.HttpRequestError + ":" + code);
                return;
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { return; }
            catch (IOException) {
                Console.Error.WriteLine("ledger-spool-delivery-pending");
                return;
            }
        }
    }

    private static bool PriorTurn(AgentPendingInput input, AgentSessionState session) =>
        input.TurnId is not null && session.TurnId is not null && input.TurnId != session.TurnId ||
        input.RequestedAtUtc is { } requested && session.TurnStartedAtUtc is { } started && requested < started;
}
