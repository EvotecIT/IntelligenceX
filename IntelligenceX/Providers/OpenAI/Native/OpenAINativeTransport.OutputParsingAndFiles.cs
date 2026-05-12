using System;
using System.Collections.Generic;
using System.IO;
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
    private int _imageGenerationArtifactSequence;

    private static List<JsonObject> BuildOutputsFromDelta(string text) {
        var list = new List<JsonObject>();
        if (!string.IsNullOrWhiteSpace(text)) {
            list.Add(new JsonObject().Add("type", "text").Add("text", text));
        }
        return list;
    }

    private List<JsonObject> ParseOutputsFromResponse(JsonObject response, string sessionId, ChatOptions options) {
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
            if (string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase)) {
                AddImageGenerationOutput(item, sessionId, options, outputs);
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

        EnsureAssistantVisibleImageText(outputs);
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

    private void AddImageGenerationOutput(JsonObject item, string sessionId, ChatOptions options, List<JsonObject> outputs) {
        var result = item.GetString("result");
        if (string.IsNullOrWhiteSpace(result)) {
            return;
        }

        var imageOptions = ResolveImageGenerationOptions(options);
        var mimeType = GuessImageMimeType(imageOptions?.OutputFormat);
        var output = new JsonObject()
            .Add("type", "image")
            .Add("base64", result)
            .Add("mime_type", mimeType);

        var id = item.GetString("id") ?? item.GetString("call_id") ?? item.GetString("callId");
        if (!string.IsNullOrWhiteSpace(id)) {
            output.Add("id", id!.Trim());
        }

        var revisedPrompt = item.GetString("revised_prompt") ?? item.GetString("revisedPrompt");
        if (!string.IsNullOrWhiteSpace(revisedPrompt)) {
            output.Add("revised_prompt", revisedPrompt!.Trim());
        }

        if (imageOptions is not null && imageOptions.SaveOutputImages == true) {
            try {
                var path = SaveImageGenerationResult(sessionId, id, result!, imageOptions);
                output.Add("path", path);
            } catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException ||
                                         ex is ArgumentException || ex is NotSupportedException) {
                output.Add("save_error", ex.Message);
            }
        }

        outputs.Add(output);
    }

    private static void EnsureAssistantVisibleImageText(List<JsonObject> outputs) {
        var imageCount = 0;
        var savedPaths = new List<string>();
        foreach (var output in outputs) {
            var type = output.GetString("type");
            if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(output.GetString("text"))) {
                return;
            }
            if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            imageCount++;
            var path = output.GetString("path");
            if (!string.IsNullOrWhiteSpace(path)) {
                savedPaths.Add(path!.Trim());
            }
        }

        if (imageCount == 0) {
            return;
        }

        var text = imageCount == 1 ? "Generated an image." : $"Generated {imageCount} images.";
        if (savedPaths.Count == 1) {
            text += " Saved to: " + savedPaths[0];
        } else if (savedPaths.Count > 1) {
            var sb = new StringBuilder(text);
            sb.AppendLine();
            sb.Append("Saved outputs:");
            foreach (var path in savedPaths) {
                sb.AppendLine();
                sb.Append("- ");
                sb.Append(path);
            }
            text = sb.ToString();
        }

        outputs.Add(new JsonObject().Add("type", "text").Add("text", text));
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

    private string SaveImageGenerationResult(string sessionId, string? callId, string result, ImageGenerationOptions options) {
        var bytes = Convert.FromBase64String(result.Trim());
        var path = GetImageGenerationArtifactPath(sessionId, callId, options);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory!);
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string GetImageGenerationArtifactPath(string sessionId, string? callId, ImageGenerationOptions options) {
        var directory = ResolveImageGenerationOutputDirectory(options);
        var safeSession = SanitizePathPart(sessionId, "session");
        var fallbackCall = "generated_image_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) +
                           "_" + Interlocked.Increment(ref _imageGenerationArtifactSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var safeCall = SanitizePathPart(callId, fallbackCall);
        var extension = ResolveImageExtension(options.OutputFormat);
        return Path.Combine(directory, safeSession, safeCall + extension);
    }

    private string ResolveImageGenerationOutputDirectory(ImageGenerationOptions options) {
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory)) {
            return options.OutputDirectory!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.CodexHome)) {
            return Path.Combine(_options.CodexHome!.Trim(), "generated_images");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        return Path.Combine(home, ".intelligencex", "generated_images");
    }

    private static string SanitizePathPart(string? value, string fallback) {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++) {
            var ch = source[i];
            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '-' ||
                ch == '_') {
                builder.Append(ch);
            } else {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static string ResolveImageExtension(string? outputFormat) {
        var format = NormalizeOptional(outputFormat) ?? "png";
        switch (format.ToLowerInvariant()) {
            case "jpg":
            case "jpeg":
                return ".jpg";
            case "webp":
                return ".webp";
            case "png":
            default:
                return ".png";
        }
    }

    private static string GuessImageMimeType(string? outputFormat) {
        var format = NormalizeOptional(outputFormat) ?? "png";
        switch (format.ToLowerInvariant()) {
            case "jpg":
            case "jpeg":
                return "image/jpeg";
            case "webp":
                return "image/webp";
            case "png":
            default:
                return "image/png";
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
