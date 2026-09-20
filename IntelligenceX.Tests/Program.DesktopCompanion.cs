using IntelligenceX.Desktop;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestDesktopCompanionResolvesOnlyPairedBundle() {
        var root = Path.Combine(Path.GetTempPath(), "ix-companion-" + Guid.NewGuid().ToString("N"));
        var chat = Path.Combine(root, "Chat");
        var tray = Path.Combine(root, "Tray");
        try {
            Directory.CreateDirectory(chat);
            Directory.CreateDirectory(tray);
            var trayExe = Path.Combine(tray, "IntelligenceX.Tray.exe");
            File.WriteAllText(trayExe, string.Empty);
            AssertEqual(trayExe, DesktopCompanionLauncher.ResolveExecutable(chat, DesktopCompanion.Tray), "paired sibling resolved");
            AssertEqual(trayExe, DesktopCompanionLauncher.ResolveExecutable(root, DesktopCompanion.Tray), "bundle child resolved");
            AssertEqual(trayExe, DesktopCompanionLauncher.ResolveExecutable(tray, DesktopCompanion.Tray), "same directory resolved");
            AssertEqual(true, DesktopCompanionLauncher.ResolveExecutable(tray, DesktopCompanion.Chat) is null, "missing companion is explicit");
            var unrelated = Path.Combine(root, "Unrelated");
            Directory.CreateDirectory(unrelated);
            AssertEqual(true, DesktopCompanionLauncher.ResolveExecutable(unrelated, DesktopCompanion.Tray) is null, "does not search arbitrary parents");
        } finally {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
