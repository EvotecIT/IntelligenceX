using System.Net.Http.Json;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Sends the agent service's sign-in states to the Ledger as `sign-in` items, using the machine reporter's
/// identity and credentials. Only the latest state per provider is kept; one the Ledger hasn't acknowledged is sent
/// again every minute, so a Ledger restart or a network gap only delays it.</summary>
internal sealed class SignInPublisher : IDisposable {
    private readonly ReporterSettings _reporter;
    private readonly HttpClient _client;
    private readonly Dictionary<string, SignInFact> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Guid _epoch = Guid.NewGuid();
    private long _sequence;

    public SignInPublisher(string reporterConfig) {
        _reporter = JsonSerializer.Deserialize<ReporterSettings>(File.ReadAllText(reporterConfig), LedgerJson.Options)
            ?? throw new InvalidDataException("Missing reporter configuration.");
        _client = LocalReporter.CreateClient(_reporter);
    }

    public void Publish(SignInFact fact) {
        lock (_pending) _pending[fact.Provider] = fact;
        _ = FlushAsync(CancellationToken.None);
    }

    public async Task RunAsync(CancellationToken stop) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false)) await FlushAsync(stop).ConfigureAwait(false);
    }

    private async Task FlushAsync(CancellationToken token) {
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try {
            SignInFact[] facts;
            lock (_pending) facts = _pending.Values.ToArray();
            if (facts.Length == 0) return;
            var events = facts.Select(fact => new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = _epoch, Sequence = Interlocked.Increment(ref _sequence), MachineId = _reporter.MachineId,
                ReporterId = _reporter.ReporterId, ItemId = _reporter.MachineId + ":sign-in:" + fact.Provider, ItemKind = "sign-in",
                Provider = fact.Provider, Source = "watcher", ObservedAtUtc = DateTimeOffset.UtcNow, SignIn = fact
            }).ToArray();
            using var response = await _client.PostAsJsonAsync("api/ledger/v1/events", events, LedgerJson.Options, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            lock (_pending) foreach (var fact in facts) if (_pending.TryGetValue(fact.Provider, out var latest) && latest == fact) _pending.Remove(fact.Provider);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException) {
            AgentService.Log("sign-in-unpublished", error.Message);
        } finally { _gate.Release(); }
    }

    public void Dispose() { _client.Dispose(); _gate.Dispose(); }
}
