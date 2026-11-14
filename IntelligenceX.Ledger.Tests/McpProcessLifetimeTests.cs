using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Xunit;
using Xunit.Abstractions;

namespace IntelligenceX.Ledger.Tests;

public sealed class McpProcessLifetimeTests(ITestOutputHelper output) {
    [WindowsTheory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task HostProcessTreeExitsWhenInputEndsOrOriginalParentExits(bool useAppHost, bool exitParent) {
        var directory = LedgerTestPaths.NewDirectory("mcp-lifetime");
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "client.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new {
            Endpoint = "http://127.0.0.1:1/", ReadToken = new string('r', 32)
        }));
        // An explicit artifact also permits qualifying an installed launcher without
        // changing its settings. The normal test run uses the project reference.
        var hostPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_MCP_TEST_HOST") ??
            Path.Combine(AppContext.BaseDirectory, "IntelligenceX.Ledger.Mcp.exe");
        if (!useAppHost) hostPath = Path.ChangeExtension(hostPath, ".dll");
        Assert.True(File.Exists(hostPath), $"MCP host artifact is missing: {hostPath}");
        var command = useAppHost ? $"\"{hostPath}\"" : $"dotnet \"{hostPath}\"";
        var start = new ProcessStartInfo("cmd.exe") {
            Arguments = $"/d /q /s /c \"start \"\" /b /wait {command} --config \"{config}\"\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var parent = Process.Start(start)!;
        _ = parent.SafeHandle;
        var parentStartedAtUtc = parent.StartTime.ToUniversalTime();
        output.WriteLine($"Original parent PID {parent.Id}, started {parentStartedAtUtc:O}");
        var stderr = parent.StandardError.ReadToEndAsync();
        var children = new List<Process>();
        try {
            using var readiness = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await parent.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"lifetime-regression","version":"1"}}}
                """);
            await parent.StandardInput.FlushAsync(readiness.Token);
            var response = await parent.StandardOutput.ReadLineAsync(readiness.Token);
            Assert.NotNull(response);
            using var initialized = JsonDocument.Parse(response);
            Assert.Equal(1, initialized.RootElement.GetProperty("id").GetInt32());
            Assert.Equal("IntelligenceX.Ledger.Mcp", initialized.RootElement.GetProperty("result")
                .GetProperty("serverInfo").GetProperty("name").GetString());

            CaptureDescendants(parent.Id, children);
            Assert.NotEmpty(children);
            var hostProcess = Assert.Single(children, child => string.Equals(Path.GetFileName(child.MainModule!.FileName),
                useAppHost ? Path.GetFileName(hostPath) : "dotnet.exe", StringComparison.OrdinalIgnoreCase));
            foreach (var child in children) {
                output.WriteLine($"Owned child PID {child.Id}, started {child.StartTime.ToUniversalTime():O}, image {child.ProcessName}");
                Assert.False(child.HasExited);
            }
            var stdout = parent.StandardOutput.ReadToEndAsync();
            var elapsed = Stopwatch.StartNew();
            Assert.False(parent.HasExited);
            if (exitParent) {
                // Kill only the launcher. The test keeps the inherited pipe's write
                // end open so EOF cannot hide a missing original-parent observer.
                parent.Kill();
                await parent.WaitForExitAsync(readiness.Token);
                Assert.True(parent.StandardInput.BaseStream.CanWrite);
            } else {
                parent.StandardInput.Close();
            }

            foreach (var child in children.OrderBy(child => child.Id != hostProcess.Id)) {
                var exited = await ExitsWithinAsync(child, TimeSpan.FromSeconds(8));
                output.WriteLine($"Child PID {child.Id} exited={exited} after {elapsed.Elapsed.TotalSeconds:F3}s");
                Assert.True(exited, $"MCP PID {child.Id} survived client disconnect (original parent {parent.Id}, started {parentStartedAtUtc:O}).");
                Assert.Equal(0, child.ExitCode);
            }
            Assert.True(await ExitsWithinAsync(parent, TimeSpan.FromSeconds(8)), "Windowless launcher survived its MCP host.");
            Assert.Equal(string.Empty, await stderr.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal(string.Empty, await stdout.WaitAsync(TimeSpan.FromSeconds(8)));
        } finally {
            // Capture any children even when initialization failed. Retained handles
            // keep cleanup bound to these original processes despite PID reuse.
            if (!parent.HasExited && children.Count == 0) CaptureDescendants(parent.Id, children);
            try {
                await Task.WhenAll(children.Select(async child => {
                    try { await StopOwnedAsync(child); }
                    finally { child.Dispose(); }
                }));
            } finally {
                await StopOwnedAsync(parent);
                Directory.Delete(directory, true);
            }
        }
    }

    private static async Task<bool> ExitsWithinAsync(Process process, TimeSpan timeout) {
        using var deadline = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(deadline.Token); return true; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return false; }
    }

    private async Task StopOwnedAsync(Process process) {
        if (process.HasExited) return;
        output.WriteLine($"Cleaning up owned PID {process.Id}, started {process.StartTime.ToUniversalTime():O}");
        process.Kill();
        Assert.True(await ExitsWithinAsync(process, TimeSpan.FromSeconds(8)), $"Owned PID {process.Id} could not be cleaned up.");
    }

    private static void CaptureDescendants(int parentId, List<Process> descendants) {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        var entries = new List<(int Id, int ParentId)>();
        if (!Process32First(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        do { entries.Add(((int)entry.ProcessId, (int)entry.ParentProcessId)); }
        while (Process32Next(snapshot, ref entry));
        var parents = new HashSet<int> { parentId };
        AddChildren(parentId);

        void AddChildren(int id) {
            foreach (var child in entries.Where(entry => entry.ParentId == id && !parents.Contains(entry.Id))) {
                var process = Process.GetProcessById(child.Id);
                _ = process.SafeHandle;
                descendants.Add(process);
                parents.Add(child.Id);
                AddChildren(child.Id);
            }
        }
    }

    private sealed class WindowsTheoryAttribute : TheoryAttribute {
        public WindowsTheoryAttribute() {
            if (!OperatingSystem.IsWindows()) Skip = "Qualifies the Windows windowless launcher process tree.";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry {
        internal uint Size, Usage, ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId, Threads, ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string ExeFile;
    }
}
