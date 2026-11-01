using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;

// Opt-in live workload diagnostic, not a microbenchmark or a correctness CI gate.
// Each launch owns a new child of the supplied scratch directory. No provider discovery or network.
if (args.Length < 1) throw new ArgumentException("Usage: <scratch-directory> [seconds=60] [files=1400] [control]");
var seconds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 60;
var fileCount = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1400;
if (seconds < 5 || fileCount < 4) throw new ArgumentOutOfRangeException(nameof(args));
var control = args.Contains("control");
var directory = Path.Combine(Path.GetFullPath(args[0]), "agent-workload-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var codex = Path.Combine(directory, "codex");
var claude = Path.Combine(directory, "claude");
var cwd = Path.Combine(directory, "repository");
Directory.CreateDirectory(Path.Combine(cwd, ".git"));
File.WriteAllText(Path.Combine(cwd, ".git", "HEAD"), "ref: refs/heads/workload\n");
File.WriteAllText(Path.Combine(cwd, ".git", "config"), "[remote \"origin\"]\nurl = https://github.com/example/workload.git\n");
var utf8 = new UTF8Encoding(false);
var paths = new string[fileCount];
var cwdJson = cwd.Replace("\\", "\\\\");
for (var i = 0; i < fileCount; i++) {
    var isCodex = i % 2 == 0;
    var parent = isCodex ? Path.Combine(codex, "sessions", "2026", "11", "01") : Path.Combine(claude, "projects", "fixture");
    Directory.CreateDirectory(parent);
    var path = paths[i] = Path.Combine(parent, (isCodex ? "rollout-" : "") + "synthetic-" + i + ".jsonl");
    var time = DateTimeOffset.UtcNow.ToString("O");
    var line = isCodex
        ? $"{{\"type\":\"session_meta\",\"timestamp\":\"{time}\",\"payload\":{{\"id\":\"synthetic-{i}\",\"cwd\":\"{cwdJson}\"}}}}\n"
        : $"{{\"type\":\"user\",\"timestamp\":\"{time}\",\"sessionId\":\"synthetic-{i}\",\"cwd\":\"{cwdJson}\"}}\n";
    File.WriteAllText(path, line + new string(' ', 4096) + "\n", utf8);
}
using var cache = new SqliteRawArtifactStore(Path.Combine(directory, "observations.db"));
var roots = new[] { new SourceRootRecord("synthetic-codex", "codex", UsageSourceKind.LocalLogs, codex),
    new SourceRootRecord("synthetic-claude", "claude", UsageSourceKind.LocalLogs, claude) };
await using var watcher = new AgentWorkWatcher(roots, new AgentWorkOptions { Cache = cache, ActiveWithin = TimeSpan.FromDays(2) });
var pending = Enumerable.Range(0, 4).Select(static _ => new ConcurrentQueue<(long Sequence, long Started)>()).ToArray();
var latencies = new ConcurrentBag<double>();
var probe = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
var produced = new long[4];
var observedSequences = new long[4];
long events = 0, appends = 0;
watcher.SnapshotChanged += (_, e) => {
    Interlocked.Increment(ref events);
    foreach (var session in e.Snapshot.Sessions) {
        if (session.Title == "latency-probe") probe.TrySetResult(Stopwatch.GetTimestamp());
        if (session.Title is null) continue;
        var parts = session.Title.Split('-');
        if (parts.Length != 3 || parts[0] != "writer") continue;
        var writer = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var sequence = long.Parse(parts[2], CultureInfo.InvariantCulture);
        Interlocked.Exchange(ref observedSequences[writer], sequence);
        long first = 0;
        while (pending[writer].TryPeek(out var item) && item.Sequence <= sequence && pending[writer].TryDequeue(out item)) {
            if (first == 0) first = item.Started;
        }
        if (first != 0) {
            latencies.Add(Stopwatch.GetElapsedTime(first).TotalMilliseconds);
        }
    }
};
var startup = Stopwatch.StartNew();
if (!control) {
    await watcher.StartAsync();
    if (watcher.CurrentSnapshot.Sessions.Count != fileCount) throw new InvalidOperationException("Fixture collection incomplete.");
}
startup.Stop();
using var stop = new CancellationTokenSource();
var writers = Enumerable.Range(0, 4).Select(i => Task.Run(async () => {
    long sequence = 0;
    try {
        while (true) {
            var title = $"writer-{i}-{++sequence}";
            var time = DateTimeOffset.UtcNow.ToString("O");
            var line = i % 2 == 0
                ? $"{{\"type\":\"event_msg\",\"timestamp\":\"{time}\",\"payload\":{{\"type\":\"thread_name_updated\",\"thread_name\":\"{title}\"}}}}\n"
                : $"{{\"type\":\"custom-title\",\"timestamp\":\"{time}\",\"sessionId\":\"synthetic-{i}\",\"customTitle\":\"{title}\"}}\n";
            if (!control) pending[i].Enqueue((sequence, Stopwatch.GetTimestamp()));
            File.AppendAllText(paths[i], line, utf8);
            Interlocked.Exchange(ref produced[i], sequence);
            Interlocked.Increment(ref appends);
            await Task.Delay(200, stop.Token);
        }
    } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
})).ToArray();
await Task.Delay(TimeSpan.FromSeconds(5)); // Settle startup, JIT and initial writer activity outside measurement.
latencies.Clear();
var initialEvents = Interlocked.Read(ref events);
var initialAppends = Interlocked.Read(ref appends);
using var process = Process.GetCurrentProcess();
var cpu = process.TotalProcessorTime;
var measured = Stopwatch.StartNew();
await Task.Delay(TimeSpan.FromSeconds(seconds));
var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
measured.Stop();
stop.Cancel();
await Task.WhenAll(writers);
var measuredEvents = Interlocked.Read(ref events) - initialEvents;
double probeMs = 0;
if (!control) {
    if (produced.Any(static sequence => sequence == 0)) throw new InvalidOperationException("A synthetic writer produced no updates.");
    using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (Enumerable.Range(0, 4).Any(i => Interlocked.Read(ref observedSequences[i]) < Interlocked.Read(ref produced[i])))
        await Task.Delay(20, drain.Token);
    var started = Stopwatch.GetTimestamp();
    File.AppendAllText(paths[0], $"{{\"type\":\"event_msg\",\"timestamp\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{{\"type\":\"thread_name_updated\",\"thread_name\":\"latency-probe\"}}}}\n", utf8);
    var observed = await probe.Task.WaitAsync(TimeSpan.FromSeconds(10));
    probeMs = Stopwatch.GetElapsedTime(started, observed).TotalMilliseconds;
}
var samples = latencies.OrderBy(static value => value).ToArray();
Console.WriteLine(FormattableString.Invariant($"singleAppendMs={probeMs:F1}"));
Console.WriteLine("producedSequences=" + string.Join(",", produced) + " observedSequences=" + string.Join(",", observedSequences));
Console.WriteLine(FormattableString.Invariant($"mode={(control ? "control" : "watcher")} files={fileCount} writers=4 writesPerSecond=20 startupSeconds={startup.Elapsed.TotalSeconds:F3} wallSeconds={measured.Elapsed.TotalSeconds:F3} cpuSeconds={cpuSeconds:F4} cpuSecondsPerMinute={cpuSeconds * 60 / measured.Elapsed.TotalSeconds:F4} oneCorePercent={cpuSeconds * 100 / measured.Elapsed.TotalSeconds:F3} appends={appends-initialAppends} events={measuredEvents} observedUpdates={samples.Length} p50Ms={(samples.Length == 0 ? 0 : samples[samples.Length / 2]):F1} p95Ms={(samples.Length == 0 ? 0 : samples[(int)((samples.Length-1)*.95)]):F1} maxMs={(samples.Length == 0 ? 0 : samples[^1]):F1}"));
Console.WriteLine("syntheticOutput=" + directory);
Console.WriteLine($"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} os={System.Runtime.InteropServices.RuntimeInformation.OSDescription} logicalProcessors={Environment.ProcessorCount} priority={process.PriorityClass}");
if (!control && (samples.Length == 0 || watcher.CurrentSnapshot.Sessions.Count != fileCount))
    throw new InvalidOperationException("Workload did not produce validated updates.");
