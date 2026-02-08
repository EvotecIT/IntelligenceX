using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport {
    private static async Task<string> ParseErrorResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) {
            return $"ChatGPT request failed ({(int)response.StatusCode}).";
        }
        try {
            var value = JsonLite.Parse(text);
            var obj = value?.AsObject();
            var error = obj?.GetObject("error");
            var message = error?.GetString("message") ?? obj?.GetString("message");
            var code = error?.GetString("code") ?? error?.GetString("type");
            if (response.StatusCode == (HttpStatusCode)429) {
                var resetsAt = error?.GetInt64("resets_at");
                if (resetsAt.HasValue) {
                    var mins = Math.Max(0, (int)Math.Round((resetsAt.Value * 1000 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 60000d));
                    return $"ChatGPT usage limit reached. Try again in about {mins} minute(s).";
                }
                return "ChatGPT usage limit reached (HTTP 429).";
            }
            if (!string.IsNullOrWhiteSpace(message)) {
                return message!;
            }
            if (!string.IsNullOrWhiteSpace(code)) {
                return $"ChatGPT error: {code}";
            }
        } catch {
            // Fall back to raw text.
        }
        return text;
    }

    private static void TryDumpRequest(HttpRequestMessage request, string json) {
        if (!IsTraceEnabled()) {
            return;
        }
        try {
            var dir = Path.Combine(Path.GetTempPath(), "IntelligenceX");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"openai-native-request-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"{request.Method} {request.RequestUri}");
            sb.AppendLine("Headers:");
            foreach (var header in request.Headers) {
                var value = string.Join(",", header.Value);
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) {
                    value = "Bearer ***";
                }
                sb.AppendLine($"{header.Key}: {value}");
            }
            if (request.Content?.Headers is not null) {
                foreach (var header in request.Content.Headers) {
                    sb.AppendLine($"{header.Key}: {string.Join(",", header.Value)}");
                }
            }
            sb.AppendLine();
            sb.AppendLine(json);
            File.WriteAllText(file, sb.ToString());
            Console.Error.WriteLine($"[IntelligenceX] Wrote native request dump: {file}");
        } catch {
            // Ignore dump failures.
        }
    }

    private static bool IsTraceEnabled() {
        var value = Environment.GetEnvironmentVariable("INTELLIGENCEX_NATIVE_TRACE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

