using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Tool definition DTO suitable for client display.
/// </summary>
public sealed record ToolDefinitionDto {
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Tool description.
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// Optional human-friendly display name.
    /// </summary>
    public string? DisplayName { get; init; }
    /// <summary>
    /// Optional category label (e.g. active-directory, system, event-log).
    /// </summary>
    public string? Category { get; init; }
    /// <summary>
    /// Optional tags used for discovery and routing.
    /// </summary>
    public string[]? Tags { get; init; }
    /// <summary>
    /// Optional tool-pack identifier (e.g. system, fs, eventlog, ad).
    /// </summary>
    public string? PackId { get; init; }
    /// <summary>
    /// Optional tool-pack display name.
    /// </summary>
    public string? PackName { get; init; }
    /// <summary>
    /// Optional tool-pack description.
    /// </summary>
    public string? PackDescription { get; init; }
    /// <summary>
    /// Optional tool-pack source classification.
    /// </summary>
    public ToolPackSourceKind? PackSourceKind { get; init; }
    /// <summary>
    /// JSON serialized input schema from the runtime tool definition.
    /// </summary>
    public string ParametersJson { get; init; } = "{}";
    /// <summary>
    /// Required argument names extracted from the input schema.
    /// </summary>
    public string[] RequiredArguments { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Optional flattened parameter metadata extracted from the input schema.
    /// </summary>
    public ToolParameterDto[] Parameters { get; init; } = System.Array.Empty<ToolParameterDto>();
}

/// <summary>
/// Flattened tool parameter metadata extracted from JSON schema.
/// </summary>
public sealed record ToolParameterDto {
    /// <summary>
    /// Parameter name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Parameter type hint (for example: string, integer, object, array).
    /// </summary>
    public string Type { get; init; } = "any";
    /// <summary>
    /// Optional parameter description.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Indicates whether the parameter is required.
    /// </summary>
    public bool Required { get; init; }
    /// <summary>
    /// Optional enum values represented as text.
    /// </summary>
    public string[]? EnumValues { get; init; }
    /// <summary>
    /// Optional JSON-encoded default value.
    /// </summary>
    public string? DefaultJson { get; init; }
    /// <summary>
    /// Optional JSON-encoded example value.
    /// </summary>
    public string? ExampleJson { get; init; }
}

/// <summary>
/// Tool call DTO emitted during a chat run.
/// </summary>
public sealed record ToolCallDto {
    /// <summary>
    /// Tool call id.
    /// </summary>
    public required string CallId { get; init; }
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// JSON-serialized arguments.
    /// </summary>
    public string ArgumentsJson { get; init; } = "{}";
}

/// <summary>
/// Tool output DTO emitted during a chat run.
/// </summary>
public sealed record ToolOutputDto {
    /// <summary>
    /// Tool call id.
    /// </summary>
    public required string CallId { get; init; }
    /// <summary>
    /// Tool output (string payload).
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// Optional parsed "ok" flag extracted from JSON tool output envelopes.
    /// </summary>
    public bool? Ok { get; init; }
    /// <summary>
    /// Optional stable error code extracted from JSON tool output envelopes.
    /// </summary>
    public string? ErrorCode { get; init; }
    /// <summary>
    /// Optional error message extracted from JSON tool output envelopes.
    /// </summary>
    public string? Error { get; init; }
    /// <summary>
    /// Optional remediation hints extracted from JSON tool output envelopes.
    /// </summary>
    public string[]? Hints { get; init; }
    /// <summary>
    /// Optional transient flag extracted from JSON tool output envelopes.
    /// </summary>
    public bool? IsTransient { get; init; }
    /// <summary>
    /// Optional markdown summary intended for UI display, extracted from JSON tool outputs.
    /// </summary>
    public string? SummaryMarkdown { get; init; }

    /// <summary>
    /// Optional meta JSON extracted from tool outputs (for UI rendering).
    /// </summary>
    public string? MetaJson { get; init; }
    /// <summary>
    /// Optional render-hints JSON extracted from tool outputs (for UI rendering).
    /// </summary>
    public string? RenderJson { get; init; }
    /// <summary>
    /// Optional structured failure JSON extracted from tool outputs.
    /// </summary>
    public string? FailureJson { get; init; }
}

/// <summary>
/// Envelope for tool calls and outputs captured in a chat run.
/// </summary>
public sealed record ToolRunDto {
    /// <summary>
    /// Tool calls executed.
    /// </summary>
    public IReadOnlyList<ToolCallDto> Calls { get; init; } = System.Array.Empty<ToolCallDto>();
    /// <summary>
    /// Tool outputs produced.
    /// </summary>
    public IReadOnlyList<ToolOutputDto> Outputs { get; init; } = System.Array.Empty<ToolOutputDto>();
}
