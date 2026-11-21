using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static readonly HashSet<string> CheckoutStates = new(StringComparer.Ordinal) { "observed", "git-unavailable", "missing" };

    internal static async Task InitializeCheckoutsAsync(SQLiteAsyncSession db, CancellationToken token = default) {
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS checkout_scans (machine TEXT PRIMARY KEY, observed TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checkouts (machine TEXT NOT NULL, path TEXT NOT NULL, repo TEXT NOT NULL, json TEXT NOT NULL,
              PRIMARY KEY(machine,path));
            CREATE INDEX IF NOT EXISTS checkouts_repo ON checkouts(repo);
            """, cancellationToken: token);
    }

    /// <summary>A scan stays within the reporter's own bounds and names only repositories and paths.</summary>
    public static bool ValidCheckoutCensus(CheckoutCensus? census, DateTimeOffset now) => census is null ||
        census.ObservedAtUtc.Offset == TimeSpan.Zero && census.ObservedAtUtc <= now.AddMinutes(2) &&
        census.Roots is { Length: <= CheckoutScanner.MaximumRoots } && census.Roots.All(root => Text(root, 1024)) &&
        census.Checkouts is { Length: <= CheckoutScanner.MaximumCheckouts } && census.Checkouts.All(checkout => checkout is not null &&
            Text(checkout.Repo, 256) && Text(checkout.Path, 1024) && CheckoutStates.Contains(checkout.State) &&
            Optional(checkout.Branch, 256) && Optional(checkout.Commit, 64) && Optional(checkout.Upstream, 256) &&
            new[] { checkout.Ahead, checkout.Behind, checkout.ChangedFiles, checkout.UntrackedFiles, checkout.Stashes }.All(n => n is null or >= 0) &&
            (checkout.UnpushedBranches is null || checkout.UnpushedBranches.Length <= CheckoutScanner.MaximumUnpushed &&
                checkout.UnpushedBranches.All(branch => Text(branch, 256))));

    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static bool Optional(string? value, int limit) => value is null || Text(value, limit);

    /// <summary>Replaces the machine's checkouts with a newer scan. An older scan arriving late is ignored.</summary>
    private static async Task RecordCheckoutsAsync(SQLiteAsyncSession db, string machine, CheckoutCensus census, CancellationToken token) {
        var args = new Dictionary<string, object?> { ["machine"] = machine, ["observed"] = census.ObservedAtUtc.ToString("O") };
        var newer = await db.ExecuteScalarAsync("SELECT 1 FROM checkout_scans WHERE machine=@machine AND observed>@observed", args, token);
        if (newer is not null) return;
        await db.ExecuteNonQueryAsync("""
            INSERT INTO checkout_scans(machine,observed,json) VALUES(@machine,@observed,@json)
            ON CONFLICT(machine) DO UPDATE SET observed=excluded.observed,json=excluded.json
            """, new Dictionary<string, object?>(args) { ["json"] = Json(new CheckoutScan(machine, census.ObservedAtUtc, census.Truncated)) }, token);
        await db.ExecuteNonQueryAsync("DELETE FROM checkouts WHERE machine=@machine", args, token);
        foreach (var checkout in census.Checkouts.DistinctBy(checkout => checkout.Path, StringComparer.Ordinal))
            await db.ExecuteNonQueryAsync("INSERT INTO checkouts(machine,path,repo,json) VALUES(@machine,@path,@repo,@json)",
                new Dictionary<string, object?>(args) {
                    ["path"] = checkout.Path, ["repo"] = RepositoryOverlapEvaluator.NormalizeRepo(checkout.Repo),
                    ["json"] = Json(new RepositoryCheckout(machine, census.ObservedAtUtc, checkout))
                }, token);
    }

    /// <summary>One sentence for the summary line: checkouts per scanned machine, then anything not yet on a remote.</summary>
    internal static string CheckoutSummary(RepositoryCheckout[] checkouts, CheckoutScan[] scans, DateTimeOffset now) {
        if (scans.Length == 0) return " Checkouts: no machine has scanned yet.";
        var stale = scans.Where(scan => scan.ObservedAtUtc < now - CheckoutScanner.Interval * 4 || scan.Truncated).Select(scan => scan.Machine).ToArray();
        var counts = string.Join(", ", scans.Select(scan => scan.Machine + " " + checkouts.Count(c => c.Machine == scan.Machine)));
        var pending = checkouts.Select(c => (c, Facts: Pending(c.Checkout))).Where(row => row.Facts.Length > 0).ToArray();
        var attention = pending.Length == 0 ? " Nothing unpushed or uncommitted."
            : " Not on a remote: " + string.Join("; ", pending.Take(5).Select(row =>
                row.c.Machine + " " + LeafName(row.c.Checkout.Path) + " (" + string.Join(", ", row.Facts) + ")")) +
              (pending.Length > 5 ? "; and " + (pending.Length - 5) + " more." : ".");
        var incomplete = stale.Length == 0 ? "" : " Scan old or incomplete on " + string.Join(", ", stale) + ".";
        return " Checkouts: " + counts + "." + attention + incomplete;
    }

    private static string[] Pending(CheckoutObservation checkout) {
        var facts = new List<string>();
        if (checkout.UnpushedBranches is { Length: > 0 } branches) facts.Add("unpushed " + string.Join(", ", branches.Take(3)) + (branches.Length > 3 ? " +" + (branches.Length - 3) : ""));
        if (checkout.Ahead > 0) facts.Add(checkout.Ahead + " ahead of " + checkout.Upstream);
        if (checkout.ChangedFiles > 0) facts.Add(checkout.ChangedFiles + " changed");
        if (checkout.UntrackedFiles > 0) facts.Add(checkout.UntrackedFiles + " untracked");
        if (checkout.Stashes > 0) facts.Add(checkout.Stashes + (checkout.Stashes == 1 ? " stash" : " stashes"));
        return facts.ToArray();
    }

    // Paths come from Windows and macOS alike, so split on both separators whatever the service runs on.
    private static string LeafName(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : path;

    private static async Task<(RepositoryCheckout[] Checkouts, CheckoutScan[] Scans)> ReadCheckoutsAsync(SQLiteAsyncSession db, string repo, CancellationToken token) {
        var args = new Dictionary<string, object?> { ["repo"] = repo };
        var checkouts = await db.QueryAsListAsync("SELECT json FROM checkouts WHERE repo=@repo ORDER BY machine,path",
            row => Read<RepositoryCheckout>(row.GetString(0)), args, cancellationToken: token);
        var scans = await db.QueryAsListAsync("SELECT json FROM checkout_scans ORDER BY machine",
            row => Read<CheckoutScan>(row.GetString(0)), cancellationToken: token);
        // Main checkouts first, then worktrees, so the repository-wide facts lead each machine's rows.
        return (checkouts.OrderBy(c => c.Machine, StringComparer.Ordinal).ThenByDescending(c => c.Checkout.Primary)
            .ThenBy(c => c.Checkout.Path, StringComparer.Ordinal).ToArray(), scans.ToArray());
    }
}
