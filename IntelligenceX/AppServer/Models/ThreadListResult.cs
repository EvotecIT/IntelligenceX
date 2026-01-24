using System.Collections.Generic;

namespace IntelligenceX.AppServer.Models;

public sealed class ThreadListResult {
    public ThreadListResult(IReadOnlyList<ThreadInfo> data, string? nextCursor) {
        Data = data;
        NextCursor = nextCursor;
    }

    public IReadOnlyList<ThreadInfo> Data { get; }
    public string? NextCursor { get; }
}
