using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Ledger.Reporting.Probe;

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
    public string? ProbeEndpoint { get; init; }
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
        LedgerEvent Event(string item, string provider, SessionFact? session = null, string[]? coverage = null) => new() {
            EventId = Guid.NewGuid(), Epoch = epoch, Sequence = 1, MachineId = settings.MachineId,
            ReporterId = settings.ReporterId, ItemId = item, Provider = provider, Source = hook ? "hook" : "watcher",
            ObservedAtUtc = DateTimeOffset.UtcNow, SourceVersion = sourceVersion, Session = session, Coverage = coverage
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
        var archives = new SessionArchiveCache();
        await using var watcher = new AgentWorkWatcher(options: new AgentWorkOptions { ActiveWithin = null, LocalSourcesOnly = true },
            readMetadataAsync: async ct => {
                var available = await IntelligenceX.Codex.CodexAgentSessionMetadataReader.ReadAvailableAsync(
                    LocalAgentWorkStateService.DiscoverRoots().Where(r =>
                        r.SourceKind == IntelligenceX.Telemetry.Usage.UsageSourceKind.LocalLogs && r.ProviderId == "codex"), ct);
                Volatile.Write(ref metadataDiagnostics, available.Diagnostics.ToArray());
                archives.Observe(available.Sessions, DateTimeOffset.UtcNow);
                return available.Sessions;
            }, github: settings.GitHub ? github : null);
        var changes = new ReporterChangeTracker(spool, settings.SpoolDirectory);
        var workspaces = new ReporterWorkspaceCache();
        using var probeHandler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var probeClient = new HttpClient(probeHandler) { Timeout = Timeout.InfiniteTimeSpan };
        var probe = new ProbeReader(probeClient, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "probe-reader"),
            "IntelligenceX Ledger " + settings.MachineId, settings.ProbeEndpoint);
        string[] coverage = ["collection-starting"];
        var captureGate = new object();
        void Capture(AgentWorkSnapshot snapshot) {
            lock (captureGate) {
            foreach (var session in snapshot.Sessions) {
                var id = ItemId(settings.MachineId, session.Provider, session.Id);
                var fact = SessionEvidence(settings, session, archives.For(session)) with {
                    Repository = workspaces.For(session, settings.GitHub && snapshot.GitHubCheckedAtUtc >= DateTimeOffset.UtcNow.AddMinutes(-10))
                };
                changes.TryAppend(Event(id, session.Provider, fact));
                if (fact.SourceMessageId is not null || session.PeerMessages.Count > 0)
                    foreach (var messageEvent in SessionMessageEvents(Event(id, session.Provider, fact), session.PeerMessages))
                        changes.TryAppend(messageEvent);
                foreach (var (childInputId, inputFact) in InputFacts(session)) {
                    var childId = LedgerScope.InputItemId(id, childInputId);
                    changes.TryAppend(Event(childId, session.Provider, inputFact) with { ItemKind = "input", ParentItemId = id });
                }
                var claims = Volatile.Read(ref claimSnapshot);
                // Claim membership and its freshness are published once in ProjectEvidence.
                // Repeating its check time for every historical session creates a full fan-out each refresh.
                var work = new WorkFact { HadPullRequests = session.PullRequests.Count > 0 || claims?.ItemsWithPullRequests.Contains(id) == true };
                changes.TryAppend(Event(id, session.Provider) with { Work = work });
            }
            changes.Checkpoint();
            }
        }
        async Task UploadLoop() {
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            do {
                var machineFacts = await probe.ReadAsync(stop.Token);
                await DeliverAsync(client, spool, Event("machine", "machine", coverage: Volatile.Read(ref coverage)), stop.Token, changes, machineFacts);
            } while (await heartbeatTimer.WaitForNextTickAsync(stop.Token));
        }
        var uploader = UploadLoop();
        _ = uploader.ContinueWith(_ => stop.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
        try {
        await watcher.StartAsync(stop.Token);
        watcher.SnapshotChanged += (_, change) => Capture(change.Snapshot);
        Console.WriteLine(JsonSerializer.Serialize(new { phase = "watching", sessions = watcher.CurrentSnapshot.Sessions.Count,
            sourceDiagnostics = watcher.CurrentSnapshot.Diagnostics.Select(x => x.Code).Distinct().ToArray() }));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var evidenceImporter = settings.EvidenceFile is null ? null : new EvidenceSnapshotImporter(settings.EvidenceFile, settings.SpoolDirectory);
        var claimsChecked = DateTimeOffset.MinValue;
        var proofsChecked = DateTimeOffset.MinValue;
        PullRequestProof[] proofs = [];
        string? lastGlobalRefreshSignature = null;
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
            await workspaces.RefreshAsync(watcher.CurrentSnapshot.Sessions, DateTimeOffset.UtcNow, stop.Token);
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
                var observation = Event("project-evidence", "project") with { GlobalWork = global };
                var refreshed = JsonSerializer.Serialize(global, LedgerJson.Options);
                if (changes.TryAppend(observation)) {
                    changes.Checkpoint();
                    lastGlobalRefreshSignature = refreshed;
                    try { prCatalog?.RetireAdmittedMerged(proofs); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("pull-request-catalog-retirement-pending"); }
                } else if (refreshed != lastGlobalRefreshSignature &&
                    await RefreshProjectEvidenceAsync(client, spool, changes, observation, stop.Token)) {
                    lastGlobalRefreshSignature = refreshed;
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
            Volatile.Write(ref coverage, watcher.CurrentSnapshot.Diagnostics.Select(x => x.Code)
                .Concat(Volatile.Read(ref metadataDiagnostics).Select(x => x.Code + ":" + x.SourceRootId))
                .Append("queue-not-visible").Concat(settings.ProjectClaims is not null && claimSnapshot is null ? [claimFailure] : Array.Empty<string>())
                .Concat(settings.GitHub && watcher.CurrentSnapshot.GitHubCheckedAtUtc is null ? ["pull-requests-unavailable"] : Array.Empty<string>())
                .Concat(proofGap ? ["pull-request-proof-unavailable"] : Array.Empty<string>())
                .Concat(settings.EvidenceFile is null ? ["delivery-evidence-not-configured"] :
                    File.Exists(settings.EvidenceFile) ? Array.Empty<string>() : ["coordination-evidence-file-missing"])
                .Concat(evidenceGap is null ? Array.Empty<string>() : [evidenceGap]).Distinct().Order(StringComparer.Ordinal).ToArray());
        } while (await timer.WaitForNextTickAsync(stop.Token));
        } finally {
            stop.Cancel();
            try { await uploader; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        return 0;
    }

    private sealed record Receipt(Guid[] AcknowledgedEventIds);

    /// <summary>Projects the selected session's metadata into the fact published by the watcher.</summary>
    internal static SessionFact SessionEvidence(ReporterSettings settings, AgentSessionState session,
        SessionArchiveCache.Observation? archive) {
        var pending = session.PendingInputs;
        var blocking = pending.FirstOrDefault(x => x.IsBlocking && x.Id is not null && !x.AnswerIsUnconfirmed && !PriorTurn(x, session));
        return new SessionFact {
            TurnId = session.TurnId is null ? null : LedgerScope.Id("turn", session.TurnId),
            InputId = blocking?.Id is { } inputId ? LedgerScope.Id("input", inputId) : null,
            SourceSessionId = Guid.TryParse(session.Id, out var sourceId) ? sourceId : null,
            SourceMessageId = session.SourceMessageId,
            Archived = archive?.Archived,
            ArchiveObservedAtUtc = archive?.ObservedAtUtc,
            TurnOpen = session.TurnOpen, TurnStartedAtUtc = session.TurnStartedAtUtc?.ToUniversalTime(), TurnEndedAtUtc = session.TurnEndedAtUtc?.ToUniversalTime(),
            LastActivityAtUtc = session.LastTurnActivityAtUtc?.ToUniversalTime(), Outcome = session.LastTurnOutcome.ToString().ToLowerInvariant(),
            Question = blocking is not null, InputUnconfirmed = pending.Any(x => x.AnswerIsUnconfirmed),
            QuestionAtUtc = blocking?.RequestedAtUtc?.ToUniversalTime(),
            // The existing limit hold covers both structured usage and rate limits.
            // Neither code establishes account scope or a reset time.
            UsageLimit = session.FailureCause is AgentFailureCause.UsageLimit or AgentFailureCause.RateLimit,
            AccountId = session.SourceRootId is { } rootId && settings.AccountByRoot.TryGetValue(rootId, out var account) ? account : null,
            RepositoryBranchKey = session.Repository is not null && session.Branch is not null
                ? ItemId("branch", session.Repository.ToLowerInvariant(), session.Branch) : null
        };
    }

    // The same production projection feeds the spool and the reader-to-ingestion regression.
    // Each fact uses its own envelope because admission permits one fact per event.
    internal static IEnumerable<LedgerEvent> SessionMessageEvents(LedgerEvent observation,
        IReadOnlyList<AgentMessageObservation>? peerMessages = null) {
        if (peerMessages is { Count: > 0 }) {
            foreach (var peer in peerMessages) {
                var retained = observation with {
                    ItemId = ItemId(observation.MachineId, observation.Provider, peer.SessionId),
                    Session = new SessionFact {
                        SourceSessionId = Guid.TryParse(peer.SessionId, out var sessionId) ? sessionId : null,
                        SourceMessageId = peer.SourceMessageId,
                        TurnId = peer.TurnId is null ? null : LedgerScope.Id("turn", peer.TurnId),
                        TurnOpen = peer.TurnOpen, TurnStartedAtUtc = peer.TurnStartedAtUtc,
                        TurnEndedAtUtc = peer.TurnEndedAtUtc, LastActivityAtUtc = peer.LastActivityAtUtc,
                        Outcome = peer.Outcome.ToString().ToLowerInvariant(),
                        UsageLimit = peer.FailureCause is AgentFailureCause.RateLimit or AgentFailureCause.UsageLimit
                    }
                };
                foreach (var messageEvent in SessionMessageEvents(retained)) yield return messageEvent;
            }
            // Older cache entries can still supply the current peer through the session fact.
            if (peerMessages.Any(peer => peer.SourceMessageId == observation.Session?.SourceMessageId)) yield break;
        }
        if (observation.Session is not { SourceMessageId: { } messageId } session) yield break;
        var id = LedgerScope.MessageItemId(observation.MachineId, observation.Provider, messageId);
        var message = observation with {
            EventId = Guid.NewGuid(), ItemId = id, ItemKind = "delivery", ParentItemId = null,
            Session = new SessionFact {
                SourceSessionId = session.SourceSessionId, SourceMessageId = messageId, TurnId = session.TurnId,
                TurnOpen = session.TurnOpen, TurnStartedAtUtc = session.TurnStartedAtUtc, TurnEndedAtUtc = session.TurnEndedAtUtc,
                LastActivityAtUtc = session.LastActivityAtUtc, Outcome = session.Outcome, UsageLimit = session.UsageLimit
            }
        };
        yield return message;
        if (session.TurnStartedAtUtc is { } started)
            yield return message with { EventId = Guid.NewGuid(), Session = null,
                Delivery = new(id, "started", started, TargetItemId: observation.ItemId) };
        if (session.TurnOpen == false && session.Outcome is "completed" or "failed" or "interrupted" or "incomplete")
            yield return message with { EventId = Guid.NewGuid(), Session = null,
                Delivery = new(id, "terminal", session.TurnEndedAtUtc,
                    session.Outcome is "completed" or "failed" ? session.Outcome : null,
                    observation.ItemId, session.UsageLimit && session.Outcome == "failed" ? "usage-limit" : null) };
    }

    // Positive answer evidence survives reporter restarts; a missing pending row is never an answer.
    internal static IEnumerable<(string Id, SessionFact Fact)> InputFacts(AgentSessionState session) {
        Guid? sourceSession = Guid.TryParse(session.Id, out var parsed) ? parsed : null;
        foreach (var input in session.PendingInputs.Where(x => x.Id is not null && (!x.IsBlocking || PriorTurn(x, session)))) {
            var id = LedgerScope.Id("input", input.Id!);
            yield return (id, new SessionFact {
                SourceSessionId = sourceSession, InputId = id, TurnId = input.TurnId is null ? null : LedgerScope.Id("turn", input.TurnId),
                Question = !input.AnswerIsUnconfirmed, QuestionBlocking = input.IsBlocking,
                InputUnconfirmed = input.AnswerIsUnconfirmed, QuestionAtUtc = input.RequestedAtUtc?.ToUniversalTime()
            });
        }
        foreach (var input in session.ResolvedInputs) {
            var id = LedgerScope.Id("input", input.Id);
            yield return (id, new SessionFact {
                SourceSessionId = sourceSession, InputId = id, TurnId = input.TurnId is null ? null : LedgerScope.Id("turn", input.TurnId),
                Outcome = "completed", TurnOpen = false, QuestionBlocking = false
            });
        }
    }

    internal static GlobalWorkFact? ProjectEvidence(ReporterSettings settings, AgentWorkSnapshot snapshot, ClaimSnapshot? claims, PullRequestProof[] proofs) {
        if (settings.ProjectClaims is null) return null;
        return new(claims?.CapturedAtUtc, claims?.ActiveItems.Order(StringComparer.Ordinal).ToArray() ?? [], snapshot.GitHubCheckedAtUtc,
            snapshot.PullRequests.Where(pr => pr.HeadRepository is not null).GroupBy(pr =>
                ItemId("branch", pr.HeadRepository!.ToLowerInvariant(), pr.Branch)).ToDictionary(g => g.Key,
                    g => g.Select(pr => ItemId("pr", "github", pr.Url)).Order(StringComparer.Ordinal).ToArray()), proofs,
            claims?.PullRequestUrlsByItem.ToDictionary(pair => pair.Key, pair => new PullRequestLinks(
                pair.Value.Select(url => ItemId("pr", "github", url)).ToArray(), !claims.IncompletePullRequestItems.Contains(pair.Key), claims.CapturedAtUtc)));
    }
    internal static async Task DeliverAsync(HttpClient client, ReporterSpool spool, LedgerEvent healthObservation,
        CancellationToken token, ReporterChangeTracker? changes = null, ProbeAvailability? probe = null) {
        var progress = changes?.Progress.Where(p => p.LastActivityAtUtc <= healthObservation.ObservedAtUtc).ToArray() ?? [];
        var coverage = (healthObservation.Coverage ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (progress.Length > 128) coverage.Insert(0, "progress-truncated");
        if (coverage.Count > 32) coverage = [.. coverage.Take(31), "coverage-truncated"];
        var health = new ReporterHealth(healthObservation.MachineId, healthObservation.ReporterId,
            healthObservation.SourceVersion, healthObservation.ObservedAtUtc, coverage.ToArray(), progress.Take(128).ToArray(), probe);
        await FlushAsync(client, spool, token, changes);
        try {
            using var response = await client.PostAsJsonAsync("api/ledger/v1/reporters/health", health, LedgerJson.Options, token);
            if (!response.IsSuccessStatusCode) Console.Error.WriteLine("ledger-health-rejected:" + (int)response.StatusCode);
        } catch (Exception error) when (!token.IsCancellationRequested &&
            error is HttpRequestException or IOException or TaskCanceledException) {
            Console.Error.WriteLine("ledger-health-delivery-pending");
        }
    }

    /// <summary>Refreshes successful source check times after the matching state has drained to the service.</summary>
    internal static async Task<bool> RefreshProjectEvidenceAsync(HttpClient client, ReporterSpool spool, ReporterChangeTracker changes,
        LedgerEvent observation, CancellationToken token) {
        if (!changes.IsCurrent(observation) || spool.Pending().Length > 0) return false;
        try {
            using var response = await client.PostAsJsonAsync("api/ledger/v1/project-evidence", observation, LedgerJson.Options, token);
            if (!response.IsSuccessStatusCode) Console.Error.WriteLine("ledger-project-refresh-rejected:" + (int)response.StatusCode);
            // A mismatch can settle after an outstanding state event. Retry the same observation later.
            return response.StatusCode != System.Net.HttpStatusCode.Conflict;
        } catch (Exception error) when (!token.IsCancellationRequested &&
            error is HttpRequestException or IOException or TaskCanceledException) {
            Console.Error.WriteLine("ledger-project-refresh-pending");
            return true; // The next successful source check supplies another observational refresh.
        }
    }

    private static async Task FlushAsync(HttpClient client, ReporterSpool spool, CancellationToken token, ReporterChangeTracker? changes) {
        while (spool.Pending() is { Length: > 0 } pending) {
            try {
                using var response = await client.PostAsJsonAsync("api/ledger/v1/events", pending, LedgerJson.Options, token);
                if (!response.IsSuccessStatusCode) { Console.Error.WriteLine("ledger-ingress-rejected:" + (int)response.StatusCode); return; }
                var receipt = await response.Content.ReadFromJsonAsync<Receipt>(cancellationToken: token);
                var sent = pending.Select(x => x.EventId).ToHashSet();
                if (receipt is null || receipt.AcknowledgedEventIds.Any(id => !sent.Contains(id))) return;
                if (changes is null) spool.Acknowledge(receipt.AcknowledgedEventIds);
                else changes.Acknowledge(receipt.AcknowledgedEventIds);
                if (receipt.AcknowledgedEventIds.Length == 0) return;
            } catch (HttpRequestException error) {
                var root = error.GetBaseException();
                var code = root is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString() : root.GetType().Name;
                Console.Error.WriteLine("ledger-transport-unavailable:" + error.HttpRequestError + ":" + code);
                return;
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                Console.Error.WriteLine("ledger-spool-delivery-pending");
                return;
            }
        }
    }

    private static bool PriorTurn(AgentPendingInput input, AgentSessionState session) =>
        input.TurnId is not null && session.TurnId is not null && input.TurnId != session.TurnId ||
        input.RequestedAtUtc is { } requested && session.TurnStartedAtUtc is { } started && requested < started;
}
