// Minimal interoperability wire records adapted from EvotecIT/Marquee.
// Source https://github.com/EvotecIT/Marquee/blob/c365e7b081f0da696d3873b3b6df59cf5160c98b/Sources/Marquee.Probe.Contracts/ProbeSnapshot.cs
// Commit c365e7b081f0da696d3873b3b6df59cf5160c98b. Only consumed fields are retained.

namespace IntelligenceX.Ledger.Reporting.Probe;

internal sealed record ProbeSnapshot {
    public int Version { get; init; }
    public DateTimeOffset At { get; init; }
    public bool Paused { get; init; }
    public DateTimeOffset? PausedUntil { get; init; }
    public ProbeCpu? Cpu { get; init; }
    public ProbeMemory? Memory { get; init; }
    public IReadOnlyList<ProbeVolume>? Volumes { get; init; }
    public ProbeActivity? Activity { get; init; }
}

internal sealed record ProbeCpu {
    public double? Use { get; init; }
    public IReadOnlyList<double>? LoadAverage { get; init; }
}

internal sealed record ProbeMemory {
    public long? Used { get; init; }
    public long? Total { get; init; }
    public long? SwapUsed { get; init; }
    public string? Pressure { get; init; }
}

internal sealed record ProbeVolume(string Mount, long Free, long Total);

internal sealed record ProbeActivity {
    public bool? RunnerListening { get; init; }
    public bool? RunnerBusy { get; init; }
}

internal sealed record ProbeInfo(int Version);
internal sealed record ProbePairRequest(string Reader);
internal sealed record ProbePairTicket(string Id);
internal sealed record ProbePairState(string State, string? Token);
