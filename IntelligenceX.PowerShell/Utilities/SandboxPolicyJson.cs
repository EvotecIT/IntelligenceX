using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

internal static class SandboxPolicyJson {
    public static JsonObject ToJson(SandboxPolicy policy) {
        var obj = new JsonObject()
            .Add("type", policy.Type);

        if (!string.IsNullOrWhiteSpace(policy.NetworkAccessMode)) {
            obj.Add("networkAccess", policy.NetworkAccessMode);
        } else if (policy.NetworkAccess.HasValue) {
            obj.Add("networkAccess", policy.NetworkAccess.Value);
        }

        var roots = policy.WritableRoots;
        if (roots is not null && roots.Count > 0) {
            var array = new JsonArray();
            foreach (var root in roots) {
                array.Add(root);
            }
            obj.Add("writableRoots", array);
        }

        return obj;
    }
}
