using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>
/// Windows starts scheduled tasks below normal priority by default, and that lowers the process's disk and memory
/// priority as well as its CPU priority. A Ledger role at low disk priority stalls for minutes whenever another process
/// writes heavily to the same volume, so each long-running role raises itself back to normal at startup.
/// </summary>
internal static class ProcessPriority {
    /// <summary>Priorities after the adjustment. Disk and memory are null off Windows or when they can't be read.</summary>
    public sealed record State(string Cpu, int? Disk, int? Memory);

    private const int ProcessIoPriority = 33, IoPriorityNormal = 2;
    private const int ProcessMemoryPriority = 0, MemoryPriorityNormal = 5;

    public static State EnsureNormal() {
        using var process = Process.GetCurrentProcess();
        if (!OperatingSystem.IsWindows()) return new(process.PriorityClass.ToString(), null, null);
        try {
            if (process.PriorityClass is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal)
                process.PriorityClass = ProcessPriorityClass.Normal;
        } catch (System.ComponentModel.Win32Exception) { }
        if (ReadDisk(process.Handle) is < IoPriorityNormal) {
            var value = IoPriorityNormal;
            _ = NtSetInformationProcess(process.Handle, ProcessIoPriority, ref value, sizeof(int));
        }
        if (ReadMemory(process.Handle) is < MemoryPriorityNormal) {
            var value = MemoryPriorityNormal;
            _ = SetProcessInformation(process.Handle, ProcessMemoryPriority, ref value, sizeof(int));
        }
        process.Refresh();
        return new(process.PriorityClass.ToString(), ReadDisk(process.Handle), ReadMemory(process.Handle));
    }

    private static int? ReadDisk(IntPtr handle) =>
        NtQueryInformationProcess(handle, ProcessIoPriority, out var value, sizeof(int), out _) == 0 ? value : null;

    private static int? ReadMemory(IntPtr handle) =>
        GetProcessInformation(handle, ProcessMemoryPriority, out var value, sizeof(int)) ? value : null;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out int value, int length, out int returned);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(IntPtr process, int informationClass, ref int value, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(IntPtr process, int informationClass, out int value, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref int value, int length);
}
