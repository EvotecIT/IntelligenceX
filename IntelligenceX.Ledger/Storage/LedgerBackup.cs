using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

/// <summary>Operator-requested online snapshots using DbaClientX's SQLite backup API.</summary>
public static class LedgerBackup {
    /// <summary>Copies a committed snapshot without stopping its writer or overwriting an existing backup.</summary>
    public static async Task<LedgerBackupManifest> CreateAsync(string source, string destination, CancellationToken token = default) {
        if (!Path.IsPathFullyQualified(source) || !Path.IsPathFullyQualified(destination) || !File.Exists(source) ||
            !Directory.Exists(Path.GetDirectoryName(destination)) || File.Exists(destination) || File.Exists(destination + ".json"))
            throw new InvalidDataException("Backup requires an existing source and a new absolute destination in an existing directory.");
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try {
            var manifest = await SnapshotAsync(source, staging, token);
            await using (var output = new FileStream(staging + ".json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(output, manifest, LedgerJson.Options, token);
            // Unique provider destinations isolate concurrent copies. Promotion refuses any existing final file.
            File.Move(staging, destination, overwrite: false);
            File.Move(staging + ".json", destination + ".json", overwrite: false);
            return manifest;
        } finally {
            File.Delete(staging);
            File.Delete(staging + ".json");
        }
    }

    private static async Task<LedgerBackupManifest> SnapshotAsync(string source, string destination, CancellationToken token) {
        var watch = Stopwatch.StartNew();
        using var database = new SQLite();
        var result = await database.BackupDatabaseAsync(source, destination, cancellationToken: token);
        long events, ordinal, items;
        await using (var snapshot = await database.OpenSessionAsync(destination, token)) {
            var integrity = await snapshot.QueryAsListAsync("PRAGMA integrity_check", row => row.GetString(0), cancellationToken: token);
            if (integrity.Count != 1 || integrity[0] != "ok") throw new InvalidDataException("Backup integrity check failed.");
            var counts = await snapshot.QueryAsListAsync(
                "SELECT COUNT(*),COALESCE(MAX(ordinal),0),(SELECT COUNT(*) FROM items) FROM events",
                row => (Events: row.GetInt64(0), Ordinal: row.GetInt64(1), Items: row.GetInt64(2)), cancellationToken: token);
            (events, ordinal, items) = counts.Single();
        }
        await using var data = File.OpenRead(destination);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(data, token)).ToLowerInvariant();
        return new LedgerBackupManifest(1, DateTimeOffset.UtcNow, result.Method.ToString(),
            data.Length, hash, events, ordinal, items, watch.ElapsedMilliseconds);
    }
}

/// <summary>Verified backup identity and the last included durable journal ordinal.</summary>
public sealed record LedgerBackupManifest(int SchemaVersion, DateTimeOffset CompletedAtUtc, string Method,
    long Bytes, string Sha256, long Events, long LastEventOrdinal, long Items, long DurationMilliseconds);
