using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo RunPhaseProgressLoopAsyncMethod =
        typeof(ChatServiceSession).GetMethod("RunPhaseProgressLoopAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RunPhaseProgressLoopAsync not found.");

    [Fact]
    public async Task PhaseProgressLoop_EmitsPlanExecuteReviewInOrder() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        using var memory = new MemoryStream();
        using var writer = new StreamWriter(memory, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokePhaseProgressLoopAsync(session, writer, "phase_plan", "Planning...", "Planning", 0, Task.CompletedTask);
        await InvokePhaseProgressLoopAsync(session, writer, "phase_execute", "Executing...", "Executing", 0, Task.CompletedTask);
        await InvokePhaseProgressLoopAsync(session, writer, "phase_review", "Reviewing...", "Reviewing", 0, Task.CompletedTask);

        var statuses = ParseStatuses(memory);
        Assert.Equal(new[] { "phase_plan", "phase_execute", "phase_review" }, statuses);
    }

    [Fact]
    public async Task PhaseProgressLoop_EmitsHeartbeatForLongRunningPhase() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        using var memory = new MemoryStream();
        using var writer = new StreamWriter(memory, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var invokeTask = InvokePhaseProgressLoopAsync(session, writer, "phase_review", "Reviewing...", "Reviewing response", 1, completion.Task);
        await WaitForStatusAsync(memory, "phase_heartbeat", TimeSpan.FromSeconds(5));
        completion.TrySetResult(null);
        await invokeTask;

        var statuses = ParseStatuses(memory);
        Assert.Contains("phase_review", statuses);
        Assert.Contains("phase_heartbeat", statuses);
        var reviewIndex = statuses.IndexOf("phase_review");
        var heartbeatIndex = statuses.IndexOf("phase_heartbeat");
        Assert.True(reviewIndex >= 0 && heartbeatIndex > reviewIndex);
    }

    private static async Task InvokePhaseProgressLoopAsync(ChatServiceSession session, StreamWriter writer, string phaseStatus, string phaseMessage,
        string heartbeatLabel, int heartbeatSeconds, Task phaseTask) {
        var args = new object?[] {
            writer,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            CancellationToken.None,
            phaseTask
        };

        var invoked = RunPhaseProgressLoopAsyncMethod.Invoke(session, args);
        var task = Assert.IsAssignableFrom<Task>(invoked);
        await task;
    }

    private static List<string> ParseStatuses(MemoryStream stream) {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var statuses = new List<string>();
        while (!reader.EndOfStream) {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "type", out var typeEl)
                || !string.Equals(typeEl.GetString(), "chat_status", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryGetPropertyIgnoreCase(root, "status", out var statusEl)) {
                var status = statusEl.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(status)) {
                    statuses.Add(status.Trim());
                }
            }
        }

        return statuses;
    }

    private static async Task WaitForStatusAsync(MemoryStream stream, string status, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            var statuses = ParseStatuses(stream);
            if (statuses.Contains(status, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"Timed out waiting for status '{status}'.");
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value) {
        foreach (var property in obj.EnumerateObject()) {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
