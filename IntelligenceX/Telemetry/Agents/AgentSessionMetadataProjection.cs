using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace IntelligenceX.Telemetry.Agents;

// Project the small state-bearing envelope before JsonDocument sees it. The stream framer
// skips unneeded values without decoding strings, retaining tool output, or buffering a line.
// System.Text.Json validates/decodes the selected metadata. Unknown payload internals are not
// a transcript-validation surface; malformed framing and malformed selected values are reported.
internal static class AgentSessionMetadataProjection {
    private const int MaximumEnvelopeBytes = 256 * 1024;
    internal static IEnumerable<JsonDocument> Read(string path, Action<string> diagnostic, CancellationToken token, AgentSessionReadWindow window) {
        window.EndOffset = window.StartOffset;
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        source.Position = window.StartOffset;
        using var bounded = new BoundedReadStream(source, Math.Max(0, source.Length - window.StartOffset));
        var input = new Cursor(bounded, token);
        var first = input.Peek();
        if (window.StartOffset == 0 && (first == 0xff || first == 0xfe)) {
            // Existing recovered UTF-16 files retain the bounded legacy path; never tail them as UTF-8.
            foreach (var line in AgentSessionLogReader.ReadLines(path, token, window)) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonDocument? document = null;
                try { document = JsonDocument.Parse(line); } catch (JsonException) { diagnostic("malformed-record"); }
                if (document is not null) yield return document;
            }
            yield break;
        }
        if (window.StartOffset == 0 && first == 0xef) {
            var valid = true;
            input.BeginRecord();
            foreach (var expected in new[] { 0xef, 0xbb, 0xbf }) {
                if (input.Take() != expected) { valid = false; break; }
            }
            if (!valid) {
                input.ToNewline();
                window.RecordIsComplete = input.EndedAtNewline;
                window.HadIncompleteRecord = !window.RecordIsComplete;
                window.IncompleteRecord = !window.RecordIsComplete;
                diagnostic("malformed-record");
                window.IncompleteRecord = false;
                if (!window.RecordIsComplete) {
                    window.TotalBytesRead = bounded.BytesRead;
                    window.TailSafe = true;
                    yield break;
                }
            }
            window.EndOffset = input.Position;
        }
        while (input.Peek() >= 0) {
            token.ThrowIfCancellationRequested();
            input.Space();
            if (input.Peek() == '\n') { input.Take(); window.EndOffset = window.StartOffset + input.Position; continue; }
            if (input.Peek() < 0) break;
            input.BeginRecord();
            JsonDocument? document = null;
            using var output = new MemoryStream();
            using var writer = new Utf8JsonWriter(output);
            try {
                if (input.SmallRecord(out var small)) {
                    window.RecordIsComplete = true;
                    window.EndOffset = window.StartOffset + input.Position;
                    document = JsonDocument.Parse(small);
                } else {
                    if (input.Peek() != '{') throw new JsonException();
                    ProjectObject(input, writer, Context.Root, 0);
                    input.Space();
                    if (input.Peek() >= 0 && input.Peek() != '\n') throw new JsonException();
                    window.RecordIsComplete = input.Peek() == '\n';
                    if (window.RecordIsComplete) {
                        input.Take();
                        window.EndOffset = window.StartOffset + input.Position;
                    }
                    writer.Flush();
                    document = JsonDocument.Parse(output.ToArray());
                }
            } catch (JsonException) {
                input.ToNewline();
                window.RecordIsComplete = input.EndedAtNewline;
                if (window.RecordIsComplete) window.EndOffset = window.StartOffset + input.Position;
                window.IncompleteRecord = !window.RecordIsComplete;
                window.HadIncompleteRecord |= window.IncompleteRecord;
                diagnostic("malformed-record");
                window.IncompleteRecord = false;
            }
            if (document is not null) yield return document;
        }
        window.TotalBytesRead = bounded.BytesRead;
        window.TailSafe = true; // EndOffset is the last complete newline, not an in-flight suffix.
    }

    private enum Context { Root, Payload, Git, Message, Block }

    private static void ProjectObject(Cursor input, Utf8JsonWriter writer, Context context, int depth) {
        if (depth > 64) throw new JsonException();
        input.Expect('{');
        writer.WriteStartObject();
        input.Space();
        if (input.Peek() != '}') while (true) {
            var rawName = input.String();
            using var nameDocument = JsonDocument.Parse(rawName);
            var name = nameDocument.RootElement.GetString()!;
            input.Space(); input.Expect(':'); input.Space();
            var nested = context == Context.Root && name == "payload" ? Context.Payload
                : context == Context.Root && name == "message" ? Context.Message
                : context == Context.Payload && name == "git" ? Context.Git : (Context?)null;
            if (nested.HasValue && input.Peek() == '{') {
                writer.WritePropertyName(name);
                ProjectObject(input, writer, nested.Value, depth + 1);
            } else if (context == Context.Message && name == "content" && input.Peek() == '[') {
                writer.WritePropertyName(name);
                ProjectBlocks(input, writer, depth + 1);
            } else if (context == Context.Payload && name == "error") {
                var isNull = input.Peek() == 'n';
                input.Skip(depth + 1);
                if (isNull) writer.WriteNull(name); else writer.WriteBoolean(name, true);
            } else if (Keep(context, name) && input.Peek() != '{' && input.Peek() != '[') {
                var raw = input.Value();
                using var value = JsonDocument.Parse(raw);
                writer.WritePropertyName(name);
                value.RootElement.WriteTo(writer);
            } else input.Skip(depth + 1);
            if (writer.BytesCommitted + writer.BytesPending > MaximumEnvelopeBytes) throw new JsonException();
            input.Space();
            if (input.Peek() != ',') break;
            input.Take(); input.Space();
        }
        input.Expect('}');
        writer.WriteEndObject();
    }

    private static void ProjectBlocks(Cursor input, Utf8JsonWriter writer, int depth) {
        input.Expect('['); writer.WriteStartArray(); input.Space();
        if (input.Peek() != ']') while (true) {
            if (input.Peek() == '{') ProjectObject(input, writer, Context.Block, depth + 1);
            else input.Skip(depth + 1);
            input.Space();
            if (input.Peek() != ',') break;
            input.Take(); input.Space();
        }
        input.Expect(']'); writer.WriteEndArray();
    }

    private static bool Keep(Context context, string name) => context switch {
        Context.Root => name is "type" or "timestamp" or "sessionId" or "cwd" or "gitBranch" or "isSidechain" or "isApiErrorMessage" or "is_error" or "customTitle" or "subtype",
        Context.Payload => name is "type" or "id" or "session_id" or "cwd" or "thread_id" or "threadId" or "thread_name" or "title" or "name" or "call_id",
        Context.Git => name is "branch" or "repository_url",
        Context.Message => name == "stop_reason",
        Context.Block => name is "type" or "name",
        _ => false
    };

    private sealed class Cursor(Stream stream, CancellationToken token) {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _offset, _length;
        internal long Position { get; private set; }
        internal bool EndedAtNewline { get; private set; }
        internal bool SmallRecord(out ReadOnlyMemory<byte> record) {
            // JsonDocument can borrow this memory until the caller disposes the yielded document.
            // Common small envelopes avoid both UTF-16 conversion and per-property projection.
            var newline = _buffer.AsSpan(_offset, _length - _offset).IndexOf((byte)'\n');
            if (newline < 0 || newline > 4096) { record = default; return false; }
            record = new ReadOnlyMemory<byte>(_buffer, _offset, newline);
            _offset += newline + 1; Position += newline + 1; EndedAtNewline = true;
            return true;
        }
        internal int Peek() {
            if (_offset == _length) {
                token.ThrowIfCancellationRequested();
                _length = stream.Read(_buffer, 0, _buffer.Length); _offset = 0;
                if (_length == 0) return -1;
            }
            return _buffer[_offset];
        }
        internal void BeginRecord() => EndedAtNewline = false;
        internal int Take() { var value = Peek(); if (value >= 0) { _offset++; Position++; EndedAtNewline = value == '\n'; } return value; }
        internal void Expect(int value) { if (Take() != value) throw new JsonException(); }
        internal void Space() { while (Peek() is ' ' or '\r' or '\t') Take(); }
        internal void ToNewline() {
            if (EndedAtNewline) return;
            while (Peek() >= 0) {
                var newline = _buffer.AsSpan(_offset, _length - _offset).IndexOf((byte)'\n');
                var count = newline < 0 ? _length - _offset : newline + 1;
                _offset += count; Position += count;
                if (newline >= 0) { EndedAtNewline = true; return; }
            }
        }
        internal byte[] String() {
            using var output = new MemoryStream();
            Expect('"'); output.WriteByte((byte)'"');
            while (true) {
                var value = Take();
                if (value < 0 || value == '\n' || output.Length >= 64 * 1024) throw new JsonException();
                output.WriteByte((byte)value);
                if (value == '"') break;
                if (value == '\\') { value = Take(); if (value < 0 || value == '\n') throw new JsonException(); output.WriteByte((byte)value); }
            }
            return output.ToArray();
        }
        internal byte[] Value() {
            if (Peek() == '"') return String();
            using var output = new MemoryStream();
            while (Peek() >= 0 && Peek() is not (',' or '}' or ']' or '\n' or '\r' or '\t' or ' ')) {
                if (output.Length >= 1024) throw new JsonException();
                output.WriteByte((byte)Take());
            }
            return output.ToArray();
        }
        internal void Skip(int depth) {
            if (depth > 64) throw new JsonException();
            var first = Peek();
            if (first == '"') { SkipString(); return; }
            if (first != '{' && first != '[') { using var value = JsonDocument.Parse(Value()); return; }
            Take(); Space();
            var end = first == '{' ? '}' : ']';
            if (Peek() != end) while (true) {
                if (first == '{') { SkipString(); Space(); Expect(':'); Space(); }
                Skip(depth + 1); Space();
                if (Peek() != ',') break;
                Take(); Space();
            }
            Expect(end);
        }
        private void SkipString() {
            Expect('"');
            while (true) {
                if (Peek() < 0) throw new JsonException();
                var count = _buffer.AsSpan(_offset, _length - _offset).IndexOfAny((byte)'"', (byte)'\\', (byte)'\n');
                if (count < 0) { Position += _length - _offset; _offset = _length; continue; }
                Position += count; _offset += count;
                var value = Take();
                if (value == '"') return;
                if (value == '\n') throw new JsonException();
                value = Take();
                if (value < 0 || value == '\n') throw new JsonException();
            }
        }
    }
}
