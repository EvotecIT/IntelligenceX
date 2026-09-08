using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>Creates credential files with private Unix permissions before any secret bytes are written.</summary>
internal static class PrivateAuthFile {
    internal static FileStream Create(string path) {
#if NET8_0_OR_GREATER
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        // O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC; constants differ between Linux and Darwin.
        int flags = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? 1 | 0x40 | 0x80 | 0x80000
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 1 | 0x200 | 0x800 | 0x1000000
            : throw new PlatformNotSupportedException("Private auth files require Windows, Linux, or macOS on this target framework.");
        int descriptor = Open(Encoding.UTF8.GetBytes(path + '\0'), flags, 0x180); // 0600, reduced further by the process umask.
        if (descriptor < 0) throw new IOException("Could not create a private authentication file.", new Win32Exception(Marshal.GetLastWin32Error()));
        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        try { return new FileStream(handle, FileAccess.Write); }
        catch { handle.Dispose(); throw; }
#endif
    }

#if !NET8_0_OR_GREATER
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(byte[] path, int flags, uint mode);
#endif
}
