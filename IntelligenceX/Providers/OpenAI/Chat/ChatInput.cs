using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Chat;

public sealed class ChatInput {
    private readonly JsonArray _items = new();

    public static ChatInput FromText(string text) => new ChatInput().AddText(text);

    public static ChatInput FromTextWithImagePath(string text, string path) => new ChatInput().AddText(text).AddImagePath(path);

    public static ChatInput FromTextWithImageUrl(string text, string url) => new ChatInput().AddText(text).AddImageUrl(url);

    public ChatInput AddText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            throw new ArgumentException("Text cannot be empty.", nameof(text));
        }
        _items.Add(new JsonObject()
            .Add("type", "text")
            .Add("text", text));
        return this;
    }

    public ChatInput AddImageUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            throw new ArgumentException("URL cannot be empty.", nameof(url));
        }
        _items.Add(new JsonObject()
            .Add("type", "image")
            .Add("url", url));
        return this;
    }

    public ChatInput AddImagePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }
        _items.Add(new JsonObject()
            .Add("type", "image")
            .Add("path", path));
        return this;
    }

    public ChatInput AddRaw(JsonObject item) {
        if (item is null) {
            throw new ArgumentNullException(nameof(item));
        }
        _items.Add(item);
        return this;
    }

    internal JsonArray ToJson() => _items;
}
