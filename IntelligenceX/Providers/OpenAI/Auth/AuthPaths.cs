using System;
using System.IO;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Provides common paths used for auth storage.
/// </summary>
public static class AuthPaths {
    /// <summary>
    /// Resolves the default auth bundle path, honoring INTELLIGENCEX_AUTH_PATH when set.
    /// </summary>
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
