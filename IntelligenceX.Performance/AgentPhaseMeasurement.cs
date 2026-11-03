using System.Diagnostics;
using System.Runtime.InteropServices;
using IntelligenceX.Telemetry.Agents;

// Windows diagnostic only. CPU is measured on the synchronous collection thread, not inferred
// from wall time or attributed from another process. Nested phases report exclusive CPU.
internal sealed class AgentPhaseMeasurement : IDisposable {
    private readonly object _gate = new();
    private readonly Dictionary<string, (double Cpu, double Wall, long Calls)> _phases = new();
    private readonly ThreadLocal<Stack<Scope>> _stack = new(static () => new Stack<Scope>());
    private int _active;

    internal AgentPhaseMeasurement() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Thread CPU measurement currently requires Windows.");
        AgentWorkMeasurement.Measure = phase => new Scope(this, phase);
        AgentWorkMeasurement.Count = (counter, value) => { lock (_gate) {
            _phases.TryGetValue(counter, out var previous);
            _phases[counter] = (previous.Cpu, previous.Wall, previous.Calls + value);
        }};
    }

    internal Dictionary<string, (double Cpu, double Wall, long Calls)> Snapshot() { lock (_gate) return new(_phases); }

    internal async Task WhenIdleAsync() {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (Volatile.Read(ref _active) != 0) await Task.Delay(5, deadline.Token);
    }

    internal void Report(Dictionary<string, (double Cpu, double Wall, long Calls)> before,
        Dictionary<string, (double Cpu, double Wall, long Calls)> after, double wallSeconds) {
        foreach (var pair in after.OrderBy(static pair => pair.Key, StringComparer.Ordinal)) {
            before.TryGetValue(pair.Key, out var initial);
            Console.WriteLine(FormattableString.Invariant($"phase={pair.Key} cpuSecondsPerMinute={(pair.Value.Cpu-initial.Cpu)*60/wallSeconds:F4} wallSeconds={pair.Value.Wall-initial.Wall:F4} count={pair.Value.Calls-initial.Calls}"));
        }
    }

    public void Dispose() { AgentWorkMeasurement.Measure = null; AgentWorkMeasurement.Count = null; _stack.Dispose(); }

    private sealed class Scope : IDisposable {
        private readonly AgentPhaseMeasurement _owner;
        private readonly string _phase;
        private readonly double _cpu = ReadCpu();
        private readonly long _wall = Stopwatch.GetTimestamp();
        private double _children;
        internal Scope(AgentPhaseMeasurement owner, string phase) { _owner = owner; _phase = phase; Interlocked.Increment(ref owner._active); owner._stack.Value!.Push(this); }
        public void Dispose() {
            var cpu = ReadCpu() - _cpu;
            var wall = Stopwatch.GetElapsedTime(_wall).TotalSeconds;
            var stack = _owner._stack.Value!;
            stack.Pop();
            if (stack.TryPeek(out var parent)) parent._children += cpu;
            lock (_owner._gate) {
                _owner._phases.TryGetValue(_phase, out var previous);
                _owner._phases[_phase] = (previous.Cpu + Math.Max(0, cpu - _children), previous.Wall + wall, previous.Calls + 1);
            }
            Interlocked.Decrement(ref _owner._active);
        }
    }

    private static double ReadCpu() {
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user)) throw new InvalidOperationException("Thread CPU clock unavailable.");
        return (kernel + user) / 10000000.0;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
}
