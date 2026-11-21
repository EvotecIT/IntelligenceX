using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static readonly HashSet<string> CheckoutStates = new(StringComparer.Ordinal) { "observed", "partial", "git-unavailable", "missing" };

    // Keyed by reporter: two reporters on one machine can scan different folders, and neither scan may erase the other's.
    internal static async Task InitializeCheckoutsAsync(SQLiteAsyncSession db, CancellationToken token = default) {
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS checkout_scans (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, observed TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checkouts (reporter TEXT NOT NULL, path TEXT NOT NULL, machine TEXT NOT NULL, repo TEXT NOT NULL,
              json TEXT NOT NULL, PRIMARY KEY(reporter,path));
            CREATE INDEX IF NOT EXISTS checkouts_repo ON checkouts(repo);
            """, cancellationToken: token);
    }

    /// <summary>A scan stays within the reporter's own bounds and names only repositories and paths.</summary>
    public static bool ValidCheckoutCensus(CheckoutCensus? census, DateTimeOffset now) => census is null ||
        census.ObservedAtUtc.Offset == TimeSpan.Zero && census.ObservedAtUtc <= now.AddMinutes(2) &&
        census.Roots is { Length: <= CheckoutScanner.MaximumRoots } && census.Roots.All(root => Text(root, 1024)) &&
        census.Checkouts is { Length: <= CheckoutScanner.MaximumCheckouts } && census.Checkouts.All(ValidCheckout);

    /// <summary>The per-row check, shared with the scanner so it never sends a row the service would refuse.</summary>
    internal static bool ValidCheckout(CheckoutObservation? checkout) => checkout is not null &&
        Text(checkout.Repo, 256) && Text(checkout.Path, 1024) && CheckoutStates.Contains(checkout.State) &&
        Optional(checkout.Branch, 256) && Optional(checkout.Commit, 64) && Optional(checkout.Upstream, 256) &&
        new[] { checkout.Ahead, checkout.Behind, checkout.ChangedFiles, checkout.UntrackedFiles, checkout.Stashes }.All(n => n is null or >= 0) &&
        (checkout.UnpushedBranches is null || checkout.UnpushedBranches.Length <= CheckoutScanner.MaximumUnpushed &&
            checkout.UnpushedBranches.All(branch => Text(branch, 256)));

    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static bool Optional(string? value, int limit) => value is null || Text(value, limit);

    /// <summary>
    /// Applies a newer scan from one reporter; an older scan arriving late is ignored. A complete scan replaces that
    /// reporter's rows. An incomplete one only updates the checkouts it read, so any it could not reach stay, with
    /// their own older scan time, instead of looking gone. The next complete scan removes checkouts that no longer exist.
    /// </summary>
    private static async Task RecordCheckoutsAsync(SQLiteAsyncSession db, string machine, string reporter, CheckoutCensus census, CancellationToken token) {
        var args = new Dictionary<string, object?> { ["reporter"] = reporter, ["machine"] = machine, ["observed"] = census.ObservedAtUtc.ToString("O") };
        var newer = await db.ExecuteScalarAsync("SELECT 1 FROM checkout_scans WHERE reporter=@reporter AND observed>@observed", args, token);
        if (newer is not null) return;
        await db.ExecuteNonQueryAsync("""
            INSERT INTO checkout_scans(reporter,machine,observed,json) VALUES(@reporter,@machine,@observed,@json)
            ON CONFLICT(reporter) DO UPDATE SET machine=excluded.machine,observed=excluded.observed,json=excluded.json
            """, new Dictionary<string, object?>(args) { ["json"] = Json(new CheckoutScan(machine, census.ObservedAtUtc, census.Truncated)) }, token);
        var rows = census.Checkouts.DistinctBy(checkout => checkout.Path, StringComparer.Ordinal)
            .Select(checkout => (Checkout: checkout, Repo: RepositoryOverlapEvaluator.NormalizeRepo(checkout.Repo))).ToArray();
        if (!census.Truncated) await db.ExecuteNonQueryAsync("DELETE FROM checkouts WHERE reporter=@reporter", args, token);
        foreach (var (checkout, repo) in rows)
            await db.ExecuteNonQueryAsync("""
                INSERT INTO checkouts(reporter,path,machine,repo,json) VALUES(@reporter,@path,@machine,@repo,@json)
                ON CONFLICT(reporter,path) DO UPDATE SET machine=excluded.machine,repo=excluded.repo,json=excluded.json
                """, new Dictionary<string, object?>(args) {
                    ["path"] = checkout.Path, ["repo"] = repo,
                    ["json"] = Json(new RepositoryCheckout(machine, census.ObservedAtUtc, checkout))
                }, token);
    }
    /// <summary>One sentence for the summary line: checkouts per scanned machine, then anything not yet on a remote.</summary>
    internal static string CheckoutSummary(RepositoryCheckout[] checkouts, CheckoutScan[] scans, DateTimeOffset now) {
        if (scans.Length == 0) return " Checkouts: no machine has scanned yet.";
        var stale = scans.Where(scan => scan.ObservedAtUtc < now - CheckoutScanner.Interval * 4 || scan.Truncated).Select(scan => scan.Machine).ToArray();
        var counts = string.Join(", ", scans.Select(scan => scan.Machine + " " + checkouts.Count(c => c.Machine == scan.Machine)));
        var pending = checkouts.Select(c => (c, Facts: Pending(c.Checkout))).Where(row => row.Facts.Length > 0).ToArray();
        // A checkout Git could not fully read is unknown, never clean.
        var unknown = checkouts.Where(c => c.Checkout.State != "observed").ToArray();
        var attention = pending.Length == 0 ? (unknown.Length == 0 ? " Nothing unpushed or uncommitted." : "")
            : " Not on a remote: " + string.Join("; ", pending.Take(5).Select(row =>
                row.c.Machine + " " + LeafName(row.c.Checkout.Path) + " (" + string.Join(", ", row.Facts) + ")")) +
              (pending.Length > 5 ? "; and " + (pending.Length - 5) + " more." : ".");
        var unread = unknown.Length == 0 ? "" : " Not fully read: " + string.Join("; ", unknown.Take(5).Select(c =>
            c.Machine + " " + LeafName(c.Checkout.Path) + " (" + c.Checkout.State + ")")) + (unknown.Length > 5 ? "; and " + (unknown.Length - 5) + " more." : ".");
        var incomplete = stale.Length == 0 ? "" : " Scan old or incomplete on " + string.Join(", ", stale) + ".";
        return " Checkouts: " + counts + "." + attention + unread + incomplete;
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
        var checkouts = await db.QueryAsListAsync("SELECT json FROM checkouts WHERE repo=@repo",
            row => Read<RepositoryCheckout>(row.GetString(0)), args, cancellationToken: token);
        var scans = await db.QueryAsListAsync("SELECT json FROM checkout_scans",
            row => Read<CheckoutScan>(row.GetString(0)), cancellationToken: token);
        // Two reporters can see the same checkout; keep the newer reading. A machine's scan time is its oldest reporter's,
        // so a fresh scan from one reporter never hides a stale one from another.
        var unique = checkouts.GroupBy(c => (c.Machine, c.Checkout.Path)).Select(g => g.MaxBy(c => c.ObservedAtUtc)!);
        var machines = scans.GroupBy(scan => scan.Machine, StringComparer.Ordinal).Select(g =>
            new CheckoutScan(g.Key, g.Min(scan => scan.ObservedAtUtc), g.Any(scan => scan.Truncated)));
        // Main checkouts first, then worktrees, so the repository-wide facts lead each machine's rows.
        return (unique.OrderBy(c => c.Machine, StringComparer.Ordinal).ThenByDescending(c => c.Checkout.Primary)
            .ThenBy(c => c.Checkout.Path, StringComparer.Ordinal).ToArray(),
            machines.OrderBy(scan => scan.Machine, StringComparer.Ordinal).ToArray());
    }
}
