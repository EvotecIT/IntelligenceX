using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Represents structured chat input content.
/// </summary>
public sealed class ChatInput {
    private readonly JsonArray _items = new();

    /// <summary>
    /// Creates a chat input from a text prompt.
    /// </summary>
    public static ChatInput FromText(string text) => new ChatInput().AddText(text);

    /// <summary>
    /// Creates a chat input from text and a local image path.
    /// </summary>
    public static ChatInput FromTextWithImagePath(string text, string path) => new ChatInput().AddText(text).AddImagePath(path);

    /// <summary>
    /// Creates a chat input from text and an image URL.
    /// </summary>
    public static ChatInput FromTextWithImageUrl(string text, string url) => new ChatInput().AddText(text).AddImageUrl(url);

    /// <summary>
    /// Adds a text item to the input.
    /// </summary>
    /// <param name="text">Text content.</param>
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
    /// Adds an image URL item to the input.
    /// </summary>
    /// <param name="url">Image URL.</param>
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
    /// Adds a synthetic tool call item to the input.
    /// </summary>
    /// <param name="callId">Tool call id.</param>
    /// <param name="name">Tool name.</param>
    /// <param name="input">Tool input JSON.</param>
    public ChatInput AddToolCall(string callId, string name, string? input = null) {
        if (string.IsNullOrWhiteSpace(callId)) {
            throw new ArgumentException("Call id cannot be empty.", nameof(callId));
        }
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(name));
        }

        var normalizedCallId = callId.Trim();
        var normalizedName = name.Trim();
        var normalizedInput = string.IsNullOrWhiteSpace(input) ? "{}" : input!.Trim();
        _items.Add(new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("id", normalizedCallId)
            .Add("call_id", normalizedCallId)
            .Add("name", normalizedName)
            .Add("input", normalizedInput)
            .Add("arguments", normalizedInput)
            .Add("function", new JsonObject()
                .Add("name", normalizedName)
                .Add("arguments", normalizedInput)));
        return this;
    }

    /// <summary>
    /// Adds a tool output item to the input.
    /// </summary>
    /// <param name="callId">Tool call id.</param>
    /// <param name="output">Tool output text.</param>
    public ChatInput AddToolOutput(string callId, string output) {
        if (string.IsNullOrWhiteSpace(callId)) {
            throw new ArgumentException("Call id cannot be empty.", nameof(callId));
        }
        var normalizedCallId = callId.Trim();
        _items.Add(new JsonObject()
            .Add("type", "custom_tool_call_output")
            .Add("call_id", normalizedCallId)
            .Add("output", output ?? string.Empty));
        return this;
    }

    /// <summary>
    /// Adds a local image path item to the input.
    /// </summary>
    /// <param name="path">Local image path.</param>
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
    /// Adds a raw JSON item to the input.
    /// </summary>
    /// <param name="item">JSON item.</param>
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
