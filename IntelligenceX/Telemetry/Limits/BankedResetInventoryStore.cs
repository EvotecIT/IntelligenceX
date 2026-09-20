using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>
/// Shared local reset inventory. Atomic replacement and an exclusive transaction lock prevent
/// competing desktop surfaces from silently losing entries. Invalid data is surfaced, never overwritten as empty.
/// </summary>
public sealed class BankedResetInventoryStore {
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    /// <summary>Uses a per-user shared file unless an explicit path is supplied for an isolated host or test.</summary>
    public BankedResetInventoryStore(string? path = null) {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligenceX", "Accounts", "banked-resets.json"));
    }

    /// <summary>Loads a consistent inventory. IO and format failures remain visible to the caller.</summary>
    public IReadOnlyList<BankedResetCredit> Load() {
        using var transaction = AcquireTransaction();
        return Read();
    }

    /// <summary>Adds or updates one scoped record while preserving other accounts and concurrent changes.</summary>
    public void Save(BankedResetCredit credit) {
        if (credit is null) throw new ArgumentNullException(nameof(credit));
        using var transaction = AcquireTransaction();
        var records = Read().ToList();
        var index = records.FindIndex(c => SameIdentity(c, credit));
        if (index >= 0) records[index] = credit;
        else records.Add(credit);
        Write(records);
    }

    /// <summary>Removes only the exact scoped record, for correcting an entry rather than redeeming it.</summary>
    public void Remove(string providerId, string accountId, string id) {
        using var transaction = AcquireTransaction();
        var records = Read().ToList();
        var removed = records.RemoveAll(c => string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.AccountId, accountId, StringComparison.Ordinal)
            && string.Equals(c.Id, id, StringComparison.Ordinal));
        if (removed > 0) Write(records);
    }

    private FileStream AcquireTransaction() {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Fail visibly if another surface is writing; the UI can offer Retry without blocking its dispatcher.
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private BankedResetCredit[] Read() {
        if (!File.Exists(_path)) return Array.Empty<BankedResetCredit>();
        var inventory = JsonSerializer.Deserialize<Inventory>(File.ReadAllText(_path), Options)
                        ?? throw new InvalidDataException("Reset inventory is empty or invalid.");
        if (inventory.Version != 1 || inventory.Credits is null || inventory.Credits.Any(c => c is null)) {
            throw new InvalidDataException("Reset inventory has an unsupported version or invalid records.");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credit in inventory.Credits) {
            // A structured identity avoids delimiter collisions in account IDs.
            var key = JsonSerializer.Serialize(new[] { credit.ProviderId.ToUpperInvariant(), credit.AccountId, credit.Id });
            if (!seen.Add(key)) throw new InvalidDataException("Reset inventory contains duplicate records.");
        }
        return inventory.Credits;
    }

    private void Write(List<BankedResetCredit> records) {
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            var json = JsonSerializer.Serialize(new Inventory { Version = 1, Credits = records.ToArray() }, Options);
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_path)) File.Replace(temporaryPath, _path, null);
            else File.Move(temporaryPath, _path);
        } finally {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool SameIdentity(BankedResetCredit a, BankedResetCredit b) =>
        string.Equals(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.AccountId, b.AccountId, StringComparison.Ordinal)
        && string.Equals(a.Id, b.Id, StringComparison.Ordinal);

    private sealed class Inventory {
        public Inventory() { }
        public int Version { get; set; }
        public BankedResetCredit[]? Credits { get; set; }
    }
}
