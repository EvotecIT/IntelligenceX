using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Builds a multi-part chat input with text and optional images.
/// </summary>
/// <example>
/// <code>
/// var input = ChatInput.FromText("Analyze the error log")
///     .AddImagePath("C:\\temp\\screenshot.png");
/// </code>
/// </example>
public sealed class ChatInput {
    private readonly JsonArray _items = new();

    /// <summary>
    /// Creates a text-only input.
    /// </summary>
    public static ChatInput FromText(string text) => new ChatInput().AddText(text);

    /// <summary>
    /// Creates a text input with a local image path.
    /// </summary>
    public static ChatInput FromTextWithImagePath(string text, string path) => new ChatInput().AddText(text).AddImagePath(path);

    /// <summary>
    /// Creates a text input with an image URL.
    /// </summary>
    public static ChatInput FromTextWithImageUrl(string text, string url) => new ChatInput().AddText(text).AddImageUrl(url);

    /// <summary>
    /// Adds a text block to the input.
    /// </summary>
    public ChatInput AddText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            throw new ArgumentException("Text cannot be empty.", nameof(text));
        }
        _items.Add(new JsonObject()
            .Add("type", "text")
            .Add("text", text));
        return this;
    }

    /// <summary>
    /// Adds an image URL to the input.
    /// </summary>
    public ChatInput AddImageUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            throw new ArgumentException("URL cannot be empty.", nameof(url));
        }
        _items.Add(new JsonObject()
            .Add("type", "image")
            .Add("url", url));
        return this;
    }

    /// <summary>
    /// Adds a local image path to the input.
    /// </summary>
    public ChatInput AddImagePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }
        _items.Add(new JsonObject()
            .Add("type", "image")
            .Add("path", path));
        return this;
    }

    /// <summary>
    /// Adds a raw JSON item to the input payload.
    /// </summary>
    public ChatInput AddRaw(JsonObject item) {
        if (item is null) {
            throw new ArgumentNullException(nameof(item));
        }
        _items.Add(item);
        return this;
    }

    internal string[] GetImagePaths() {
        var list = new System.Collections.Generic.List<string>();
        foreach (var item in _items) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }
            var type = obj.GetString("type");
            if (!string.Equals(type, "image", StringComparison.Ordinal)) {
                continue;
            }
            var path = obj.GetString("path");
            if (!string.IsNullOrWhiteSpace(path)) {
                list.Add(path!);
            }
        }
        return list.ToArray();
    }

    internal JsonArray ToJson() => _items;
}
