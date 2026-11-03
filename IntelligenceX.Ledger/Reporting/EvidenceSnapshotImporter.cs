using System.Security.Cryptography;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Durable admission progress for a metadata snapshot, independent of outbox acknowledgements.</summary>
internal sealed class EvidenceSnapshotImporter(string file, string spoolDirectory) {
    private sealed record Progress(string Hash, int NextIndex);
    private readonly string _checkpoint = Path.Combine(spoolDirectory, "evidence-import.cursor");

    internal bool Import(ReporterSpool spool) {
        if (!File.Exists(file)) return false;
        if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("Evidence snapshot exceeds its size bound.");
        var bytes = File.ReadAllBytes(file);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var progress = File.Exists(_checkpoint) ? JsonSerializer.Deserialize<Progress>(File.ReadAllText(_checkpoint), LedgerJson.Options) : null;
        var facts = JsonSerializer.Deserialize<LedgerEvent[]>(bytes, LedgerJson.Options) ?? throw new JsonException();
        var index = progress?.Hash == hash ? progress.NextIndex : 0;
        if (index < 0 || index > facts.Length) throw new InvalidDataException("Invalid evidence admission checkpoint.");
        if (index == facts.Length && progress?.Hash == hash) return true;
        var saved = index;
        while (index < facts.Length && spool.TryAppend(facts[index])) {
            index++;
            if (index % 100 == 0) { Save(new(hash, index)); saved = index; }
        }
        if (saved != index || progress?.Hash != hash) Save(new(hash, index));
        return index == facts.Length;
    }

    private void Save(Progress progress) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(progress, LedgerJson.Options);
        using (var output = new FileStream(_checkpoint + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) {
            output.Write(bytes); output.Flush(true);
        }
        File.Move(_checkpoint + ".tmp", _checkpoint, true);
    }
}
