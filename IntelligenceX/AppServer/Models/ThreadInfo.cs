using System;

namespace IntelligenceX.AppServer.Models;

public sealed class ThreadInfo {
    public ThreadInfo(string id, string? preview, string? model, DateTimeOffset? createdAt) {
        Id = id;
        Preview = preview;
        Model = model;
        CreatedAt = createdAt;
    }

    public string Id { get; }
    public string? Preview { get; }
    public string? Model { get; }
    public DateTimeOffset? CreatedAt { get; }
}
