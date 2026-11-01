using System.Diagnostics;
using IntelligenceX.Codex;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;

internal static class AgentRealWorkload {
    internal static async Task RunAsync(string[] args) {
        if (args.Length < 1) throw new ArgumentException("Supply a task-owned scratch directory.");
        var seconds = args.Length > 1 ? int.Parse(args[1]) : 120;
        var reconcileSeconds = args.Length > 2 ? int.Parse(args[2]) : 30;
        if (seconds < 30 || reconcileSeconds < 1) throw new ArgumentOutOfRangeException(nameof(args));
        var directory = Path.Combine(Path.GetFullPath(args[0]), "real-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var cache = new SqliteRawArtifactStore(Path.Combine(directory, "observations.db"));
        var roots = LocalAgentWorkStateService.DiscoverRoots();
        using var phases = new AgentPhaseMeasurement();
        long events = 0;
        await using var watcher = new AgentWorkWatcher(roots, new AgentWorkOptions {
            ActiveWithin = TimeSpan.FromDays(2), StaleAfter = TimeSpan.FromDays(5), Cache = cache,
            ReconcileInterval = TimeSpan.FromSeconds(reconcileSeconds)
        }, readMetadataAsync: async token => {
            var rows = new List<AgentSessionState>();
            foreach (var root in roots.Where(static root => root.Enabled && root.ProviderId == "codex"))
                rows.AddRange(await CodexAgentSessionMetadataReader.ReadAsync(root, token));
            return rows;
        });
        watcher.SnapshotChanged += (_, _) => Interlocked.Increment(ref events);
        var startup = Stopwatch.StartNew();
        await watcher.StartAsync();
        startup.Stop();
        Console.WriteLine(FormattableString.Invariant($"realRoots={roots.Count} sessions={watcher.CurrentSnapshot.Sessions.Count} startupSeconds={startup.Elapsed.TotalSeconds:F3} processId={Environment.ProcessId}"));
        await Task.Delay(TimeSpan.FromSeconds(5));
        await phases.WhenIdleAsync();
        using var process = Process.GetCurrentProcess();
        var before = phases.Snapshot();
        var beforeEvents = Interlocked.Read(ref events);
        var cpu = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await phases.WhenIdleAsync();
        wall.Stop();
        var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
        var after = phases.Snapshot();
        Console.WriteLine(FormattableString.Invariant($"mode=real wallSeconds={wall.Elapsed.TotalSeconds:F3} cpuSecondsPerMinute={cpuSeconds*60/wall.Elapsed.TotalSeconds:F4} oneCorePercent={cpuSeconds*100/wall.Elapsed.TotalSeconds:F3} events={Interlocked.Read(ref events)-beforeEvents} sessions={watcher.CurrentSnapshot.Sessions.Count} reconcileSeconds={reconcileSeconds}"));
        phases.Report(before, after, wall.Elapsed.TotalSeconds);
        Console.WriteLine("diagnostics=" + string.Join(",", watcher.CurrentSnapshot.Diagnostics.Select(static row => row.Provider + ":" + row.Code + ":" + row.Count)));
        Console.WriteLine("privateCache=" + directory);
        // Only aggregate counts are printed. No IDs, titles, paths from roots, exceptions or JSON.
    }
}
