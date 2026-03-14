using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.PowerShell;

internal static class PowerShellToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_environment_discover"] = new[] {
                "discover available shell hosts and execution policy before running a local command or script"
            },
            ["powershell_hosts"] = new[] {
                "list which local shell hosts are available before choosing pwsh, Windows PowerShell, or cmd"
            },
            ["powershell_run"] = new[] {
                "run a bounded local PowerShell or cmd check with explicit read-only or read-write intent"
            }
        };
}
