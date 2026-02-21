using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport : IOpenAITransport {
    private static List<JsonObject> BuildOutputsFromDelta(string text) {
        var list = new List<JsonObject>();
        if (!string.IsNullOrWhiteSpace(text)) {
            list.Add(new JsonObject().Add("type", "text").Add("text", text));
        }
        return list;
    }

    private static List<JsonObject> ParseOutputsFromResponse(JsonObject response) {
        var outputs = new List<JsonObject>();
        var outputArray = response.GetArray("output");
        if (outputArray is null) {
            return outputs;
        }

        foreach (var itemValue in outputArray) {
            var item = itemValue.AsObject();
            if (item is null) {
                continue;
            }
            var type = item.GetString("type");
            if (string.Equals(type, "message", StringComparison.Ordinal)) {
                var content = item.GetArray("content");
                if (content is not null) {
                    ParseContentParts(content, outputs);
                }
                continue;
            }
            if (string.Equals(type, "output_image", StringComparison.Ordinal)) {
                AddImageOutput(item, outputs);
                continue;
            }
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase)) {
                outputs.Add(item);
                continue;
            }
            var text = item.GetString("text");
            if (!string.IsNullOrWhiteSpace(text)) {
                outputs.Add(new JsonObject().Add("type", "text").Add("text", text));
            }
        }

        return outputs;
    }

    private static void ParseContentParts(JsonArray content, List<JsonObject> outputs) {
        foreach (var partValue in content) {
            var part = partValue.AsObject();
            if (part is null) {
                continue;
            }
            var partType = part.GetString("type");
            if (string.Equals(partType, "output_text", StringComparison.Ordinal)) {
                var text = part.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    outputs.Add(new JsonObject().Add("type", "text").Add("text", text));
                }
                continue;
            }
            if (string.Equals(partType, "refusal", StringComparison.Ordinal)) {
                var refusal = part.GetString("refusal") ?? part.GetString("text");
                if (!string.IsNullOrWhiteSpace(refusal)) {
                    outputs.Add(new JsonObject().Add("type", "text").Add("text", refusal));
                }
                continue;
            }
            if (string.Equals(partType, "output_image", StringComparison.Ordinal) ||
                string.Equals(partType, "image", StringComparison.Ordinal)) {
                AddImageOutput(part, outputs);
            }
        }
    }

    private static void AddImageOutput(JsonObject part, List<JsonObject> outputs) {
        var url = part.GetString("image_url") ?? part.GetString("url");
        if (string.IsNullOrWhiteSpace(url)) {
            var imageUrlObj = part.GetObject("image_url");
            url = imageUrlObj?.GetString("url");
        }
        if (!string.IsNullOrWhiteSpace(url)) {
            outputs.Add(new JsonObject().Add("type", "image").Add("url", url));
        }
    }

    private static string ExtractAssistantText(IReadOnlyList<JsonObject> outputs) {
        var sb = new StringBuilder();
        foreach (var output in outputs) {
            var type = output.GetString("type");
            if (!string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var text = output.GetString("text");
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }
            if (sb.Length > 0) {
                sb.AppendLine();
            }
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string TruncatePreview(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        const int max = 120;
        return text.Length <= max ? text : text.Substring(0, max);
    }

    private static async Task<byte[]> ReadFileBytesAsync(string path, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return File.ReadAllBytes(path);
#else
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
#endif
    }

    private static string GuessMimeType(string path) {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext)) {
            return "application/octet-stream";
        }
        var value = ext!.TrimStart('.').ToLowerInvariant();
        return value switch {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    public void Dispose() {
        _httpClient.Dispose();
    }
}
