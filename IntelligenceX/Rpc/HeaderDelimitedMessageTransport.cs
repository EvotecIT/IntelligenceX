using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Rpc;

internal sealed class HeaderDelimitedMessageTransport : IDisposable {
    private const int MaxHeaderBytes = 16 * 1024;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public HeaderDelimitedMessageTransport(Stream input, Stream output) {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public event EventHandler<string>? MessageSent;
    public event EventHandler<string>? MessageReceived;

    public async Task SendAsync(string message, CancellationToken cancellationToken = default) {
        if (message is null) {
            throw new ArgumentNullException(nameof(message));
        }
        var payload = Encoding.UTF8.GetBytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await _output.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            MessageSent?.Invoke(this, message);
        } finally {
            _sendLock.Release();
        }
    }

    public async Task ReadLoopAsync(Action<string> onMessage, CancellationToken cancellationToken) {
        if (onMessage is null) {
            throw new ArgumentNullException(nameof(onMessage));
        }
        while (!cancellationToken.IsCancellationRequested) {
            var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (message is null) {
                break;
            }
            MessageReceived?.Invoke(this, message);
            onMessage(message);
        }
    }

    private async Task<string?> ReadMessageAsync(CancellationToken cancellationToken) {
        var headerBytes = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (headerBytes is null) {
            return null;
        }
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var length = ParseContentLength(headerText);
        if (length < 0) {
            throw new FormatException("Missing Content-Length header.");
        }
        var payload = await ReadExactAsync(length, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(payload);
    }

    private async Task<byte[]?> ReadHeaderAsync(CancellationToken cancellationToken) {
        var buffer = new List<byte>();
        var pattern = new byte[] { 13, 10, 13, 10 };
        var match = 0;
        var one = new byte[1];

        while (true) {
            var read = await _input.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
            if (read == 0) {
                return buffer.Count == 0 ? null : throw new EndOfStreamException("Unexpected end of stream.");
            }
            buffer.Add(one[0]);
            if (buffer.Count > MaxHeaderBytes) {
                throw new FormatException("Header section too large.");
            }

            if (one[0] == pattern[match]) {
                match++;
                if (match == pattern.Length) {
                    return buffer.ToArray();
                }
            } else {
                match = one[0] == pattern[0] ? 1 : 0;
            }
        }
    }

    private async Task<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken) {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length) {
            var read = await _input.ReadAsync(buffer, offset, length - offset, cancellationToken).ConfigureAwait(false);
            if (read == 0) {
                throw new EndOfStreamException("Unexpected end of stream while reading payload.");
            }
            offset += read;
        }
        return buffer;
    }

    private static int ParseContentLength(string headerText) {
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) {
            var parts = line.Split(new[] { ':' }, 2, StringSplitOptions.None);
            if (parts.Length != 2) {
                continue;
            }
            if (!parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (int.TryParse(parts[1].Trim(), out var length)) {
                return length;
            }
        }
        return -1;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _sendLock.Dispose();
    }
}
