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
    private const int MaximumProviderCodeBytes = 64;
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

    private enum Context { Root, Payload, Git, Message, Block, Error, Code, Item, Origin, SessionSource }

    private static void ProjectObject(Cursor input, Utf8JsonWriter writer, Context context, int depth) {
        if (depth > 64) throw new JsonException();
        input.Expect('{');
        writer.WriteStartObject();
        input.Space();
        if (input.Peek() != '}') while (true) {
            var rawName = input.String();
            using var nameDocument = JsonDocument.Parse(rawName);
            var name = DecodeString(nameDocument.RootElement);
            input.Space(); input.Expect(':'); input.Space();
            var nested = context == Context.Root && name == "payload" ? Context.Payload
                : context == Context.Root && name == "message" ? Context.Message
                : context == Context.Root && name == "origin" ? Context.Origin
                : (context == Context.Root || context == Context.Payload) && name == "error" ? Context.Error
                : (context == Context.Payload || context == Context.Error) && name == "codex_error_info" ? Context.Code
                : context == Context.Payload && name == "git" ? Context.Git
                : context == Context.Payload && name == "source" ? Context.SessionSource : (Context?)null;
            if (context == Context.Payload && name == "item") nested = Context.Item;
            if (nested.HasValue && input.Peek() == '{') {
                writer.WritePropertyName(name);
                ProjectObject(input, writer, nested.Value, depth + 1);
            } else if ((context == Context.Message || context == Context.Payload) && name == "content" && input.Peek() == '[') {
                writer.WritePropertyName(name);
                ProjectBlocks(input, writer, depth + 1);
            } else if (context == Context.Block && name == "text" && input.Peek() == '"') {
                ProjectQuestionReply(input, writer, depth + 1);
            } else if (context == Context.Item && name == "questions" && input.Peek() == '[') {
                input.Expect('['); input.Space();
                var count = 0;
                if (input.Peek() != ']') while (true) {
                    input.Skip(depth + 1); count++; input.Space();
                    if (input.Peek() != ',') break;
                    input.Take(); input.Space();
                }
                input.Expect(']'); writer.WriteNumber("questionCount", count);
            } else if (context == Context.Code) {
                writer.WriteNull(name);
                input.Skip(depth + 1);
            } else if (context == Context.Root && name == "error" && input.Peek() == '"') {
                // Native Claude errors can be scalar codes. Retain only exact known codes,
                // draining arbitrary error text without retaining or exporting it.
                using var text = new JsonStringStream(input);
                var buffer = new byte[MaximumProviderCodeBytes];
                var length = text.Read(buffer, 0, buffer.Length);
                text.Drain();
                var code = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
                if (length < buffer.Length && AgentFailureClassifier.ClaudeCause(code) != AgentFailureCause.Unknown)
                    writer.WriteString(name, code);
            } else if (context == Context.Payload && name == "error") {
                var isNull = input.Peek() == 'n';
                input.Skip(depth + 1);
                if (isNull) writer.WriteNull(name); else writer.WriteBoolean(name, true);
            } else if (context == Context.SessionSource && name == "subagent" && (input.Peek() == '{' || input.Peek() == '"')) {
                writer.WriteBoolean(name, true);
                input.Skip(depth + 1);
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
        Context.Root => name is "type" or "uuid" or "parentUuid" or "parentSessionId" or "timestamp" or "sessionId" or "cwd" or "gitBranch" or "isSidechain" or "isApiErrorMessage" or "is_error" or "customTitle" or "subtype" or "prNumber" or "prUrl" or "prRepository",
        Context.Payload => name is "type" or "role" or "turn_id" or "turnId" or "id" or "session_id" or "source" or "parent_thread_id" or "cwd" or "thread_id" or "threadId" or "thread_name" or "title" or "name" or "call_id" or "codex_error_info",
        Context.Git => name is "branch" or "repository_url",
        Context.Message => name == "stop_reason",
        Context.Block => name is "type" or "name" or "id" or "tool_use_id",
        Context.Error => name is "type" or "codex_error_info",
        Context.Item => name is "type" or "id" or "delivery",
        Context.Origin => name is "kind" or "msg_id",
        _ => false
    };

    // Only the native reply wrapper is decoded. Ordinary messages and large answers are
    // skipped in the stream; the projection retains question identities, never text.
    private static void ProjectQuestionReply(Cursor input, Utf8JsonWriter writer, int depth) {
        using var text = new JsonStringStream(input);
        foreach (var expected in CodexQuestionReplies.Opening) {
            if (text.ReadByte() != expected) { text.Drain(); return; }
        }
        var body = new Cursor(text, default, allowNewlines: true);
        var ids = new List<string>();
        var identityCharacters = 0;
        body.Space();
        while (body.Peek() is '\n' or '\r' or ' ' or '\t') body.Take();
        // Untrusted user text that resembles a wrapper is not a malformed session record.
        try {
            body.Expect('['); body.Space();
            if (body.Peek() != ']') while (true) {
                body.Expect('{'); body.Space();
                if (body.Peek() != '}') while (true) {
                    using var key = JsonDocument.Parse(body.String());
                    body.Space(); body.Expect(':'); body.Space();
                    if (DecodeString(key.RootElement) == "questionItemId" && body.Peek() == '"') {
                        using var id = JsonDocument.Parse(body.String());
                        ids.Add(DecodeString(id.RootElement));
                        identityCharacters += ids[ids.Count - 1].Length;
                        if (ids.Count > 1024 || identityCharacters > 64 * 1024) throw new JsonException();
                    } else body.Skip(depth + 1);
                    body.Space();
                    if (body.Peek() != ',') break;
                    body.Take(); body.Space();
                }
                body.Expect('}'); body.Space();
                if (body.Peek() != ',') break;
                body.Take(); body.Space();
            }
            body.Expect(']');
            while (body.Peek() is '\n' or '\r' or ' ' or '\t') body.Take();
            foreach (var expected in CodexQuestionReplies.Closing) body.Expect(expected);
            while (body.Peek() is '\n' or '\r' or ' ' or '\t') body.Take();
            if (body.Peek() >= 0) throw new JsonException();
            writer.WriteStartArray("questionReplyIds");
            foreach (var id in ids) writer.WriteStringValue(id);
            writer.WriteEndArray();
        } catch (JsonException) { /* Ignore an invalid reply; retain the pending question. */ }
        text.Drain();
    }

    private static string DecodeString(JsonElement value) {
        try { return value.GetString()!; }
        catch (InvalidOperationException error) {
            // JsonDocument accepts escaped unpaired surrogates until string decoding.
            // Classify them with other invalid record data at the per-record boundary.
            throw new JsonException("Invalid Unicode in session metadata.", error);
        }
    }

    // Expose one JSON string as decoded UTF-8 for the existing streaming framer.
    // Escape decoding stays bounded even when an answer contains megabytes of text.
    private sealed class JsonStringStream : Stream {
        private readonly Cursor _input;
        private byte[] _escaped = Array.Empty<byte>();
        private int _index;
        private bool _ended;
        internal JsonStringStream(Cursor input) { _input = input; input.Expect('"'); }
        public override int ReadByte() {
            if (_index < _escaped.Length) return _escaped[_index++];
            if (_ended) return -1;
            var value = _input.Take();
            if (value == '"') { _ended = true; return -1; }
            if (value < 0 || value < 0x20) throw new JsonException();
            if (value != '\\') return value;
            var escape = _input.Take();
            if (escape != 'u') return escape switch {
                '"' or '\\' or '/' => escape, 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', _ => throw new JsonException()
            };
            var encoded = new List<byte> { (byte)'"', (byte)'\\', (byte)'u' };
            for (var i = 0; i < 4; i++) encoded.Add((byte)_input.Take());
            // A high surrogate must include its paired escape for System.Text.Json.
            if ((encoded[3] is (byte)'d' or (byte)'D') && encoded[4] is (byte)'8' or (byte)'9' or (byte)'a' or (byte)'A' or (byte)'b' or (byte)'B') {
                _input.Expect('\\'); _input.Expect('u'); encoded.Add((byte)'\\'); encoded.Add((byte)'u');
                for (var i = 0; i < 4; i++) encoded.Add((byte)_input.Take());
            }
            encoded.Add((byte)'"');
            using var decoded = JsonDocument.Parse(encoded.ToArray());
            _escaped = System.Text.Encoding.UTF8.GetBytes(DecodeString(decoded.RootElement)); _index = 0;
            return _escaped[_index++];
        }
        internal void Drain() { if (!_ended) { _input.SkipStringRemainder(); _ended = true; } }
        public override int Read(byte[] buffer, int offset, int count) {
            var read = 0;
            while (read < count) {
                if (_index >= _escaped.Length && !_ended) {
                    var copied = _input.CopyStringChunk(buffer, offset + read, count - read);
                    if (copied > 0) { read += copied; continue; }
                }
                var value = ReadByte(); if (value < 0) break; buffer[offset + read++] = (byte)value;
            }
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Cursor(Stream stream, CancellationToken token, bool allowNewlines = false) {
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
        internal void Space() { while (Peek() is ' ' or '\r' or '\t' || (allowNewlines && Peek() == '\n')) Take(); }
        internal int CopyStringChunk(byte[] target, int offset, int count) {
            if (Peek() < 0) return 0;
            var available = _buffer.AsSpan(_offset, Math.Min(count, _length - _offset));
            var special = available.IndexOfAny((byte)'"', (byte)'\\', (byte)'\n');
            var copied = special < 0 ? available.Length : special;
            available.Slice(0, copied).CopyTo(target.AsSpan(offset, copied));
            _offset += copied; Position += copied;
            return copied;
        }
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
            SkipStringRemainder();
        }
        internal void SkipStringRemainder() {
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
