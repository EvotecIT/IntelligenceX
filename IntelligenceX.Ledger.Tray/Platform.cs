using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace IntelligenceX.Ledger.Tray;

/// <summary>Where the tray finds the Ledger's read settings and keeps its own settings and log.</summary>
internal sealed class TrayPaths {
    public TrayPaths(string? ledgerDirectory = null, string? clientConfig = null) {
        LedgerDirectory = ledgerDirectory ?? DefaultLedgerDirectory();
        ClientConfig = clientConfig ?? Path.Combine(LedgerDirectory, "client.json");
        Directory = Path.Combine(LedgerDirectory, "ledger-tray");
    }

    public string LedgerDirectory { get; }
    public string ClientConfig { get; }
    public string Directory { get; }
    public string SettingsFile => Path.Combine(Directory, "settings.json");
    public string LogFile => Path.Combine(Directory, "ledger-tray.log");

    /// <summary>The Ledger folder the installers use: %LOCALAPPDATA%\IntelligenceX\Ledger on Windows and
    /// ~/.local/share/intelligencex-ledger on a Mac.</summary>
    public static string DefaultLedgerDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelligenceX", "Ledger")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "intelligencex-ledger");

    /// <summary>This machine's Ledger name, from the reporter's settings beside the read settings.</summary>
    public string? MachineId() {
        foreach (var name in new[] { "reporter.json", "mac-reporter.json", "agent.json" }) {
            var path = Path.Combine(LedgerDirectory, name);
            try {
                if (!File.Exists(path) || new FileInfo(path).Length > 65_536) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.TryGetProperty("machineId", out var id) && id.ValueKind == JsonValueKind.String) return id.GetString();
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }
}

/// <summary>The tray's own settings: whether to notify. Everything else comes from the Ledger.</summary>
internal sealed class TraySettings {
    public bool Notify { get; set; } = true;

    public static TraySettings Load(string path) {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string path) {
        try {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { TrayLog.Write("Settings not saved: " + error.Message); }
    }
}

internal static class TrayLog {
    public static string? FilePath { get; set; }

    public static void Write(string message) {
        if (FilePath is not { } path) return;
        try {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576) File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Starting with the user's session: a LaunchAgent on macOS, the Run key on Windows. Both belong to the user, need no
/// administrator and are removed by switching the option off.
/// </summary>
internal static class LoginItem {
    public const string Label = "com.evotec.intelligencex.ledger-tray";
    private const string RunValue = "IX Ledger tray";

    private static string AgentPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public static bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public static bool IsEnabled => OperatingSystem.IsMacOS() ? File.Exists(AgentPath) : OperatingSystem.IsWindows() && ReadRun() is not null;

    public static void Set(bool enabled) {
        try {
            if (OperatingSystem.IsMacOS()) {
                if (!enabled) File.Delete(AgentPath);
                else if (Environment.ProcessPath is { } executable) {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
                    File.WriteAllText(AgentPath, Plist(executable));
                }
            } else if (OperatingSystem.IsWindows()) SetRun(enabled);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException) {
            TrayLog.Write("Start at login couldn't be changed: " + error.Message);
        }
    }

    /// <summary>KeepAlive only after a crash, so Quit from the menu stays quit until the next login.</summary>
    internal static string Plist(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
          </array>
          <key>EnvironmentVariables</key>
          <dict>
            <key>DOTNET_ROOT</key>
            <string>{SecurityElement.Escape(DotnetRoot())}</string>
          </dict>
          <key>RunAtLoad</key>
          <true/>
          <key>KeepAlive</key>
          <dict>
            <key>SuccessfulExit</key>
            <false/>
          </dict>
          <key>LimitLoadToSessionType</key>
          <string>Aqua</string>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>
        """;

    /// <summary>The .NET installation this process runs on. A LaunchAgent gets no shell environment, so the app host
    /// is told where it is.</summary>
    internal static string DotnetRoot() => Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root ? root
        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));

    [SupportedOSPlatform("windows")]
    private static string? ReadRun() {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(RunValue) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void SetRun(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (!enabled) key.DeleteValue(RunValue, throwOnMissingValue: false);
        else if (Environment.ProcessPath is { } executable) key.SetValue(RunValue, "\"" + executable + "\"");
    }
}

/// <summary>Opening folders, the official sign-in commands and system notifications.</summary>
internal static class Shell {
    public static void Reveal(string directory) {
        try {
            System.IO.Directory.CreateDirectory(directory);
            Start(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "explorer.exe", [directory]);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The official sign-in for a provider, as its user would type it.</summary>
    internal static string[] SignInArguments(string provider) => provider switch {
        "github" => ["auth", "login"], "claude" => ["auth", "login"], _ => ["login"]
    };

    /// <summary>
    /// Opens a terminal running the provider's own sign-in for the user to complete. Nothing is typed or answered for
    /// them; the tray only starts the command when they choose the menu item.
    /// </summary>
    public static void SignIn(string provider, string? binary, string directory) {
        var executable = binary ?? provider switch { "github" => "gh", _ => provider };
        var arguments = SignInArguments(provider);
        if (OperatingSystem.IsWindows()) {
            // A console of its own that stays open, so the user sees the result. cmd takes the command line as one
            // quoted argument and strips the outer quotes itself.
            try {
                Process.Start(new ProcessStartInfo("cmd.exe", "/k \"" + Quote(executable) + " " + string.Join(' ', arguments) + "\"") { UseShellExecute = true })?.Dispose();
            } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) {
                TrayLog.Write("Sign-in not started: " + error.Message);
            }
            return;
        }
        if (!OperatingSystem.IsMacOS()) return;
        try {
            System.IO.Directory.CreateDirectory(directory);
            var script = Path.Combine(directory, "sign-in-" + provider + ".command");
            File.WriteAllText(script, "#!/bin/zsh -l\n'" + executable.Replace("'", "'\\''") + "' " + string.Join(' ', arguments) + "\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Start("/usr/bin/open", ["-a", "Terminal", script]);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { TrayLog.Write("Sign-in not started: " + error.Message); }
    }

    private static string Quote(string path) => path.Contains(' ') ? "\"" + path + "\"" : path;

    /// <summary>A system notification: Notification Center on a Mac, a toast on Windows.</summary>
    public static void Notify(string title, string body) {
        if (OperatingSystem.IsMacOS()) {
            Start("/usr/bin/osascript", ["-e", "display notification " + AppleText(body) + " with title " + AppleText(title)]);
        } else if (OperatingSystem.IsWindows()) {
            var xml = "<toast><visual><binding template=\"ToastGeneric\"><text>" + SecurityElement.Escape(title) + "</text><text>" +
                SecurityElement.Escape(body) + "</text></binding></visual></toast>";
            // PowerShell's own app id shows a toast without registering one for the tray.
            var script = "$x=[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]::new();" +
                "$x.LoadXml('" + xml.Replace("'", "''") + "');" +
                "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]::" +
                "CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe')" +
                ".Show([Windows.UI.Notifications.ToastNotification]::new($x))";
            Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        }
    }

    internal static string AppleText(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Start(string file, string[] arguments) {
        try {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Process.Start(start)?.Dispose();
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) {
            TrayLog.Write("Couldn't start " + file + ": " + error.Message);
        }
    }
}
