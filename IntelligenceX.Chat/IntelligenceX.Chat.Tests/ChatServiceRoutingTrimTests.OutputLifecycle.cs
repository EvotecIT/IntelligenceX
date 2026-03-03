using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo WriteAsyncMethod =
        typeof(ChatServiceSession).GetMethod("WriteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("WriteAsync not found.");

    [Fact]
    public async Task WriteAsync_DeduplicatesIdenticalFinalResultsForSameRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

        var message = new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req-dup",
            ThreadId = "thread-1",
            Text = "Done."
        };

        await InvokeWriteAsync(session, writer, message);
        await InvokeWriteAsync(session, writer, message);

        var lines = ReadJsonLines(stream);
        Assert.Single(lines);
    }

    [Fact]
    public async Task WriteAsync_DoesNotDeduplicateWhenFinalTextChanges() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

        await InvokeWriteAsync(session, writer, new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req-nondup",
            ThreadId = "thread-1",
            Text = "First"
        });

        await InvokeWriteAsync(session, writer, new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req-nondup",
            ThreadId = "thread-1",
            Text = "Second"
        });

        var lines = ReadJsonLines(stream);
        Assert.Equal(2, lines.Count);
    }

    private static async Task InvokeWriteAsync(ChatServiceSession session, StreamWriter writer, ChatServiceMessage message) {
        var taskObj = WriteAsyncMethod.Invoke(session, new object?[] { writer, message, CancellationToken.None });
        var task = Assert.IsAssignableFrom<Task>(taskObj);
        await task;
        await writer.FlushAsync();
    }

    private static IReadOnlyList<string> ReadJsonLines(MemoryStream stream) {
        var payload = Encoding.UTF8.GetString(stream.ToArray());
        using var reader = new StringReader(payload);
        var lines = new List<string>();
        while (true) {
            var line = reader.ReadLine();
            if (line is null) {
                break;
            }

            if (line.Length > 0) {
                lines.Add(line);
            }
        }

        return lines;
    }
}
