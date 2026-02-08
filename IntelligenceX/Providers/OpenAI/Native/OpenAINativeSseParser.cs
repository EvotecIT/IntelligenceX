using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Native;

internal static class OpenAINativeSseParser {
    public static async Task ParseAsync(Stream stream, Func<JsonObject, Task> onEvent, CancellationToken cancellationToken) {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new StringBuilder();
        var charBuffer = new char[4096];

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(charBuffer, 0, charBuffer.Length).ConfigureAwait(false);
            if (read <= 0) {
                break;
            }
            buffer.Append(charBuffer, 0, read);
            NormalizeNewLines(buffer);
            await DrainBufferAsync(buffer, onEvent, cancellationToken).ConfigureAwait(false);
        }

        await DrainBufferAsync(buffer, onEvent, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DrainBufferAsync(StringBuilder buffer, Func<JsonObject, Task> onEvent,
        CancellationToken cancellationToken) {
        while (true) {
            var index = buffer.ToString().IndexOf("\n\n", StringComparison.Ordinal);
            if (index < 0) {
                return;
            }

            var chunk = buffer.ToString(0, index);
            buffer.Remove(0, index + 2);
            var data = ExtractData(chunk);
            if (string.IsNullOrWhiteSpace(data) || string.Equals(data, "[DONE]", StringComparison.Ordinal)) {
                continue;
            }
            JsonValue? value;
            try {
                value = JsonLite.Parse(data);
            } catch (FormatException) {
                // Some transports/proxies can emit multiline `data:` values for JSON payloads. Try an alternate
                // reconstruction that avoids inserting separators between data lines.
                var alt = ExtractData(chunk, alternate: true);
                if (string.IsNullOrWhiteSpace(alt) || string.Equals(alt, data, StringComparison.Ordinal)) {
                    continue;
                }
                try {
                    value = JsonLite.Parse(alt);
                } catch (FormatException) {
                    // Skip invalid events and continue parsing; callers can still fall back to accumulated deltas
                    // if the completed response isn't available.
                    continue;
                }
            }
            var obj = value?.AsObject();
            if (obj is null) {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await onEvent(obj).ConfigureAwait(false);
        }
    }

    private static string ExtractData(string chunk) {
        return ExtractData(chunk, alternate: false);
    }

    private static string ExtractData(string chunk, bool alternate) {
        var lines = chunk.Split('\n');
        var dataLines = new StringBuilder();
        foreach (var line in lines) {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) {
                continue;
            }
            var value = line.Substring(5).Trim();
            if (!alternate && dataLines.Length > 0) {
                // SSE multiline data is joined with newlines by spec.
                dataLines.Append('\n');
            }
            dataLines.Append(value);
        }
        return dataLines.ToString();
    }

    private static void NormalizeNewLines(StringBuilder buffer) {
        buffer.Replace("\r\n", "\n");
    }
}
