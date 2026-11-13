namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    public Task<RepositoryActivity> RepoActivityAsync(string repo, CancellationToken token = default, bool recent24h = false, bool details = false) =>
        GetAsync("api/ledger/v1/repo-activity?repo=" + Uri.EscapeDataString(repo) + (recent24h ? "&recent24h=true" : "") +
            (details ? "&details=true" : ""), LedgerReadJson.Default.RepositoryActivity, token);

    public Task<RepositoryOverlap> OverlapAsync(string repo, string? branch = null, string[]? files = null, string? text = null, CancellationToken token = default, bool recent24h = false, bool details = false) {
        var route = "api/ledger/v1/overlap?repo=" + Uri.EscapeDataString(repo);
        if (branch is not null) route += "&branch=" + Uri.EscapeDataString(branch);
        foreach (var file in files ?? []) route += "&files=" + Uri.EscapeDataString(file);
        if (text is not null) route += "&text=" + Uri.EscapeDataString(text);
        if (recent24h) route += "&recent24h=true";
        if (details) route += "&details=true";
        return GetAsync(route, LedgerReadJson.Default.RepositoryOverlap, token);
    }
}
