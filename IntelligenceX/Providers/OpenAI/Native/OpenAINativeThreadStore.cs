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
    public long SessionInputTokens { get; private set; }
    public long SessionOutputTokens { get; private set; }
    public long SessionTotalTokens { get; private set; }
    public int UsageTurnCount { get; private set; }

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

    public void AddUsage(TurnUsage? usage) {
        if (usage is null) {
            return;
        }
        if (usage.InputTokens.HasValue) {
            SessionInputTokens += Math.Max(0, usage.InputTokens.Value);
        }
        if (usage.OutputTokens.HasValue) {
            SessionOutputTokens += Math.Max(0, usage.OutputTokens.Value);
        }
        if (usage.TotalTokens.HasValue) {
            SessionTotalTokens += Math.Max(0, usage.TotalTokens.Value);
        } else {
            var derivedTotal = Math.Max(0, usage.InputTokens ?? 0) + Math.Max(0, usage.OutputTokens ?? 0);
            if (derivedTotal > 0) {
                SessionTotalTokens += derivedTotal;
            }
        }
        UsageTurnCount++;
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
        if (UsageTurnCount > 0) {
            raw.Add("usageSummary", new JsonObject()
                .Add("turns", UsageTurnCount)
                .Add("input_tokens", SessionInputTokens)
                .Add("output_tokens", SessionOutputTokens)
                .Add("total_tokens", SessionTotalTokens));
        }
        return ThreadInfo.FromJson(raw);
    }
}
