using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Bounded metadata-only catalog retains known links after a PR leaves an open-search result.</summary>
internal sealed class PullRequestCatalog(string directory, GitHubDashboardService source) {
    private sealed record Entry(string? Url, PullRequestProof? Proof);
    private readonly string _file = Path.Combine(directory, "pull-requests.catalog");
    private Dictionary<string, Entry>? _entries;
    internal bool ReadIncomplete { get; private set; }
    internal static bool TryParse(string url, out string repository, out int number) {
        repository = ""; number = 0;
        if (url.Length > 512 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[2] != "pull" || !int.TryParse(parts[3], out number) || number < 1 ||
            parts.Take(2).Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) return false;
        repository = parts[0] + "/" + parts[1];
        return true;
    }

    internal async Task<PullRequestProof[]> RefreshAsync(IEnumerable<string> urls, CancellationToken token) {
        _entries ??= File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file), LedgerJson.Options)! : [];
        ReadIncomplete = false;
        var active = _entries.Values.Count(entry => entry.Url is not null);
        foreach (var url in urls.Distinct(StringComparer.Ordinal)) {
            if (!TryParse(url, out _, out _)) throw new InvalidDataException("Invalid known PR link.");
            var id = LocalReporter.ItemId("pr", "github", url);
            if (_entries.ContainsKey(id)) continue; // Null URL is an immutable merged proof already durably admitted.
            if (active >= 1000) { ReadIncomplete = true; continue; }
            _entries.Add(id, new(url, null)); active++;
        }
        Save(); // Link identity is durable even if shutdown interrupts its first status read.
        using var limit = new SemaphoreSlim(4);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var updated = await Task.WhenAll(_entries.Where(pair => pair.Value.Url is not null).Select(async pair => {
            if (pair.Value.Proof?.State == "merged") return (pair.Key, pair.Value);
            var acquired = false;
            try {
                await limit.WaitAsync(deadline.Token); acquired = true;
                TryParse(pair.Value.Url!, out var repository, out var number);
                var pr = await source.FetchAgentPullRequestAsync(repository, number, deadline.Token);
                return (pair.Key, pair.Value with { Proof = new(pair.Key, pr.IsOpen ? "open" : pr.IsMerged ? "merged" : "closed", DateTimeOffset.UtcNow) });
            } catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException or JsonException or InvalidOperationException or OperationCanceledException) {
                ReadIncomplete = true;
                return (pair.Key, pair.Value); // Prior proof keeps its source time; failed reads never renew it.
            } finally { if (acquired) limit.Release(); }
        }));
        foreach (var (id, value) in updated) _entries[id] = value;
        Save();
        return _entries.Values.Select(entry => entry.Proof).OfType<PullRequestProof>().OrderBy(proof => proof.Id, StringComparer.Ordinal).ToArray();
    }

    internal void RetireAdmittedMerged(IEnumerable<PullRequestProof> admitted) {
        if (_entries is null) return;
        var terminal = _entries.Values.Count(entry => entry.Url is null);
        foreach (var proof in admitted.Where(proof => proof.State == "merged")) {
            if (!_entries.TryGetValue(proof.Id, out var entry) || entry.Proof != proof) continue;
            if (terminal >= 100000) { ReadIncomplete = true; continue; }
            _entries[proof.Id] = new(null, null); terminal++;
        }
        Save(); // The caller first durably spools these immutable proofs; open/closed links remain refreshable.
    }

    private void Save() {
        var temporary = _file + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
            output.Write(JsonSerializer.SerializeToUtf8Bytes(_entries, LedgerJson.Options)); output.Flush(true);
        }
        File.Move(temporary, _file, true);
    }
}
