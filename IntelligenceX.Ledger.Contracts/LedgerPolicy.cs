using System.Text.Json;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Automatic lifecycle settings apply to sessions with confirmed agent origin.</summary>
public sealed record SessionPolicy(double IdleArchiveHours = 48, int FreshAfterTurns = 40,
    double FreshAfterDays = 3, double CleanupIntervalHours = 24);

/// <summary>Reservation durations and liveness windows, expressed in minutes unless named otherwise.</summary>
public sealed record ResourceLeasePolicy(int DefaultTtlSeconds = 600, double TakeoverIdleMinutes = 30,
    double TakeoverIdleMinutesHuman = 1440, double LiveWithinMinutes = 30, double QueueTtlMinutes = 60) {
    public Dictionary<string, double> MaximumTtlMinutes { get; set; } = new(StringComparer.Ordinal) {
        ["bench"] = 120, ["build"] = 60, ["test"] = 60, ["branch"] = 480, ["machine"] = 120,
        ["other"] = 1440
    };
}

/// <summary>Benchmark reservation budgets use elapsed reservation time in a rolling daily window.</summary>
public sealed record BenchmarkPolicy(double MaxPerDayHours = 4, double MaxContinuousMinutes = 120,
    int HeavySlotsWhileActive = 0, double ContinuityGapMinutes = 1);

/// <summary>Load decisions use fresh Probe observations and the configured persistence windows.</summary>
public sealed record MachineLoadPolicy(double HeavyCpuPercent = 85, double HeavySustainedMinutes = 2,
    double AlertCpuPercent = 95, double AlertMinutes = 15, double SampleFreshMinutes = 2) {
    public string[] HeavyMemoryPressure { get; set; } = ["warning", "critical"];
}

/// <summary>One changed policy value. The containing change supplies the editor and timestamp.</summary>
public sealed record PolicyValueChange(string Path, JsonElement OldValue, JsonElement NewValue);

/// <summary>Updates one policy path against an exact existing revision.</summary>
public sealed record PolicyValueCommand(string Path, JsonElement Value, long ExpectedRevision);
