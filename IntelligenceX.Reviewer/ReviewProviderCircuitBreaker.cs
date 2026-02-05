using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal static class ReviewProviderCircuitBreaker {
    private sealed class CircuitState {
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? OpenUntilUtc { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<ReviewProvider, CircuitState> States = new();

    public static bool IsOpen(ReviewProvider provider, DateTimeOffset nowUtc, out TimeSpan remaining) {
        lock (Gate) {
            if (!States.TryGetValue(provider, out var state) || !state.OpenUntilUtc.HasValue) {
                remaining = TimeSpan.Zero;
                return false;
            }
            var openUntil = state.OpenUntilUtc.Value;
            if (openUntil <= nowUtc) {
                state.OpenUntilUtc = null;
                state.ConsecutiveFailures = 0;
                remaining = TimeSpan.Zero;
                return false;
            }
            remaining = openUntil - nowUtc;
            return true;
        }
    }

    public static void RecordFailure(ReviewProvider provider, int failureThreshold, TimeSpan openDuration, DateTimeOffset nowUtc) {
        if (failureThreshold <= 0) {
            return;
        }
        lock (Gate) {
            var state = GetOrCreate(provider);
            if (state.OpenUntilUtc.HasValue && state.OpenUntilUtc.Value > nowUtc) {
                return;
            }
            state.ConsecutiveFailures++;
            if (state.ConsecutiveFailures >= failureThreshold) {
                state.OpenUntilUtc = nowUtc + openDuration;
            }
        }
    }

    public static void RecordSuccess(ReviewProvider provider) {
        lock (Gate) {
            if (!States.TryGetValue(provider, out var state)) {
                return;
            }
            state.ConsecutiveFailures = 0;
            state.OpenUntilUtc = null;
        }
    }

    public static void Reset() {
        lock (Gate) {
            States.Clear();
        }
    }

    private static CircuitState GetOrCreate(ReviewProvider provider) {
        if (!States.TryGetValue(provider, out var state)) {
            state = new CircuitState();
            States[provider] = state;
        }
        return state;
    }
}
