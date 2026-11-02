using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace IntelligenceX.Telemetry.Agents;

internal static class AgentSessionLogReader {
    // Tool payloads are not needed for state. Bound a malformed or oversized line independently of file size.
    private const int MaximumLineCharacters = 1024 * 1024;

    internal static IEnumerable<string> ReadLines(string path, CancellationToken cancellationToken, AgentSessionReadWindow? window = null) {
        window ??= new AgentSessionReadWindow();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = window.StartOffset;
        using var bounded = new BoundedReadStream(stream, Math.Max(0, stream.Length - window.StartOffset));
        using var reader = new StreamReader(bounded, Encoding.UTF8, window.StartOffset == 0);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var oversized = false;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var count = reader.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            var offset = 0;
            while (offset < count) {
                var newline = Array.IndexOf(buffer, '\n', offset, count - offset);
                var end = newline < 0 ? count : newline;
                if (!oversized) {
                    var length = end - offset;
                    if (length > MaximumLineCharacters - line.Length) {
                        line.Clear();
                        oversized = true;
                    } else {
                        line.Append(buffer, offset, length);
                    }
                }
                if (newline >= 0) {
                    if (!oversized) yield return line.ToString();
                    line.Clear();
                    oversized = false;
                }
                offset = end + 1;
            }
        }
        // A final unterminated record may be in flight; a complete JSON object can still be read safely.
        if (!oversized && line.Length > 0) yield return line.ToString();
        window.EndOffset = window.StartOffset + bounded.BytesRead;
        window.TailSafe = bounded.LastByte == (byte)'\n' && reader.CurrentEncoding.CodePage == Encoding.UTF8.CodePage;
    }

    internal static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static JsonElement Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    internal static DateTimeOffset? Timestamp(JsonElement element, string property = "timestamp") =>
        DateTimeOffset.TryParse(Text(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var time) ? time.ToUniversalTime() : null;

    // Preserve an open-turn observation until all sources have been collected. Never cache clock-derived state.
    internal static AgentSessionState Finish(AgentSessionState session, bool active) {
        if (!session.IsArchived && session.State != AgentWorkState.Finished && session.PendingInputs.Any(static input => input.IsBlocking))
            return session with { State = AgentWorkState.WaitingOnPerson, StateIsInferred = false };
        return active ? session with { StateIsInferred = true, LastTurnOutcome = AgentTurnOutcome.Incomplete } : session;
    }
}

internal sealed class AgentSessionReadWindow {
    internal long StartOffset, EndOffset;
    internal bool TailSafe;
    internal long TotalBytesRead = -1;
    internal bool RecordIsComplete, IncompleteRecord, HadIncompleteRecord;
    internal AgentSessionParserState? Checkpoint;
}

// StreamReader may buffer ahead. Bound bytes at open time and track the actual byte offset, including UTF-8.
internal sealed class BoundedReadStream(Stream source, long length) : Stream {
    internal long BytesRead { get; private set; }
    internal byte? LastByte { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) {
        var read = source.Read(buffer, offset, (int)Math.Min(count, length - BytesRead));
        if (read > 0) LastByte = buffer[offset + read - 1];
        BytesRead += read;
        return read;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
