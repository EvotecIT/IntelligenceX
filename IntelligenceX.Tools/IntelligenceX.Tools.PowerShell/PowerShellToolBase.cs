using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Engines.PowerShell;
using IntelligenceX.Json;
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

    /// <summary>
    /// Adds language-neutral chaining/discovery metadata for read-only PowerShell runtime posture tools.
    /// </summary>
    protected static void AddReadOnlyRuntimeChainingMeta(
        JsonObject meta,
        string currentTool,
        IReadOnlyList<string> availableHosts,
        bool enabled,
        bool allowWrite) {
        if (meta is null) {
            throw new ArgumentNullException(nameof(meta));
        }

        var normalizedTool = string.IsNullOrWhiteSpace(currentTool) ? "powershell_environment_discover" : currentTool.Trim();
        var preferredHost = availableHosts.Count > 0 ? availableHosts[0] : "auto";

        var nextActions = new List<ToolNextActionModel>();
        if (!string.Equals(normalizedTool, "powershell_hosts", StringComparison.OrdinalIgnoreCase)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "powershell_hosts",
                reason: "enumerate_runtime_hosts_before_command_execution",
                mutating: false));
        }

        if (enabled) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "powershell_run",
                reason: "execute_read_only_runtime_probe",
                suggestedArguments: ToolChainingHints.Map(
                    ("host", preferredHost),
                    ("intent", "read_only")),
                mutating: false));
        }

        var chain = ToolChainingHints.Create(
            nextActions: nextActions,
            confidence: availableHosts.Count > 0 ? 0.90d : 0.62d,
            checkpoint: ToolChainingHints.Map(
                ("current_tool", normalizedTool),
                ("enabled", enabled),
                ("allow_write", allowWrite),
                ("available_hosts", availableHosts.Count),
                ("preferred_host", preferredHost)));

        var nextActionsJson = new JsonArray();
        for (var i = 0; i < chain.NextActions.Count; i++) {
            nextActionsJson.Add(ToolJson.ToJsonObjectSnakeCase(chain.NextActions[i]));
        }

        meta.Add("next_actions", nextActionsJson);
        meta.Add("discovery_status", ToolJson.ToJsonObjectSnakeCase(new {
            current_tool = normalizedTool,
            enabled,
            allow_write = allowWrite,
            available_hosts = availableHosts.Count,
            preferred_host = preferredHost
        }));
        meta.Add("chain_confidence", chain.Confidence);
    }
}
