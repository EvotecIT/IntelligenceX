using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace IntelligenceX.Tray.Services;

public sealed class WindowsStartupRegistrationService {
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "IntelligenceX Tray";
    private const string StartupTaskId = "IntelligenceXTrayStartup";
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    public async Task<StartupRegistrationState> GetStateAsync() {
        if (!OperatingSystem.IsWindows()) {
            return new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.Unsupported,
                Message: "Windows startup registration is only available on Windows.");
        }

        if (HasPackageIdentity()) {
            return await GetPackagedStateAsync().ConfigureAwait(false);
        }

        return GetRegistryState();
    }

    public async Task<StartupRegistrationChangeResult> SetEnabledAsync(bool enabled) {
        if (!OperatingSystem.IsWindows()) {
            var unsupportedState = new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.Unsupported,
                Message: "Windows startup registration is only available on Windows.");
            return new StartupRegistrationChangeResult(
                Applied: false,
                State: unsupportedState,
                Message: unsupportedState.Message);
        }

        if (HasPackageIdentity()) {
            return await SetPackagedEnabledAsync(enabled).ConfigureAwait(false);
        }

        return SetRegistryEnabled(enabled);
    }

    private static async Task<StartupRegistrationState> GetPackagedStateAsync() {
        try {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return MapPackagedState(task.State);
        }
        catch (Exception ex) {
            return new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "The packaged startup task is unavailable: " + ex.Message);
        }
    }

    private static StartupRegistrationState GetRegistryState() {
        try {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = runKey?.GetValue(EntryName) as string;
            return new StartupRegistrationState(
                IsEnabled: CommandTargetsProcessPath(value, ResolveProcessPath()),
                CanChange: true,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.RegistryRunKey);
        } catch {
            return new StartupRegistrationState(
                IsEnabled: false,
                CanChange: true,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.RegistryRunKey,
                Message: "Unable to inspect the current user's Windows startup registration.");
        }
    }

    private static async Task<StartupRegistrationChangeResult> SetPackagedEnabledAsync(bool enabled) {
        try {
            var task = await StartupTask.GetAsync(StartupTaskId);
            StartupRegistrationState state;
            if (enabled) {
                var requestedState = await task.RequestEnableAsync();
                state = MapPackagedState(requestedState);
            } else {
                task.Disable();
                state = MapPackagedState(task.State);
            }

            return new StartupRegistrationChangeResult(
                Applied: state.IsEnabled == enabled,
                State: state,
                Message: BuildPackagedChangeMessage(enabled, state));
        } catch (Exception ex) {
            var failureState = new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "Unable to update the packaged startup task: " + ex.Message);
            return new StartupRegistrationChangeResult(
                Applied: false,
                State: failureState,
                Message: failureState.Message);
        }
    }

    private static StartupRegistrationChangeResult SetRegistryEnabled(bool enabled) {
        try {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (runKey is null) {
                var unavailableState = new StartupRegistrationState(
                    IsEnabled: false,
                    CanChange: true,
                    RequiresManualAction: false,
                    Kind: StartupRegistrationKind.RegistryRunKey,
                    Message: "The current user's Windows startup registry key is unavailable.");
                return new StartupRegistrationChangeResult(
                    Applied: false,
                    State: unavailableState,
                    Message: unavailableState.Message);
            }

            if (enabled) {
                runKey.SetValue(EntryName, BuildCommand(), RegistryValueKind.String);
            } else {
                runKey.DeleteValue(EntryName, throwOnMissingValue: false);
            }

            var state = GetRegistryState();
            return new StartupRegistrationChangeResult(
                Applied: state.IsEnabled == enabled,
                State: state,
                Message: state.IsEnabled == enabled ? null : "Unable to update the Windows startup registration for the tray app.");
        } catch {
            var failureState = new StartupRegistrationState(
                IsEnabled: false,
                CanChange: true,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.RegistryRunKey,
                Message: "Unable to update the Windows startup registry key for the tray app.");
            return new StartupRegistrationChangeResult(
                Applied: false,
                State: failureState,
                Message: failureState.Message);
        }
    }

    private static StartupRegistrationState MapPackagedState(StartupTaskState state) {
        return state.ToString() switch {
            "Enabled" or "EnabledByUser" => new StartupRegistrationState(
                IsEnabled: true,
                CanChange: true,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.PackagedStartupTask),
            "EnabledByPolicy" => new StartupRegistrationState(
                IsEnabled: true,
                CanChange: false,
                RequiresManualAction: true,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "Windows startup is enabled by policy for this packaged tray app."),
            "Disabled" => new StartupRegistrationState(
                IsEnabled: false,
                CanChange: true,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.PackagedStartupTask),
            "DisabledByUser" => new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: true,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "Windows startup was disabled in Task Manager. Re-enable it there to let the packaged tray app start automatically."),
            "DisabledByPolicy" => new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: true,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "Windows startup is blocked by policy for this packaged tray app."),
            _ => new StartupRegistrationState(
                IsEnabled: false,
                CanChange: false,
                RequiresManualAction: false,
                Kind: StartupRegistrationKind.PackagedStartupTask,
                Message: "The packaged startup task returned an unknown Windows state.")
        };
    }

    private static string? BuildPackagedChangeMessage(bool enabled, StartupRegistrationState state) {
        if (state.IsEnabled == enabled) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(state.Message)) {
            return state.Message;
        }

        return enabled
            ? "Windows did not enable startup for the packaged tray app."
            : "Windows did not disable startup for the packaged tray app.";
    }

    private static bool HasPackageIdentity() {
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        if (result == AppModelErrorNoPackage) {
            return false;
        }

        if (result == ErrorInsufficientBuffer && length > 0) {
            var packageFullName = new StringBuilder(length);
            return GetCurrentPackageFullName(ref length, packageFullName) == 0;
        }

        return result == 0;
    }

    private static string BuildCommand() {
        return "\"" + ResolveProcessPath() + "\"";
    }

    private static string ResolveProcessPath() {
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory)) {
            var appHostPath = Path.Combine(AppContext.BaseDirectory, "IntelligenceX.Tray.exe");
            if (File.Exists(appHostPath)) {
                return appHostPath;
            }
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) {
            processPath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(processPath)) {
            throw new InvalidOperationException("Unable to resolve the current tray executable path for startup registration.");
        }

        return processPath;
    }

    internal static bool CommandTargetsProcessPath(string? commandValue, string processPath) {
        if (string.IsNullOrWhiteSpace(processPath)) {
            return false;
        }

        var trimmed = commandValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return false;
        }

        var quotedProcessPath = "\"" + processPath + "\"";
        if (trimmed.StartsWith(quotedProcessPath, StringComparison.OrdinalIgnoreCase)) {
            return trimmed.Length == quotedProcessPath.Length
                   || char.IsWhiteSpace(trimmed[quotedProcessPath.Length]);
        }

        if (!trimmed.StartsWith(processPath, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return trimmed.Length == processPath.Length
               || char.IsWhiteSpace(trimmed[processPath.Length]);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
