using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
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
    /// Optional routing role from the orchestration catalog.
    /// </summary>
    public string? RoutingRole { get; init; }
    /// <summary>
    /// Optional routing scope from the orchestration catalog.
    /// </summary>
    public string? RoutingScope { get; init; }
    /// <summary>
    /// Optional routing operation from the orchestration catalog.
    /// </summary>
    public string? RoutingOperation { get; init; }
    /// <summary>
    /// Optional routing entity from the orchestration catalog.
    /// </summary>
    public string? RoutingEntity { get; init; }
    /// <summary>
    /// Optional routing risk from the orchestration catalog.
    /// </summary>
    public string? RoutingRisk { get; init; }
    /// <summary>
    /// Optional routing source from the orchestration catalog.
    /// </summary>
    public string? RoutingSource { get; init; }
    /// <summary>
    /// Optional normalized domain-intent family token.
    /// </summary>
    public string? DomainIntentFamily { get; init; }
    /// <summary>
    /// Optional domain-intent action identifier.
    /// </summary>
    public string? DomainIntentActionId { get; init; }
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
    /// Indicates whether the tool is an orientation/pack-info tool.
    /// </summary>
    public bool IsPackInfoTool { get; init; }
    /// <summary>
    /// Indicates whether the tool is an environment-discovery tool.
    /// </summary>
    public bool IsEnvironmentDiscoverTool { get; init; }
    /// <summary>
    /// Whether the tool is write-capable (mutating).
    /// </summary>
    public bool IsWriteCapable { get; init; }
    /// <summary>
    /// Indicates whether the tool requires an authentication/runtime identity contract.
    /// </summary>
    public bool RequiresAuthentication { get; init; }
    /// <summary>
    /// Optional stable authentication contract identifier.
    /// </summary>
    public string? AuthenticationContractId { get; init; }
    /// <summary>
    /// Canonical authentication-related arguments projected from the contract.
    /// </summary>
    public string[] AuthenticationArguments { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Indicates whether the tool supports a connectivity/authentication probe workflow.
    /// </summary>
    public bool SupportsConnectivityProbe { get; init; }
    /// <summary>
    /// Optional helper tool name used for connectivity/authentication probing.
    /// </summary>
    public string? ProbeToolName { get; init; }
    /// <summary>
    /// Indicates whether the tool exposes a structured execution contract.
    /// </summary>
    public bool IsExecutionAware { get; init; }
    /// <summary>
    /// Optional stable execution contract identifier.
    /// </summary>
    public string? ExecutionContractId { get; init; }
    /// <summary>
    /// Human-readable execution locality classification.
    /// </summary>
    public string ExecutionScope { get; init; } = "local_only";
    /// <summary>
    /// Indicates whether the tool can execute in the local runtime.
    /// </summary>
    public bool SupportsLocalExecution { get; init; } = true;
    /// <summary>
    /// Indicates whether the tool can execute against remote targets or remote backends.
    /// </summary>
    public bool SupportsRemoteExecution { get; init; }
    /// <summary>
    /// Indicates whether the tool supports explicit target scoping.
    /// </summary>
    public bool SupportsTargetScoping { get; init; }
    /// <summary>
    /// Canonical target-scope arguments projected from the schema.
    /// </summary>
    public string[] TargetScopeArguments { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Indicates whether the tool supports remote-host targeting.
    /// </summary>
    public bool SupportsRemoteHostTargeting { get; init; }
    /// <summary>
    /// Canonical remote-host arguments projected from the schema.
    /// </summary>
    public string[] RemoteHostArguments { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Optional representative task examples projected from pack-owned catalog metadata.
    /// </summary>
    public string[] RepresentativeExamples { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Indicates whether the tool exposes a setup contract.
    /// </summary>
    public bool IsSetupAware { get; init; }
    /// <summary>
    /// Optional setup helper tool name.
    /// </summary>
    public string? SetupToolName { get; init; }
    /// <summary>
    /// Indicates whether the tool exposes outbound handoff routes.
    /// </summary>
    public bool IsHandoffAware { get; init; }
    /// <summary>
    /// Handoff target pack ids derived from the orchestration catalog.
    /// </summary>
    public string[] HandoffTargetPackIds { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Handoff target tool names derived from the orchestration catalog.
    /// </summary>
    public string[] HandoffTargetToolNames { get; init; } = System.Array.Empty<string>();
    /// <summary>
    /// Indicates whether the tool exposes recovery behavior.
    /// </summary>
    public bool IsRecoveryAware { get; init; }
    /// <summary>
    /// Indicates whether transient retry is supported.
    /// </summary>
    public bool SupportsTransientRetry { get; init; }
    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetryAttempts { get; init; }
    /// <summary>
    /// Recovery helper tool names from the orchestration catalog.
    /// </summary>
    public string[] RecoveryToolNames { get; init; } = System.Array.Empty<string>();
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
    private string _argumentsJson = "{}";
    private bool _argumentsJsonExplicitlySet;

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
    public string ArgumentsJson {
        get => _argumentsJson;
        init {
            _argumentsJson = string.IsNullOrWhiteSpace(value) ? "{}" : value;
            _argumentsJsonExplicitlySet = !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>
    /// Backward-compatible alias accepted from legacy payloads/object-initializers.
    /// </summary>
    [Obsolete("Use ArgumentsJson instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Input {
        init {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            if (!_argumentsJsonExplicitlySet) {
                _argumentsJson = value;
            }
        }
    }
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
