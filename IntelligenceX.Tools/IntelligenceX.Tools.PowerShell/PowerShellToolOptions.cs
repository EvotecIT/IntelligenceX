using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Runtime options for IX.PowerShell tools.
/// </summary>
public sealed class PowerShellToolOptions : IToolPackRuntimeConfigurable, IToolPackRuntimeOptionTarget {
    private static readonly string[] RuntimeOptionKeyValues = {
        "powershell",
        "powershell_runtime"
    };

    /// <summary>
    /// Enables runtime command/script execution for this pack.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Default command timeout in milliseconds.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Maximum timeout accepted from tool input in milliseconds.
    /// </summary>
    public int MaxTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// Default combined output cap in characters.
    /// </summary>
    public int DefaultMaxOutputChars { get; set; } = 50_000;

    /// <summary>
    /// Maximum combined output cap accepted from tool input.
    /// </summary>
    public int MaxOutputChars { get; set; } = 250_000;

    /// <summary>
    /// Allows read-write PowerShell execution intent.
    /// </summary>
    /// <remarks>
    /// When false, tool calls must remain read-only and mutating payloads are rejected.
    /// </remarks>
    public bool AllowWrite { get; set; }

    /// <summary>
    /// Requires explicit <c>allow_write=true</c> when <c>intent=read_write</c>.
    /// </summary>
    public bool RequireExplicitWriteFlag { get; set; } = true;

    /// <summary>
    /// Enables heuristic detection for mutating commands/scripts.
    /// </summary>
    public bool EnableMutationHeuristic { get; set; } = true;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <inheritdoc />
    public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
        ArgumentNullException.ThrowIfNull(context);

        Enabled = true;
        DefaultTimeoutMs = context.PowerShellDefaultTimeoutMs;
        MaxTimeoutMs = context.PowerShellMaxTimeoutMs;
        DefaultMaxOutputChars = context.PowerShellDefaultMaxOutputChars;
        MaxOutputChars = context.PowerShellMaxOutputChars;
        AllowWrite = context.PowerShellAllowWrite;
    }

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        if (DefaultTimeoutMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeoutMs), "DefaultTimeoutMs must be greater than zero.");
        }

        if (MaxTimeoutMs < DefaultTimeoutMs) {
            throw new ArgumentOutOfRangeException(nameof(MaxTimeoutMs), "MaxTimeoutMs must be greater than or equal to DefaultTimeoutMs.");
        }

        if (DefaultMaxOutputChars <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultMaxOutputChars), "DefaultMaxOutputChars must be greater than zero.");
        }

        if (MaxOutputChars < DefaultMaxOutputChars) {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputChars), "MaxOutputChars must be greater than or equal to DefaultMaxOutputChars.");
        }
    }
}
