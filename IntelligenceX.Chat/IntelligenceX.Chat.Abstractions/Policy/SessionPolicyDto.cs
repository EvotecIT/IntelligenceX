using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Session policy banner data that a UI can render (read-only, allowed roots, enabled packs, etc.).
/// </summary>
public sealed record SessionPolicyDto {
    /// <summary>
    /// Whether the session is operating in a read-only mode (no writes implied).
    /// </summary>
    public required bool ReadOnly { get; init; }
    /// <summary>
    /// Allowed filesystem roots for file/evtx tools (if any).
    /// </summary>
    public string[] AllowedRoots { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled tool packs.
    /// </summary>
    public ToolPackInfoDto[] Packs { get; init; } = Array.Empty<ToolPackInfoDto>();
    /// <summary>
    /// Whether any dangerous/write tools are enabled.
    /// </summary>
    public required bool DangerousToolsEnabled { get; init; }
    /// <summary>
    /// Optional per-tool timeout in seconds (null/0 means no explicit timeout).
    /// </summary>
    public int? ToolTimeoutSeconds { get; init; }
    /// <summary>
    /// Optional per-turn timeout in seconds (null/0 means no explicit timeout).
    /// </summary>
    public int? TurnTimeoutSeconds { get; init; }
    /// <summary>
    /// Max tool-call rounds per user message.
    /// </summary>
    public required int MaxToolRounds { get; init; }
    /// <summary>
    /// Whether tool calls can be executed in parallel.
    /// </summary>
    public required bool ParallelTools { get; init; }
    /// <summary>
    /// Whether mutating tool calls are allowed to execute in parallel.
    /// </summary>
    public required bool AllowMutatingParallelToolCalls { get; init; }

    /// <summary>
    /// Optional maximum number of rows to show in table-like outputs (null/0 means no explicit limit).
    /// </summary>
    public int? MaxTableRows { get; init; }

    /// <summary>
    /// Optional maximum number of items to show in samples/lists (null/0 means no explicit limit).
    /// </summary>
    public int? MaxSample { get; init; }

    /// <summary>
    /// Whether outputs should be redacted (best-effort) for display/logging.
    /// </summary>
    public bool Redact { get; init; }

    /// <summary>
    /// Startup/bootstrap notices (for example plugin load warnings).
    /// </summary>
    public string[] StartupWarnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Effective plugin search roots used by the runtime.
    /// </summary>
    public string[] PluginSearchPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Runtime governance/auth policy snapshot active for the session.
    /// </summary>
    public SessionRuntimePolicyDto? RuntimePolicy { get; init; }

    /// <summary>
    /// Structured routing catalog diagnostics for the active registry.
    /// </summary>
    public SessionRoutingCatalogDiagnosticsDto? RoutingCatalog { get; init; }
}
