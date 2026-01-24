using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Utils;

namespace IntelligenceX.AppServer;

public sealed class SandboxPolicy {
    public SandboxPolicy(string type, bool? networkAccess = null, IReadOnlyList<string>? writableRoots = null) {
        Type = Guard.NotNullOrWhiteSpace(type, nameof(type));
        NetworkAccess = networkAccess;
        WritableRoots = writableRoots;
    }

    public SandboxPolicy(string type, string networkAccessMode, IReadOnlyList<string>? writableRoots = null) {
        Type = Guard.NotNullOrWhiteSpace(type, nameof(type));
        NetworkAccessMode = networkAccessMode;
        WritableRoots = writableRoots;
    }

    public string Type { get; }
    public bool? NetworkAccess { get; }
    public string? NetworkAccessMode { get; }
    public IReadOnlyList<string>? WritableRoots { get; }

    internal JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("type", Type);

        if (!string.IsNullOrWhiteSpace(NetworkAccessMode)) {
            obj.Add("networkAccess", NetworkAccessMode);
        } else if (NetworkAccess.HasValue) {
            obj.Add("networkAccess", NetworkAccess.Value);
        }

        if (WritableRoots is not null && WritableRoots.Count > 0) {
            var roots = new JsonArray();
            foreach (var root in WritableRoots) {
                roots.Add(root);
            }
            obj.Add("writableRoots", roots);
        }

        return obj;
    }
}
