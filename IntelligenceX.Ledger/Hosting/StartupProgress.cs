using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>
/// Writes the service's startup phase to <c>service-startup.json</c> in the data directory and to standard output.
/// The supervisor reads the file to tell a slow but moving start from a stuck one: it waits while the phase or its
/// progress changes, and treats a start that stops moving like any other unhealthy service.
/// </summary>
internal sealed class StartupProgress(string dataDirectory) : IProgress<StartupStep> {
    internal const string FileName = "service-startup.json";
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly object _sync = new();

    public void Report(StartupStep value) => Write(value, ready: false);

    public void Ready() => Write(new("ready"), ready: true);

    private void Write(StartupStep step, bool ready) {
        var now = DateTimeOffset.UtcNow;
        var record = new {
            schemaVersion = 1, pid = Environment.ProcessId, startedAtUtc = _started, phase = step.Phase,
            done = step.Done, total = step.Total, ready, atUtc = now
        };
        lock (_sync) {
            Console.Out.WriteLine($"{now:O} startup {step.Phase}" + (step.Total is { } total ? $" {step.Done}/{total}" : ""));
            var path = Path.Combine(dataDirectory, FileName);
            var temporary = path + "." + Environment.ProcessId + ".tmp";
            try {
                File.WriteAllText(temporary, JsonSerializer.Serialize(record, LedgerJson.Options));
                File.Move(temporary, path, overwrite: true);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                // Progress is advisory; a locked or full disk must not stop the service from starting.
                try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
