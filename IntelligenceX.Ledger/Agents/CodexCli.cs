using Microsoft.Win32;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Which Codex CLI the Ledger runs. Every Codex on a machine shares <c>~/.codex</c> (threads, state and
/// sign-in), so the Ledger uses the copy that ships inside the Codex desktop app, the same version as the app server the
/// app itself runs. A separate npm or Homebrew copy is used only where the app isn't installed.</summary>
internal static class CodexCli {
    private const string MacAppCli = "/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex";
    private const string WindowsPackages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string WindowsPackagePrefix = "OpenAI.Codex_";

    /// <summary>The desktop app's CLI when the app is installed, otherwise the configured executable. Resolved on every
    /// use because an app update moves the Windows package to a new folder.</summary>
    public static string Resolve(string configured) => DesktopApp() ?? configured;

    /// <summary>The Codex desktop app's own CLI: on Windows <c>app\resources\codex.exe</c> in the newest registered
    /// OpenAI.Codex package, on macOS the CLI inside ChatGPT.app. Null when the app isn't installed.</summary>
    public static string? DesktopApp() {
        if (OperatingSystem.IsMacOS()) return File.Exists(MacAppCli) ? MacAppCli : null;
        if (!OperatingSystem.IsWindows()) return null;
        try {
            using var packages = Registry.CurrentUser.OpenSubKey(WindowsPackages);
            if (packages is null) return null;
            var roots = new List<(Version Version, string Root)>();
            foreach (var name in packages.GetSubKeyNames()) {
                if (!name.StartsWith(WindowsPackagePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                using var package = packages.OpenSubKey(name);
                if (package?.GetValue("PackageRootFolder") is string root && PackageVersion(name) is { } version) roots.Add((version, root));
            }
            return WindowsCli(roots);
        } catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) {
            return null;
        }
    }

    /// <summary>The newest package whose CLI exists.</summary>
    internal static string? WindowsCli(IEnumerable<(Version Version, string Root)> packages, Func<string, bool>? exists = null) {
        exists ??= File.Exists;
        return packages.OrderByDescending(package => package.Version)
            .Select(package => Path.Combine(package.Root, "app", "resources", "codex.exe")).FirstOrDefault(exists);
    }

    /// <summary>The version in a package full name such as <c>OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0</c>.</summary>
    internal static Version? PackageVersion(string fullName) {
        var parts = fullName.Split('_');
        return parts.Length >= 2 && parts[0].Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(parts[1], out var version) ? version : null;
    }
}
