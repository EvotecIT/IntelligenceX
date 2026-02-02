using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Describes sandbox constraints for app-server execution.
/// </summary>
public sealed class SandboxPolicy {
    /// <summary>
    /// Initializes a sandbox policy using a boolean network access flag.
    /// </summary>
    /// <param name="type">Sandbox type identifier.</param>
    /// <param name="networkAccess">Whether network access is allowed.</param>
    /// <param name="writableRoots">Writable root paths.</param>
    public SandboxPolicy(string type, bool? networkAccess = null, IReadOnlyList<string>? writableRoots = null) {
        Type = Guard.NotNullOrWhiteSpace(type, nameof(type));
        NetworkAccess = networkAccess;
        WritableRoots = writableRoots;
    }

    /// <summary>
    /// Initializes a sandbox policy using a network access mode string.
    /// </summary>
    /// <param name="type">Sandbox type identifier.</param>
    /// <param name="networkAccessMode">Network access mode.</param>
    /// <param name="writableRoots">Writable root paths.</param>
    public SandboxPolicy(string type, string networkAccessMode, IReadOnlyList<string>? writableRoots = null) {
        Type = Guard.NotNullOrWhiteSpace(type, nameof(type));
        NetworkAccessMode = networkAccessMode;
        WritableRoots = writableRoots;
    }

    /// <summary>
    /// Gets the sandbox type identifier.
    /// </summary>
    public string Type { get; }
    /// <summary>
    /// Gets the network access flag when provided.
    /// </summary>
    public bool? NetworkAccess { get; }
    /// <summary>
    /// Gets the network access mode when provided.
    /// </summary>
    public string? NetworkAccessMode { get; }
    /// <summary>
    /// Gets the writable root paths.
    /// </summary>
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
