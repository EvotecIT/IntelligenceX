using System.Text;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>One closed metadata contract for all command surfaces.</summary>
public static class CoordinationValidation {
    public static bool Id(string? value) => DeclarationValidation.Identifier(value);
    internal static bool Providers(string[]? providers) => providers is null || providers.Length <= 64 &&
        providers.All(Id) && providers.Distinct(StringComparer.Ordinal).Count() == providers.Length;
    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string reason) {
        if (!condition) throw new CoordinationConflictException(reason);
    }
    public static bool Demand(ResourceDemand d) => d.AgentSlots >= 0 && d.BuildSlots >= 0 && d.CpuUnits >= 0 &&
        d.MemoryMiB >= 0 && d.AgentSlots + d.BuildSlots + d.CpuUnits + d.MemoryMiB > 0 &&
        (d.Processors is null || d.Processors.Length > 0 && d.Processors.All(p => p >= 0) && d.Processors.Distinct().Count() == d.Processors.Length);

    public static void Policy(CoordinationPolicy p) {
        Require(p is not null, "invalid-policy");
        Require(p.Revision > 0 && p.Machines is { Length: > 0 and <= 128 } && p.Machines.All(m => m is not null) && p.MaximumQueued is > 0 and <= 10000 &&
            p.GrantSeconds is >= 15 and <= 3600, "invalid-policy");
        Require(p.Machines.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == p.Machines.Length, "duplicate-machine");
        foreach (var m in p.Machines) Require(m is not null && Id(m.Id) && m.AgentSlots >= 0 && m.BuildSlots >= 0 && m.TestSlots is not < 0 && m.CpuUnits > 0 && m.MemoryMiB > 0 &&
            m.Capabilities is { Length: > 0 and <= 64 } && m.Capabilities.All(Id) && Providers(m.AllowedCreateProviders) &&
            (m.BenchmarkProcessors is null || m.BenchmarkProcessors.Length > 0 && m.BenchmarkProcessors.All(p => p >= 0) &&
             m.BenchmarkProcessors.Distinct().Count() == m.BenchmarkProcessors.Length), "invalid-machine-policy");
        Require(p.Session is not null && Positive(p.Session.IdleArchiveHours) && p.Session.FreshAfterTurns > 0 &&
            Positive(p.Session.FreshAfterDays) && Positive(p.Session.CleanupIntervalHours), "invalid-session-policy");
        Require(p.Lease is not null && p.Lease.DefaultTtlSeconds > 0 && Nonnegative(p.Lease.TakeoverIdleMinutes) &&
            Nonnegative(p.Lease.TakeoverIdleMinutesHuman) && Positive(p.Lease.LiveWithinMinutes) && Positive(p.Lease.QueueTtlMinutes) &&
            p.Lease.MaximumTtlMinutes is { Count: > 0 and <= 64 } && p.Lease.MaximumTtlMinutes.ContainsKey("other") &&
            p.Lease.MaximumTtlMinutes.All(pair => Id(pair.Key) && pair.Key == pair.Key.ToLowerInvariant() && Positive(pair.Value) && pair.Value * 60 >= 1), "invalid-lease-policy");
        Require(p.Bench is not null && Positive(p.Bench.MaxPerDayHours) && p.Bench.MaxPerDayHours <= 24 &&
            Positive(p.Bench.MaxContinuousMinutes) && p.Bench.HeavySlotsWhileActive >= 0 && Nonnegative(p.Bench.ContinuityGapMinutes), "invalid-benchmark-policy");
        Require(p.Load is not null && p.Load.HeavyCpuPercent is >= 0 and <= 100 && p.Load.AlertCpuPercent is >= 0 and <= 100 &&
            Nonnegative(p.Load.HeavySustainedMinutes) && Positive(p.Load.AlertMinutes) && Positive(p.Load.SampleFreshMinutes) &&
            p.Load.HeavyMemoryPressure is { Length: <= 16 } && p.Load.HeavyMemoryPressure.All(Id), "invalid-load-policy");
        Require(p.Routing is { Count: > 0 and <= 64 } && p.Routing.All(pair => Id(pair.Key) &&
            pair.Value is { Length: > 0 and <= 64 } && Providers(pair.Value)), "invalid-routing-policy");
        Require(p.Priority is { Count: > 0 and <= 64 } && p.Priority.All(pair => Id(pair.Key) &&
            pair.Value is "low" or "normal" or "high"), "invalid-priority-policy");
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;

    public static void Request(CoordinationRequest r, CoordinationPrincipal caller) {
        var c = r.Checkpoint;
        Require(Id(r.Id) && Id(r.GoalId) && Id(r.Capability) && Id(r.AuthorizationRef) && r.ControlRevision > 0 &&
            r.Kind is "build" or "benchmark" or "agent" or "handoff" or "help" or "review" && Demand(r.Resources) &&
            (r.RequiredProvider is null || Id(r.RequiredProvider)) &&
            (r.MachineId is null || Id(r.MachineId)) && (r.WorkerId is null || Id(r.WorkerId)), "invalid-request");
        Require(r.TaskBrief is null || !string.IsNullOrWhiteSpace(r.TaskBrief) &&
            Encoding.UTF8.GetByteCount(r.TaskBrief) <= 8192, "invalid-task-brief");
        Checkpoint(c);
        Require(caller.Projects.Contains(c!.Project, StringComparer.Ordinal), "project-not-authorized");
        Require(r.Dependencies is { Length: <= 64 } && r.Dependencies.All(Id) && !r.Dependencies.Contains(r.Id) &&
            r.Dependencies.Distinct().Count() == r.Dependencies.Length, "invalid-dependencies");
        Require(r.Kind is not ("agent" or "handoff") || r.WorkerId is not null && r.Resources.AgentSlots > 0, "worker-required");
        Require(r.Kind is not ("help" or "review") || r.ReturnTo is not null && r.Resources.AgentSlots > 0, "return-binding-required");
        Require(!r.ReuseSession || r.Kind is "help" or "review" &&
            (Id(r.WorkerId) && Id(r.MachineId) || r.Kind == "review" && r.ReadOnly && Id(r.RequiredProvider)),
            "named-session-required");
        Require(r.Kind != "review" || r.ReadOnly, "review-must-be-read-only");
        Require(r.Kind != "benchmark" || r.Resources.Exclusive || r.Resources.Processors is { Length: > 0 }, "benchmark-isolation-required");
    }

    /// <summary>Validates portable source identity and recovery references before enrollment or admission.</summary>
    public static void Checkpoint(WorkCheckpoint? c) =>
        Require(c is not null && Id(c.Project) && c.Branch is { Length: > 0 and <= 200 } && !c.Branch.Any(char.IsControl) &&
            c.Commit is { Length: 40 or 64 } && c.Commit.All(Uri.IsHexDigit) && Id(c.CheckpointRef) && Id(c.NextActionRef) &&
            (c.PredecessorRequest is null || Id(c.PredecessorRequest)) &&
            (c.PullRequest is null || Uri.TryCreate(c.PullRequest, UriKind.Absolute, out var pr) && pr.Scheme == "https" && pr.UserInfo.Length == 0),
            "invalid-checkpoint");
}
