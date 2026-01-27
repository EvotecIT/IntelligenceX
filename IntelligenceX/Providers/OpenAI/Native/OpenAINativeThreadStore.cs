using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeThreadStore {
    private readonly Dictionary<string, NativeThreadState> _threads = new(StringComparer.Ordinal);

    public NativeThreadState StartNew(string model) {
        var state = NativeThreadState.Create(model);
        _threads[state.Id] = state;
        return state;
    }

    public NativeThreadState Resume(string threadId, string model) {
        if (_threads.TryGetValue(threadId, out var existing)) {
            existing.Touch(model);
            return existing;
        }
        var state = NativeThreadState.Create(model, threadId);
        _threads[state.Id] = state;
        return state;
    }

    public bool TryGet(string threadId, out NativeThreadState state) {
        return _threads.TryGetValue(threadId, out state!);
    }
}

internal sealed class NativeThreadState {
    private NativeThreadState(string id, string sessionId, string model, long createdAtUnix, long updatedAtUnix) {
        Id = id;
        SessionId = sessionId;
        Model = model;
        CreatedAtUnix = createdAtUnix;
        UpdatedAtUnix = updatedAtUnix;
    }

    public string Id { get; }
    public string SessionId { get; }
    public string Model { get; private set; }
    public long CreatedAtUnix { get; }
    public long UpdatedAtUnix { get; private set; }
    public string? Preview { get; private set; }
    public List<JsonObject> Messages { get; } = new();
    public int MessageIndex { get; private set; }

    public static NativeThreadState Create(string model, string? id = null) {
        var threadId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id!;
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new NativeThreadState(threadId, sessionId, model, now, now);
    }

    public void Touch(string model) {
        Model = model;
        UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void UpdatePreview(string? preview) {
        if (!string.IsNullOrWhiteSpace(preview)) {
            Preview = preview;
        }
        UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public string NextMessageId() {
        var id = $"msg_{MessageIndex}";
        MessageIndex++;
        return id;
    }

    public ThreadInfo ToThreadInfo() {
        var raw = new JsonObject()
            .Add("id", Id)
            .Add("preview", Preview)
            .Add("model", Model)
            .Add("createdAt", CreatedAtUnix)
            .Add("updatedAt", UpdatedAtUnix);
        return ThreadInfo.FromJson(raw);
    }
}
