using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Styling;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// <c>IntelligenceX.Ledger.Tray</c> starts the tray or menu bar app. <c>snapshot</c> prints what the menu would show and
/// exits, for checking a machine over SSH; <c>login on|off</c> sets start at login, as the menu does. <c>--ledger-dir</c> and <c>--config</c> point at another Ledger folder or
/// read settings; <c>--theme light|dark</c> forces the details window's theme, <c>--details</c> opens it at start and
/// <c>--render &lt;folder&gt;</c> draws the details page in light and dark and every icon offscreen, then exits. A second start leaves the first in charge.
/// </summary>
internal static partial class Program {
    [STAThread]
    public static int Main(string[] args) {
        string? directory = null, config = null, command = null;
        for (var i = 0; i < args.Length; i++) {
            switch (args[i]) {
                case "--ledger-dir" when i + 1 < args.Length: directory = args[++i]; break;
                case "--config" when i + 1 < args.Length: config = args[++i]; break;
                case "--theme" when i + 1 < args.Length:
                    TrayApp.Theme = args[++i] switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => null };
                    break;
                case "snapshot": command = "snapshot"; break;
                case "login" when i + 1 < args.Length && args[i + 1] is "on" or "off":
                    command = "login-" + args[++i];
                    break;
                case "--details": TrayApp.OpenDetails = true; break;
                case "--render" when i + 1 < args.Length: TrayApp.RenderTo = args[++i]; break;
                default:
                    if (!args[i].StartsWith("-psn_", StringComparison.Ordinal)) { Usage(); return 2; }
                    break;
            }
        }
        var paths = new TrayPaths(directory, config);
        LoginItem.Arguments = config is null ? ["--ledger-dir", paths.LedgerDirectory] : ["--ledger-dir", paths.LedgerDirectory, "--config", paths.ClientConfig];
        TrayLog.FilePath = paths.LogFile;
        if (command == "snapshot") return Snapshot(paths);
        if (command is "login-on" or "login-off") {
            // The same start-at-login the menu toggles, for the installer.
            LoginItem.Set(command == "login-on");
            return LoginItem.IsEnabled == (command == "login-on") ? 0 : 1;
        }
        using var instance = SingleInstance.TryTake(paths.Directory);
        if (instance is null) return 0; // Already running; the icon is there.
        TrayApp.Paths = paths;
        TrayLog.Write("Started for " + (paths.MachineId() ?? "unknown machine") + " reading " + paths.ClientConfig);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime([], Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TrayApp>()
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true })
        .LogToTrace();

    private static int Snapshot(TrayPaths paths) {
        AttachParentConsole();
        var overview = new LedgerPoller(paths.ClientConfig, paths.LedgerDirectory).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
        output.WriteLine(TrayView.Build(overview, paths.MachineId(), DateTimeOffset.UtcNow).Text());
        return overview.Health is null ? 1 : 0;
    }

    private static void Usage() {
        AttachParentConsole();
        Console.Error.WriteLine("IntelligenceX.Ledger.Tray [snapshot | login on|off] [--ledger-dir <folder>] [--config <client.json>] [--theme light|dark] [--details] [--render <folder>]");
    }

    /// <summary>A Windows GUI build has no console of its own; commands write to the one they were started from.</summary>
    private static void AttachParentConsole() {
        if (OperatingSystem.IsWindows()) AttachConsole(-1);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}

/// <summary>One tray per Ledger folder: a second start finds the lock taken and leaves the first in charge.</summary>
internal sealed class SingleInstance : IDisposable {
    private readonly FileStream _lock;

    private SingleInstance(FileStream file) => _lock = file;

    public static SingleInstance? TryTake(string directory) {
        try {
            Directory.CreateDirectory(directory);
            return new SingleInstance(new FileStream(Path.Combine(directory, "tray.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose));
        } catch (IOException) {
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();
}
