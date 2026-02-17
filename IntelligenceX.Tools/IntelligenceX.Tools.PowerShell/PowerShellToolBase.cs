using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Engines.PowerShell;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Base class for IX.PowerShell tools.
/// </summary>
public abstract class PowerShellToolBase : ToolBase {
    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellToolBase"/> class.
    /// </summary>
    protected PowerShellToolBase(PowerShellToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>
    /// Pack options.
    /// </summary>
    protected PowerShellToolOptions Options { get; }

    /// <summary>
    /// Converts engine failures to standardized tool errors.
    /// </summary>
    protected static string ErrorFromFailure(PowerShellCommandQueryFailure? failure, string defaultMessage = "PowerShell runtime query failed.") {
        var code = failure?.Code ?? PowerShellCommandQueryFailureCode.QueryFailed;
        var message = string.IsNullOrWhiteSpace(failure?.Message) ? defaultMessage : failure!.Message;
        var hints = new List<string>();

        var toolCode = code switch {
            PowerShellCommandQueryFailureCode.InvalidRequest => "invalid_argument",
            PowerShellCommandQueryFailureCode.Cancelled => "cancelled",
            PowerShellCommandQueryFailureCode.HostNotAvailable => "host_unavailable",
            PowerShellCommandQueryFailureCode.Timeout => "timeout",
            _ => "query_failed"
        };

        if (code == PowerShellCommandQueryFailureCode.HostNotAvailable) {
            hints.Add("Use powershell_environment_discover or powershell_hosts to check host availability (pwsh/windows_powershell/cmd).");
        }
        if (code == PowerShellCommandQueryFailureCode.Timeout) {
            hints.Add("Increase timeout_ms for long-running commands.");
        }

        return ToolResponse.Error(
            errorCode: toolCode,
            error: message,
            hints: hints.Count == 0 ? null : hints,
            isTransient: code == PowerShellCommandQueryFailureCode.Timeout || code == PowerShellCommandQueryFailureCode.QueryFailed);
    }

    /// <summary>
    /// Returns runtime hosts available for powershell_run execution.
    /// </summary>
    protected static IReadOnlyList<string> GetAvailableRuntimeHosts() {
        var hosts = new List<string>(PowerShellCommandQueryExecutor.GetAvailableHosts());
        if (IsCmdHostAvailable()) {
            hosts.Add("cmd");
        }

        return hosts;
    }

    /// <summary>
    /// Returns true when cmd.exe is available on this machine.
    /// </summary>
    protected static bool IsCmdHostAvailable() {
        try {
            var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return File.Exists(cmdPath);
        } catch {
            return false;
        }
    }
}
