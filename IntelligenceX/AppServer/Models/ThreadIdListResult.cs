using System.Collections.Generic;

namespace IntelligenceX.AppServer.Models;

public sealed class ThreadIdListResult {
    public ThreadIdListResult(IReadOnlyList<string> data) {
        Data = data;
    }

    public IReadOnlyList<string> Data { get; }
}
