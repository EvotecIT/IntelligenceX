using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Durable, bounded outbox. Only acknowledged IDs are deleted; capacity failure is explicit.</summary>
public sealed class ReporterSpool : IDisposable {
    private readonly string _directory;
    private readonly long _maximumBytes;
    private readonly FileStream? _ownership;
    private readonly object _gate = new();
    private bool _capacityReported;

    public ReporterSpool(string directory, long maximumBytes, bool ownsDelivery = true) {
        _directory = Path.GetFullPath(directory);
        _maximumBytes = maximumBytes;
        Directory.CreateDirectory(_directory);
        if (ownsDelivery) _ownership = new FileStream(Path.Combine(_directory, "reporter.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (ownsDelivery) {
            try { Recover(); }
            catch { _ownership?.Dispose(); throw; }
        }
    }

    private void Recover() {
        using var admission = Admission();
        foreach (var temporary in Directory.EnumerateFiles(_directory, "*.json.tmp")) {
            var target = temporary[..^4];
            try {
                var bytes = File.ReadAllBytes(temporary);
                var observation = JsonSerializer.Deserialize<LedgerEvent>(bytes, LedgerJson.Options);
                if (observation is null || Path.GetFileName(target) != $"{observation.Sequence:D20}-{observation.EventId:N}.json")
                    throw new JsonException();
                if (File.Exists(target)) {
                    if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Conflicting spool remnant.");
                    File.Delete(temporary);
                } else File.Move(temporary, target);
            } catch (JsonException) {
                // An incomplete atomic write was never admitted; the watcher reconciles its source.
                File.Delete(temporary);
                Console.Error.WriteLine("ledger-spool-incomplete-write-reconciled");
            }
        }
    }

    private FileStream Admission() {
        var deadline = Environment.TickCount64 + 2000;
        while (true) {
            try { return new FileStream(Path.Combine(_directory, "admission.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(20); }
        }
    }

    /// <summary>Backpressure rejects new observations while leaving delivery able to drain retained files.</summary>
    public bool TryAppend(LedgerEvent observation) {
        lock (_gate) {
            try { Append(observation); _capacityReported = false; return true; }
            catch (SpoolCapacityException) {
                if (!_capacityReported) Console.Error.WriteLine("ledger-spool-full-reconciliation-pending");
                _capacityReported = true;
                return false;
            }
        }
    }

    public void Append(LedgerEvent observation) {
        lock (_gate) {
            using var admission = Admission();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, LedgerJson.Options);
            if (bytes.Length + 2 > LedgerJson.MaximumBatchBytes) throw new InvalidDataException("Event exceeds the ingress byte bound; source retained for correction.");
            var target = Path.Combine(_directory, $"{observation.Sequence:D20}-{observation.EventId:N}.json");
            if (File.Exists(target)) {
                var existing = JsonSerializer.Deserialize<LedgerEvent>(File.ReadAllText(target), LedgerJson.Options);
                if (!JsonSerializer.SerializeToUtf8Bytes(existing, LedgerJson.Options).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Conflicting spool event.");
                return;
            }
            if (Directory.EnumerateFiles(_directory, "*.json*").Sum(x => new FileInfo(x).Length) + bytes.Length > _maximumBytes)
                throw new SpoolCapacityException();
            var temporary = target + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, target);
        }
    }

    public LedgerEvent[] Pending(int limit = LedgerJson.MaximumBatchEvents) {
        lock (_gate) {
            var pending = new List<LedgerEvent>();
            var size = 2; // JSON array brackets and one comma between each pair of events.
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal).Take(Math.Clamp(limit, 1, LedgerJson.MaximumBatchEvents))) {
                var observation = JsonSerializer.Deserialize<LedgerEvent>(File.ReadAllText(file), LedgerJson.Options)!;
                var length = JsonSerializer.SerializeToUtf8Bytes(observation, LedgerJson.Options).Length;
                if (size + length + (pending.Count > 0 ? 1 : 0) > LedgerJson.MaximumBatchBytes) {
                    if (pending.Count == 0) throw new InvalidDataException("Retained event exceeds the ingress byte bound.");
                    break;
                }
                size += length + (pending.Count > 0 ? 1 : 0);
                pending.Add(observation);
            }
            return pending.ToArray();
        }
    }

    public void Acknowledge(IEnumerable<Guid> ids) {
        lock (_gate) foreach (var id in ids) {
            foreach (var file in Directory.EnumerateFiles(_directory, "*-" + id.ToString("N") + ".json")) File.Delete(file);
        }
    }

    public void Dispose() => _ownership?.Dispose();
}

internal sealed class SpoolCapacityException : IOException;
