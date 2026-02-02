using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single output item in a chat turn.
/// </summary>
public sealed class TurnOutput {
    /// <summary>
    /// Initializes a new turn output.
    /// </summary>
    public TurnOutput(string type, string? text, string? imageUrl, string? imagePath, string? base64, string? mimeType,
        JsonObject raw, JsonObject? additional) {
        Type = type;
        Text = text;
        ImageUrl = imageUrl;
        ImagePath = imagePath;
        Base64 = base64;
        MimeType = mimeType;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the output type.
    /// </summary>
    public string Type { get; }
    /// <summary>
    /// Gets the text content when present.
    /// </summary>
    public string? Text { get; }
    /// <summary>
    /// Gets the image URL when present.
    /// </summary>
    public string? ImageUrl { get; }
    /// <summary>
    /// Gets the local image path when present.
    /// </summary>
    public string? ImagePath { get; }
    /// <summary>
    /// Gets the base64 payload when present.
    /// </summary>
    public string? Base64 { get; }
    /// <summary>
    /// Gets the mime type when present.
    /// </summary>
    public string? MimeType { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Gets a value indicating whether this output is text.
    /// </summary>
    public bool IsText => string.Equals(Type, "text", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Gets a value indicating whether this output is an image.
    /// </summary>
    public bool IsImage => string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<TurnOutput> FromTurn(JsonObject turnObj) {
        var outputs = FindOutputs(turnObj);
        if (outputs is null || outputs.Count == 0) {
            return Array.Empty<TurnOutput>();
        }

        var items = new List<TurnOutput>(outputs.Count);
        foreach (var value in outputs) {
            var obj = value.AsObject();
            if (obj is null) {
                continue;
            }
            items.Add(Parse(obj));
        }
        return items;
    }

    internal static IReadOnlyList<TurnOutput> FilterImages(IReadOnlyList<TurnOutput> outputs) {
        if (outputs.Count == 0) {
            return Array.Empty<TurnOutput>();
        }
        var images = new List<TurnOutput>();
        foreach (var output in outputs) {
            if (output.IsImage) {
                images.Add(output);
            }
        }
        return images;
    }

    private static TurnOutput Parse(JsonObject obj) {
        var type = obj.GetString("type") ?? obj.GetString("kind") ?? "unknown";
        var text = obj.GetString("text") ?? obj.GetString("content");
        var url = obj.GetString("url") ?? obj.GetString("imageUrl") ?? obj.GetString("image_url");
        var path = obj.GetString("path") ?? obj.GetString("file") ?? obj.GetString("filePath");
        var base64 = obj.GetString("base64") ?? obj.GetString("data");
        var mime = obj.GetString("mimeType") ?? obj.GetString("mime_type") ?? obj.GetString("contentType");
        var additional = obj.ExtractAdditional(
            "type", "kind", "text", "content", "url", "imageUrl", "image_url",
            "path", "file", "filePath", "base64", "data", "mimeType", "mime_type", "contentType");
        return new TurnOutput(type, text, url, path, base64, mime, obj, additional);
    }

    private static JsonArray? FindOutputs(JsonObject turnObj) {
        var outputs = turnObj.GetArray("output") ?? turnObj.GetArray("outputs");
        if (outputs is not null) {
            return outputs;
        }

        var response = turnObj.GetObject("response");
        if (response is not null) {
            outputs = response.GetArray("output") ?? response.GetArray("outputs");
            if (outputs is not null) {
                return outputs;
            }
        }

        var result = turnObj.GetObject("result");
        if (result is not null) {
            outputs = result.GetArray("output") ?? result.GetArray("outputs");
            if (outputs is not null) {
                return outputs;
            }
        }

        return null;
    }
}
