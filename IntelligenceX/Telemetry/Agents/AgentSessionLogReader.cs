using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

internal static class AgentSessionLogReader {
    // Tool payloads are not needed for state. Bound a malformed or oversized line independently of file size.
    private const int MaximumLineCharacters = 1024 * 1024;

    internal static IEnumerable<string> ReadLines(string path, CancellationToken cancellationToken) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var remaining = stream.Length;
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var oversized = false;
        while (remaining > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var count = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) break;
            remaining -= count;
            for (var i = 0; i < count; i++) {
                if (buffer[i] == '\n') {
                    if (!oversized) yield return line.ToString();
                    line.Clear();
                    oversized = false;
                } else if (!oversized) {
                    if (line.Length >= MaximumLineCharacters) {
                        line.Clear();
                        oversized = true;
                    } else {
                        line.Append(buffer[i]);
                    }
                }
            }
        }
        // A final unterminated record may be in flight; a complete JSON object can still be read safely.
        if (!oversized && line.Length > 0) yield return line.ToString();
    }

    internal static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static JsonElement Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    internal static DateTimeOffset? Timestamp(JsonElement element, string property = "timestamp") =>
        DateTimeOffset.TryParse(Text(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var time) ? time.ToUniversalTime() : null;

    internal static AgentSessionState Finish(AgentSessionState session, bool active, DateTimeOffset now,
        TimeSpan activeWindow, SourceRootRecord root) {
        if (session.IsArchived) return session with { State = AgentWorkState.Finished, StateIsInferred = false };
        if (!active) return session;
        var recent = session.LastActivityAtUtc.HasValue && session.LastActivityAtUtc.Value <= now
            && now - session.LastActivityAtUtc.Value <= activeWindow;
        // Recovered/remote logs cannot establish that a process is running on this machine.
        recent &= root.SourceKind == UsageSourceKind.LocalLogs;
        return session with {
            State = recent ? AgentWorkState.Running : AgentWorkState.Idle,
            StateIsInferred = true,
            LastTurnOutcome = recent ? AgentTurnOutcome.Unknown : AgentTurnOutcome.Incomplete
        };
    }
}
