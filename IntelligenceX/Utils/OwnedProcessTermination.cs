using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace IntelligenceX.Utils;

/// <summary>Terminates an SDK-owned process and, where supported, its attached descendants.</summary>
internal static class OwnedProcessTermination {
    internal static void KillTree(Process process) {
        if (process.HasExited) return;
#if NET5_0_OR_GREATER
        process.Kill(entireProcessTree: true);
#else
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            // Preserve ordinary clients' legacy root-process cleanup. Restricted treatment
            // rejects this platform/target combination before starting any process.
            process.Kill();
            if (!process.WaitForExit(5000)) throw new TimeoutException("Owned process did not exit during shutdown.");
            return;
        }
        // Framework/standard builds lack Kill(entireProcessTree). Windows taskkill owns
        // this operation; pass only the numeric id of the exact process we started.
        using var terminator = Process.Start(new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
            Arguments = "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Unable to start owned-process termination.");
        var stdout = terminator.StandardOutput.ReadToEndAsync();
        var stderr = terminator.StandardError.ReadToEndAsync();
        if (!terminator.WaitForExit(5000)) {
            terminator.Kill();
            throw new TimeoutException("Owned-process termination timed out.");
        }
        stdout.GetAwaiter().GetResult(); stderr.GetAwaiter().GetResult();
        if (terminator.ExitCode != 0 && !process.HasExited)
            throw new InvalidOperationException("Owned-process termination failed.");
#endif
        if (!process.WaitForExit(5000)) throw new TimeoutException("Owned process did not exit during shutdown.");
    }
}
