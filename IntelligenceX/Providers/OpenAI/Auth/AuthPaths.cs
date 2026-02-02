using System;
using System.IO;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Resolves default storage paths for IntelligenceX auth bundles.
/// </summary>
public static class AuthPaths {
    /// <summary>Resolves the default auth.json path.</summary>
    public static string ResolveAuthPath() {
        var overridePath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            return overridePath;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        return Path.Combine(home, ".intelligencex", "auth.json");
    }
}
