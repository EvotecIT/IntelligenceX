using System;
using System.IO;

namespace IntelligenceX.Auth;

public static class AuthPaths {
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
