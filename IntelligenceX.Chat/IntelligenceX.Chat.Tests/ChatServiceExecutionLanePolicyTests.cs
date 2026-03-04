using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceExecutionLanePolicyTests {
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(32, 32)]
    public void ResolveSessionExecutionQueueLimit_ClampsNegativeValues(int configuredLimit, int expected) {
        var resolved = ChatServiceSession.ResolveSessionExecutionQueueLimit(configuredLimit);

        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    public void ResolveGlobalExecutionLaneConcurrency_ClampsNegativeValues(int configuredConcurrency, int expected) {
        var resolved = ChatServiceSession.ResolveGlobalExecutionLaneConcurrency(configuredConcurrency);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task CancelActiveChatIfAnyAsync_ClearsQueuedRunIdsFromIndex() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var sessionType = typeof(ChatServiceSession);
        var chatRunType = sessionType.GetNestedType("ChatRun", BindingFlags.NonPublic);
        Assert.NotNull(chatRunType);

        object CreateRun(string requestId) {
            return Activator.CreateInstance(
                chatRunType!,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                args: new object?[] {
                    requestId,
                    new CancellationTokenSource(),
                    null,
                    new StreamWriter(Stream.Null),
                    new ChatRequest {
                        RequestId = requestId,
                        Text = "ping"
                    }
                },
                culture: null)!;
        }

        var activeRun = CreateRun("req-active");
        var queuedRun = CreateRun("req-queued");

        var queueField = sessionType.GetField("_queuedChats", BindingFlags.NonPublic | BindingFlags.Instance);
        var mapField = sessionType.GetField("_chatRunsByRequestId", BindingFlags.NonPublic | BindingFlags.Instance);
        var activeField = sessionType.GetField("_activeChat", BindingFlags.NonPublic | BindingFlags.Instance);
        var cancelMethod = sessionType.GetMethod("CancelActiveChatIfAnyAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(queueField);
        Assert.NotNull(mapField);
        Assert.NotNull(activeField);
        Assert.NotNull(cancelMethod);

        var queue = queueField!.GetValue(session);
        var map = mapField!.GetValue(session);
        Assert.NotNull(queue);
        Assert.NotNull(map);

        queue!.GetType().GetMethod("Enqueue")!.Invoke(queue, new[] { queuedRun });
        var addMethod = map!.GetType().GetMethod("Add");
        Assert.NotNull(addMethod);
        addMethod!.Invoke(map, new[] { "req-active", activeRun });
        addMethod.Invoke(map, new[] { "req-queued", queuedRun });
        activeField!.SetValue(session, activeRun);

        var cancelTask = cancelMethod!.Invoke(session, Array.Empty<object>());
        var typedTask = Assert.IsAssignableFrom<Task>(cancelTask);
        await typedTask;

        var countProperty = map.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);
        Assert.Equal(0, (int)countProperty!.GetValue(map)!);

        var tryAddMethod = map.GetType().GetMethod("TryAdd");
        Assert.NotNull(tryAddMethod);
        var canAddQueuedIdAgain = (bool)tryAddMethod!.Invoke(map, new[] { "req-queued", queuedRun })!;
        Assert.True(canAddQueuedIdAgain);
    }
}
