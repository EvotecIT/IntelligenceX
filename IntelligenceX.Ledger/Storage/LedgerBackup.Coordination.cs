using System.Security.Cryptography;
using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Storage;

public static partial class LedgerBackup {
    /// <summary>Excludes a normal service writer while a stopped backup reads its databases and artifacts.</summary>
    internal static FileStream AcquireServiceLease(string dataDirectory) => new(
        Path.Combine(dataDirectory, "ledger.sqlite.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>Copies both databases and result artifacts while holding the service and coordination writer leases.
    /// Publishes a new directory only after every file has been checked. Local configuration is retained separately.</summary>
    public static async Task<LedgerBackupManifest> CreateStoppedAsync(string dataDirectory, string destination,
        CancellationToken token = default) {
        ValidateNewDirectory(destination);
        if (!Path.IsPathFullyQualified(dataDirectory) || !File.Exists(Path.Combine(dataDirectory, "ledger.sqlite")) ||
            !File.Exists(Path.Combine(dataDirectory, "coordination.sqlite")))
            throw new InvalidDataException("Stopped coordination backup requires both source databases.");
        RejectLink(dataDirectory);
        using var serviceLease = AcquireServiceLease(dataDirectory);
        using var coordinationLease = new FileStream(Path.Combine(dataDirectory, "coordination.sqlite.writer.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(staging);
        try {
            var ledger = Path.Combine(dataDirectory, "ledger.sqlite");
            var coordination = Path.Combine(dataDirectory, "coordination.sqlite");
            RejectLink(ledger);
            RejectLink(coordination);
            var manifest = await SnapshotAsync(ledger, Path.Combine(staging, "ledger.sqlite"), token);
            using var database = new SQLite();
            var copy = Path.Combine(staging, "coordination.sqlite");
            await database.BackupDatabaseAsync(coordination, copy, cancellationToken: token);
            await CheckIntegrityAsync(copy, token);
            var artifacts = new Dictionary<string, LedgerBackupFile>(StringComparer.Ordinal);
            var resultDirectory = Path.Combine(dataDirectory, "coordination-results");
            Directory.CreateDirectory(Path.Combine(staging, "coordination-results"));
            if (Directory.Exists(resultDirectory)) {
                foreach (var artifact in ArtifactFiles(resultDirectory, allowArchive: true, skipAbandonedStaging: true)) {
                    var target = Path.Combine(staging, "coordination-results", artifact.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(artifact.Path, target, overwrite: false);
                    artifacts.Add(artifact.Key, await IdentityAsync(target, token));
                }
            }
            var state = await ReadCoordinationAsync(copy, token);
            VerifyResults(staging, state);
            manifest = manifest with { SchemaVersion = 3, CompletedAtUtc = DateTimeOffset.UtcNow,
                Coordination = new(await IdentityAsync(copy, token), artifacts) };
            await File.WriteAllTextAsync(Path.Combine(staging, "ledger.sqlite.json"),
                JsonSerializer.Serialize(manifest, LedgerJson.Options), token);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return manifest;
        } finally {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Verifies a backup and publishes it to a new data directory. Coordination restores disable admission,
    /// pause active goals and require fresh host/worker observations and exact reconciliation before resumption.
    /// Configuration, credentials and enrollment are supplied separately by the operator.</summary>
    public static async Task<LedgerBackupManifest> RestoreAsync(string source, string destination,
        CancellationToken token = default) {
        ValidateNewDirectory(destination);
        var bundle = Directory.Exists(source);
        var ledger = bundle ? Path.Combine(source, "ledger.sqlite") : source;
        if (!Path.IsPathFullyQualified(source)) throw new InvalidDataException("Restore requires an absolute backup path.");
        if (bundle) RejectLink(source);
        RejectLink(ledger + ".json");
        var manifest = JsonSerializer.Deserialize<LedgerBackupManifest>(await File.ReadAllTextAsync(ledger + ".json", token),
            LedgerJson.Options) ?? throw new InvalidDataException("Missing backup manifest.");
        if (manifest.SchemaVersion is not (1 or 2 or 3) || (manifest.SchemaVersion >= 2) != (manifest.Coordination is not null) ||
            bundle != (manifest.SchemaVersion >= 2))
            throw new InvalidDataException("Unsupported or incomplete backup layout.");
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(staging);
        try {
            await CopyVerifiedAsync(ledger, Path.Combine(staging, "ledger.sqlite"), new(manifest.Bytes, manifest.Sha256), token);
            await CheckIntegrityAsync(Path.Combine(staging, "ledger.sqlite"), token);
            if (manifest.Coordination is { } coordination) {
                await CopyVerifiedAsync(Path.Combine(source, "coordination.sqlite"), Path.Combine(staging, "coordination.sqlite"),
                    coordination.Database, token);
                await CheckIntegrityAsync(Path.Combine(staging, "coordination.sqlite"), token);
                var results = Path.Combine(source, "coordination-results");
                var files = ArtifactFiles(results, allowArchive: manifest.SchemaVersion >= 3)
                    .ToDictionary(artifact => artifact.Key, artifact => artifact.Path, StringComparer.Ordinal);
                if (files.Count != coordination.Artifacts.Count || !coordination.Artifacts.Keys.All(files.ContainsKey))
                    throw new InvalidDataException("Backup artifacts do not match the manifest.");
                Directory.CreateDirectory(Path.Combine(staging, "coordination-results"));
                foreach (var artifact in coordination.Artifacts) {
                    var target = Path.Combine(staging, "coordination-results", artifact.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await CopyVerifiedAsync(files[artifact.Key], target, artifact.Value, token);
                }
                // This modifies only the verified staging copy, before any service can open the restore.
                var state = await ReadCoordinationAsync(Path.Combine(staging, "coordination.sqlite"), token);
                VerifyResults(staging, state);
                using var store = new CoordinationStore(Path.Combine(staging, "coordination.sqlite"), state.Policy);
                await store.InitializeAsync(token);
                await store.ExecuteAsync((engine, state) => {
                    var recovery = new CoordinationPrincipal("backup-restore", "operator", "", []);
                    engine.ReplacePolicy(state.Policy with { Enabled = false, Revision = state.Policy.Revision + 1 },
                        recovery, state.Policy.Revision);
                    foreach (var goal in state.Goals.Values.Where(g => !g.Paused && !g.Stopped && !g.Accepted).ToArray())
                        engine.SetGoal(goal with { Paused = true }, recovery, goal.Revision);
                    state.Hosts.Clear();
                    foreach (var worker in state.Workers.Values.ToArray())
                        state.Workers[worker.Id] = worker with { Eligible = false, UnavailableReason = "backup-restored" };
                    return true;
                }, DateTimeOffset.UtcNow, token);
            }
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return manifest;
        } finally {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void ValidateNewDirectory(string path) {
        if (!Path.IsPathFullyQualified(path) || Path.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path)))
            throw new InvalidDataException("Destination must be a new absolute directory with an existing parent.");
    }

    private static void RejectLink(string path) {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Backup paths must not be symbolic links or reparse points.");
    }

    private static void ValidateArtifact(string file) {
        RejectLink(file);
        var name = Path.GetFileName(file);
        var prefix = name.StartsWith("result-", StringComparison.Ordinal) ? "result-" : "assignment-";
        if ((File.GetAttributes(file) & FileAttributes.Directory) != 0 || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Length != prefix.Length + 64 + 4 || !name.EndsWith(".txt", StringComparison.Ordinal) ||
            !name.AsSpan(prefix.Length, 64).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Coordination results contain an unexpected file or directory.");
    }

    // Only this fixed second tier is part of schema 3. Enumerating one level explicitly rejects
    // traversal keys, extra directories and links, including files absent from a restore manifest.
    private static IEnumerable<(string Key, string Path)> ArtifactFiles(string directory, bool allowArchive,
        bool skipAbandonedStaging = false) {
        RejectLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
            var name = Path.GetFileName(entry);
            // Only the stopped source scan owns both writer leases. An interrupted immutable write
            // has no committed reference; preserve its local file and verify every committed result below.
            if (skipAbandonedStaging && name.Length == 41 && name.StartsWith(".partial-", StringComparison.Ordinal) &&
                Guid.TryParseExact(name.AsSpan(9), "N", out _) &&
                (File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0) continue;
            if (allowArchive && Path.GetFileName(entry) == CoordinationArtifactStore.ArchiveDirectoryName) {
                RejectLink(entry);
                if (!Directory.Exists(entry)) throw new InvalidDataException("The result archive must be a directory.");
                foreach (var file in Directory.EnumerateFileSystemEntries(entry)) {
                    ValidateArtifact(file);
                    yield return (CoordinationArtifactStore.ArchiveDirectoryName + "/" + Path.GetFileName(file), file);
                }
            } else {
                ValidateArtifact(entry);
                yield return (Path.GetFileName(entry), entry);
            }
        }
    }

    private static async Task<LedgerBackupFile> IdentityAsync(string file, CancellationToken token) {
        await using var stream = File.OpenRead(file);
        return new(stream.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token)));
    }

    private static async Task CopyVerifiedAsync(string source, string destination, LedgerBackupFile expected, CancellationToken token) {
        RejectLink(source);
        File.Copy(source, destination, overwrite: false);
        if (await IdentityAsync(destination, token) != expected) throw new InvalidDataException("Backup file hash or size mismatch.");
    }

    private static async Task CheckIntegrityAsync(string path, CancellationToken token) {
        using var database = new SQLite();
        await using var session = await database.OpenSessionAsync(path, token);
        var integrity = await session.QueryAsListAsync("PRAGMA integrity_check", row => row.GetString(0), cancellationToken: token);
        if (integrity.Count != 1 || integrity[0] != "ok") throw new InvalidDataException("Backup integrity check failed.");
    }

    private static async Task<CoordinationState> ReadCoordinationAsync(string path, CancellationToken token) {
        using var database = new SQLite();
        await using var session = await database.OpenSessionAsync(path, token);
        var json = (string?)await session.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1", cancellationToken: token);
        return JsonSerializer.Deserialize<CoordinationState>(json ?? throw new InvalidDataException("Missing coordination state."),
            new JsonSerializerOptions(LedgerJson.Options) { MaxDepth = 32 }) ?? throw new InvalidDataException("Missing coordination state.");
    }

    private static void VerifyResults(string directory, CoordinationState state) {
        var artifacts = new CoordinationArtifactStore(Path.Combine(directory, "coordination-results"));
        foreach (var work in state.Work.Values.Where(w => w.Request.ReturnTo is not null && w.ResultRef is not null))
            if (artifacts.ReadForAssignment(work.Request.Id, work.Generation, work.GrantId!, work.ResultRef!) is null)
                throw new InvalidDataException("Coordination backup is missing retained result content or its assignment binding.");
    }
}
