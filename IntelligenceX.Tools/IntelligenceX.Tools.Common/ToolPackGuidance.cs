using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Typed contract for pack-level guidance returned by <c>*_pack_info</c> tools.
/// </summary>
public sealed class ToolPackInfoModel {
    private IReadOnlyList<ToolPackToolCatalogEntryModel> _toolCatalog = Array.Empty<ToolPackToolCatalogEntryModel>();

    /// <summary>
    /// Pack identifier (for example: <c>active_directory</c>, <c>system</c>).
    /// </summary>
    public string Pack { get; init; } = string.Empty;

    /// <summary>
    /// Canonical engine/library backing this pack.
    /// </summary>
    public string Engine { get; init; } = string.Empty;

    /// <summary>
    /// Guidance contract version for model-side compatibility checks.
    /// </summary>
    public int GuidanceVersion { get; init; } = 1;

    /// <summary>
    /// Output contract that clarifies raw payload and view projection semantics.
    /// </summary>
    public ToolPackOutputContractModel OutputContract { get; init; } = new();

    /// <summary>
    /// Optional setup/environment hints.
    /// </summary>
    public object? SetupHints { get; init; }

    /// <summary>
    /// Optional safety boundary hints.
    /// </summary>
    public object? Safety { get; init; }

    /// <summary>
    /// Optional pack-level limits/caps hints.
    /// </summary>
    public object? Limits { get; init; }

    /// <summary>
    /// Optional recommended flow for planning.
    /// </summary>
    public IReadOnlyList<string> RecommendedFlow { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Structured planning steps (goal + suggested tools) for model-driven tool orchestration.
    /// </summary>
    public IReadOnlyList<ToolPackFlowStepModel> RecommendedFlowSteps { get; init; } = Array.Empty<ToolPackFlowStepModel>();

    /// <summary>
    /// Structured capability catalog for this pack.
    /// </summary>
    public IReadOnlyList<ToolPackCapabilityModel> Capabilities { get; init; } = Array.Empty<ToolPackCapabilityModel>();

    /// <summary>
    /// Structured entity handoff guidance for cross-tool and cross-pack correlation.
    /// </summary>
    public IReadOnlyList<ToolPackEntityHandoffModel> EntityHandoffs { get; init; } = Array.Empty<ToolPackEntityHandoffModel>();

    /// <summary>
    /// Tool-level catalog derived from runtime registrations and schemas.
    /// </summary>
    public IReadOnlyList<ToolPackToolCatalogEntryModel> ToolCatalog {
        get => _toolCatalog;
        init => _toolCatalog = ToolPackGuidance.NormalizeToolCatalogContract(value);
    }

    /// <summary>
    /// Registered tool names for this pack.
    /// </summary>
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional free-form additional note.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Shared output-semantics contract for pack guidance.
/// </summary>
public sealed class ToolPackOutputContractModel {
    /// <summary>
    /// Describes how raw payloads should be used by the model.
    /// </summary>
    public string RawPayloadPolicy { get; init; } = "Preserve raw payload fields for model reasoning and correlation.";

    /// <summary>
    /// Describes projection argument semantics.
    /// </summary>
    public string ViewProjectionPolicy { get; init; } = "Projection arguments are optional and view-only.";

    /// <summary>
    /// Suffix used for projected/presentation-only row fields.
    /// </summary>
    public string ViewFieldSuffix { get; init; } = "_view";

    /// <summary>
    /// Optional explicit correlation guidance.
    /// </summary>
    public string? CorrelationGuidance { get; init; }
}

/// <summary>
/// Structured planning step in pack guidance.
/// </summary>
public sealed class ToolPackFlowStepModel {
    /// <summary>
    /// Goal for this planning step.
    /// </summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>
    /// Suggested tools for this step.
    /// </summary>
    public IReadOnlyList<string> SuggestedTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional notes for the step.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Structured capability descriptor in pack guidance.
/// </summary>
public sealed class ToolPackCapabilityModel {
    /// <summary>
    /// Stable capability identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable capability summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Primary tools for this capability.
    /// </summary>
    public IReadOnlyList<string> PrimaryTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional notes for this capability.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Structured descriptor for handing entity evidence from source tools to follow-up tools.
/// </summary>
public sealed class ToolPackEntityHandoffModel {
    /// <summary>
    /// Stable handoff identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable handoff summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Entity kinds promoted by this handoff (for example user/computer/domain).
    /// </summary>
    public IReadOnlyList<string> EntityKinds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Source tools that emit the handoff entities.
    /// </summary>
    public IReadOnlyList<string> SourceTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Follow-up tools that should consume the handoff entities.
    /// </summary>
    public IReadOnlyList<string> TargetTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional field-level mapping hints between source payload fields and target arguments.
    /// </summary>
    public IReadOnlyList<ToolPackEntityFieldMappingModel> FieldMappings { get; init; } = Array.Empty<ToolPackEntityFieldMappingModel>();

    /// <summary>
    /// Optional notes for this handoff.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Field mapping hint for entity handoffs.
/// </summary>
public sealed class ToolPackEntityFieldMappingModel {
    /// <summary>
    /// Source payload field path or symbolic field name.
    /// </summary>
    public string SourceField { get; init; } = string.Empty;

    /// <summary>
    /// Target tool argument name.
    /// </summary>
    public string TargetArgument { get; init; } = string.Empty;

    /// <summary>
    /// Optional normalization hint for model/tool routing.
    /// </summary>
    public string? Normalization { get; init; }
}

/// <summary>
/// Structured routing taxonomy for model-side tool selection.
/// </summary>
public sealed class ToolPackToolRoutingModel {
    /// <summary>
    /// Primary scope where the tool operates (for example: host/domain/file/message/pack).
    /// </summary>
    public string Scope { get; init; } = ToolRoutingTaxonomy.ScopeGeneral;

    /// <summary>
    /// Primary operation kind (for example: query/search/list/read/write/execute/guide).
    /// </summary>
    public string Operation { get; init; } = ToolRoutingTaxonomy.OperationRead;

    /// <summary>
    /// Primary entity class handled by this tool.
    /// </summary>
    public string Entity { get; init; } = ToolRoutingTaxonomy.EntityResource;

    /// <summary>
    /// Relative execution risk profile (for example: low/medium/high).
    /// </summary>
    public string Risk { get; init; } = ToolRoutingTaxonomy.RiskLow;

    /// <summary>
    /// Routing source marker (<c>explicit</c> when overridden, otherwise <c>inferred</c>).
    /// </summary>
    public string Source { get; init; } = ToolRoutingTaxonomy.SourceInferred;
}

/// <summary>
/// Normalized outbound handoff edge derived from tool-owned handoff contracts.
/// </summary>
public sealed class ToolPackToolHandoffEdgeModel {
    /// <summary>
    /// Target pack id for this handoff edge.
    /// </summary>
    public string TargetPackId { get; init; } = string.Empty;

    /// <summary>
    /// Optional target tool name for this handoff edge.
    /// </summary>
    public string TargetToolName { get; init; } = string.Empty;

    /// <summary>
    /// Optional target routing role for this handoff edge.
    /// </summary>
    public string TargetRole { get; init; } = string.Empty;

    /// <summary>
    /// Number of bindings declared by this route.
    /// </summary>
    public int BindingCount { get; init; }

    /// <summary>
    /// Normalized source-to-target binding pairs ("source->target").
    /// Duplicate pairs are preserved to keep declared contract multiplicity.
    /// </summary>
    public IReadOnlyList<string> BindingPairs { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Structured setup, handoff, and recovery contract projection for model-side orchestration.
/// </summary>
public sealed class ToolPackToolOrchestrationModel {
    /// <summary>
    /// Normalized pack identifier.
    /// </summary>
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    /// Routing role token.
    /// </summary>
    public string Role { get; init; } = ToolRoutingTaxonomy.RoleOperational;

    /// <summary>
    /// Routing source token.
    /// </summary>
    public string RoutingSource { get; init; } = ToolRoutingTaxonomy.SourceExplicit;

    /// <summary>
    /// Indicates whether tool exposes routing-aware metadata.
    /// </summary>
    public bool IsRoutingAware { get; init; }

    /// <summary>
    /// Optional domain intent family token.
    /// </summary>
    public string DomainIntentFamily { get; init; } = string.Empty;

    /// <summary>
    /// Optional domain intent action id token.
    /// </summary>
    public string DomainIntentActionId { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether tool is setup-aware.
    /// </summary>
    public bool IsSetupAware { get; init; }

    /// <summary>
    /// Number of distinct normalized setup requirement (<c>id</c>, <c>kind</c>) pairs.
    /// </summary>
    public int SetupRequirementCount { get; init; }

    /// <summary>
    /// Optional setup helper tool name.
    /// </summary>
    public string SetupToolName { get; init; } = string.Empty;

    /// <summary>
    /// Optional setup contract identifier.
    /// </summary>
    public string SetupContractId { get; init; } = string.Empty;

    /// <summary>
    /// Normalized setup requirement identifiers.
    /// </summary>
    public IReadOnlyList<string> SetupRequirementIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized setup requirement kinds.
    /// </summary>
    public IReadOnlyList<string> SetupRequirementKinds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized setup hint keys (contract + requirement-level hints).
    /// </summary>
    public IReadOnlyList<string> SetupHintKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether tool declares outbound handoff routes.
    /// </summary>
    public bool IsHandoffAware { get; init; }

    /// <summary>
    /// Number of declared outbound handoff routes.
    /// </summary>
    public int HandoffRouteCount { get; init; }

    /// <summary>
    /// Number of declared outbound handoff bindings across all routes.
    /// Duplicate normalized binding pairs are counted when explicitly declared.
    /// </summary>
    public int HandoffBindingCount { get; init; }

    /// <summary>
    /// Optional handoff contract identifier.
    /// </summary>
    public string HandoffContractId { get; init; } = string.Empty;

    /// <summary>
    /// Normalized outbound handoff edges.
    /// </summary>
    public IReadOnlyList<ToolPackToolHandoffEdgeModel> HandoffEdges { get; init; } = Array.Empty<ToolPackToolHandoffEdgeModel>();

    /// <summary>
    /// Indicates whether tool declares effective recovery behavior in normalized projection.
    /// </summary>
    public bool IsRecoveryAware { get; init; }

    /// <summary>
    /// Indicates support for transient retry.
    /// </summary>
    public bool SupportsTransientRetry { get; init; }

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetryAttempts { get; init; }

    /// <summary>
    /// Indicates support for alternate internal engines.
    /// </summary>
    public bool SupportsAlternateEngines { get; init; }

    /// <summary>
    /// Number of declared alternate internal engines.
    /// </summary>
    public int AlternateEngineCount { get; init; }

    /// <summary>
    /// Optional recovery contract identifier.
    /// </summary>
    public string RecoveryContractId { get; init; } = string.Empty;

    /// <summary>
    /// Number of declared recovery helper tools.
    /// </summary>
    public int RecoveryToolCount { get; init; }

    /// <summary>
    /// Normalized retryable error codes.
    /// </summary>
    public IReadOnlyList<string> RetryableErrorCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized alternate engine identifiers.
    /// </summary>
    public IReadOnlyList<string> AlternateEngineIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized recovery helper tool names.
    /// </summary>
    public IReadOnlyList<string> RecoveryToolNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Tool-level catalog entry for pack guidance.
/// </summary>
public sealed class ToolPackToolCatalogEntryModel {
    private ToolPackToolRoutingModel _routing = new();
    private ToolPackToolOrchestrationModel _orchestration = new();

    /// <summary>
    /// Tool name as registered in the runtime tool registry.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional display name from tool definition metadata.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DisplayName { get; init; }

    /// <summary>
    /// Category for routing/scoping.
    /// </summary>
    public string Category { get; init; } = "general";

    /// <summary>
    /// Selection tags exposed by tool definition metadata.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Structured routing taxonomy for model-side tool selection.
    /// </summary>
    public ToolPackToolRoutingModel Routing {
        get => _routing;
        init => _routing = ToolPackGuidance.NormalizeRoutingContract(value);
    }

    /// <summary>
    /// Tool description from definition metadata.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Required input arguments from the tool input schema.
    /// </summary>
    public IReadOnlyList<string> RequiredArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Input argument hints derived from the tool input schema.
    /// </summary>
    public IReadOnlyList<ToolPackToolArgumentModel> Arguments { get; init; } = Array.Empty<ToolPackToolArgumentModel>();

    /// <summary>
    /// Indicates whether the tool input schema supports optional view projection arguments.
    /// </summary>
    public bool SupportsTableViewProjection { get; init; }

    /// <summary>
    /// Indicates whether this is a pack guidance tool.
    /// </summary>
    public bool IsPackInfoTool { get; init; }

    /// <summary>
    /// Structured capability hints inferred from tool arguments.
    /// </summary>
    public ToolPackToolTraitsModel Traits { get; init; } = new();

    /// <summary>
    /// Structured orchestration metadata projected from tool-owned contracts.
    /// </summary>
    public ToolPackToolOrchestrationModel Orchestration {
        get => _orchestration;
        init => _orchestration = ToolPackGuidance.NormalizeOrchestrationContract(value);
    }

    /// <summary>
    /// Indicates whether tool can perform write/mutating operations.
    /// </summary>
    public bool IsWriteCapable { get; init; }

    /// <summary>
    /// Indicates whether write-governance authorization is required.
    /// </summary>
    public bool RequiresWriteGovernance { get; init; }

    /// <summary>
    /// Optional governance contract id for write authorization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? WriteGovernanceContractId { get; init; }

    /// <summary>
    /// Indicates whether tool exposes authentication behavior/requirements.
    /// </summary>
    public bool IsAuthenticationAware { get; init; }

    /// <summary>
    /// Indicates whether authentication is required for normal operation.
    /// </summary>
    public bool RequiresAuthentication { get; init; }

    /// <summary>
    /// Optional authentication contract id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AuthenticationContractId { get; init; }

    /// <summary>
    /// Optional authentication mode identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AuthenticationMode { get; init; }

    /// <summary>
    /// Authentication-related argument names declared by contract.
    /// </summary>
    public IReadOnlyList<string> AuthenticationArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether tool supports connectivity/authentication probe workflows.
    /// </summary>
    public bool SupportsConnectivityProbe { get; init; }

    /// <summary>
    /// Optional probe tool name declared by authentication contract.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ProbeToolName { get; init; }
}

/// <summary>
/// Input argument hint descriptor in tool catalog metadata.
/// </summary>
public sealed class ToolPackToolArgumentModel {
    /// <summary>
    /// Argument name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// JSON-schema type hint (for example: <c>string</c>, <c>integer</c>, <c>array&lt;string&gt;</c>).
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether argument is required.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Optional description from schema metadata.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional enum-like allowed values from schema metadata.
    /// </summary>
    public IReadOnlyList<string> EnumValues { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Structured capability hints inferred from tool input schema arguments.
/// </summary>
public sealed class ToolPackToolTraitsModel {
    /// <summary>
    /// Indicates support for optional table projection arguments (for display-only shaping).
    /// </summary>
    public bool SupportsTableViewProjection { get; init; }

    /// <summary>
    /// Projection argument names when <see cref="SupportsTableViewProjection"/> is true.
    /// </summary>
    public IReadOnlyList<string> TableViewArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for cursor/offset paging arguments.
    /// </summary>
    public bool SupportsPaging { get; init; }

    /// <summary>
    /// Paging argument names when <see cref="SupportsPaging"/> is true.
    /// </summary>
    public IReadOnlyList<string> PagingArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for explicit time filters.
    /// </summary>
    public bool SupportsTimeRange { get; init; }

    /// <summary>
    /// Time-related argument names when <see cref="SupportsTimeRange"/> is true.
    /// </summary>
    public IReadOnlyList<string> TimeRangeArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for dynamic attribute selection/bag shaping arguments.
    /// </summary>
    public bool SupportsDynamicAttributes { get; init; }

    /// <summary>
    /// Dynamic attribute argument names when <see cref="SupportsDynamicAttributes"/> is true.
    /// </summary>
    public IReadOnlyList<string> DynamicAttributeArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for explicit target scoping (domain/path/channel/provider/computer).
    /// </summary>
    public bool SupportsTargetScoping { get; init; }

    /// <summary>
    /// Scope argument names when <see cref="SupportsTargetScoping"/> is true.
    /// </summary>
    public IReadOnlyList<string> TargetScopeArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for directly targeting remote hosts or host-like endpoints.
    /// </summary>
    public bool SupportsRemoteHostTargeting { get; init; }

    /// <summary>
    /// Remote-host argument names when <see cref="SupportsRemoteHostTargeting"/> is true.
    /// </summary>
    public IReadOnlyList<string> RemoteHostArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for mutating/action flags.
    /// </summary>
    public bool SupportsMutatingActions { get; init; }

    /// <summary>
    /// Mutating/action argument names when <see cref="SupportsMutatingActions"/> is true.
    /// </summary>
    public IReadOnlyList<string> MutatingActionArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for canonical write-governance metadata arguments.
    /// </summary>
    public bool SupportsWriteGovernanceMetadata { get; init; }

    /// <summary>
    /// Canonical write-governance metadata argument names present in the tool schema.
    /// </summary>
    public IReadOnlyList<string> WriteGovernanceMetadataArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates support for explicit authentication/profile reference arguments.
    /// </summary>
    public bool SupportsAuthentication { get; init; }

    /// <summary>
    /// Authentication-related argument names present in the tool schema.
    /// </summary>
    public IReadOnlyList<string> AuthenticationArguments { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Factory for consistent pack guidance models.
/// </summary>
public static partial class ToolPackGuidance {
    private static readonly string[] TableViewArgumentNames = { "columns", "sort_by", "sort_direction", "top" };
    private static readonly string[] PagingArgumentNames = { "cursor", "page_size", "offset", "skip", "limit" };
    private static readonly string[] TimeRangeArgumentNames = { "start_time_utc", "end_time_utc", "since_utc", "before_utc", "reference_time_utc" };
    private static readonly string[] DynamicAttributeArgumentNames = { "attributes", "include_raw", "include_operational_attributes", "include_computed_flags", "include_security_descriptor" };
    private static readonly string[] TargetScopeArgumentNames = {
        "domain_name",
        "forest_name",
        "domain_controller",
        "search_base_dn",
        "path",
        "folder",
        "channel",
        "provider_name",
        "computer_name",
        "machine_name",
        "machine_names",
        "server"
    };
    private static readonly string[] RemoteHostArgumentNames = {
        "computer_name",
        "machine_name",
        "machine_names",
        "domain_controller",
        "server",
        "targets"
    };
    private static readonly IReadOnlyList<string> MutatingActionArgumentNames = ToolMutabilityHintNames.CanonicalMutatingActionArguments;
    private static readonly IReadOnlyList<string> AuthenticationArgumentNames =
        ToolAuthenticationArgumentNames.CanonicalArguments;

    internal static ToolPackToolRoutingModel NormalizeRoutingContract(ToolPackToolRoutingModel? routing) {
        return NormalizeRouting(routing);
    }

    internal static ToolPackToolOrchestrationModel NormalizeOrchestrationContract(
        ToolPackToolOrchestrationModel? orchestration) {
        return NormalizeOrchestration(orchestration);
    }

    internal static IReadOnlyList<ToolPackToolCatalogEntryModel> NormalizeToolCatalogContract(
        IEnumerable<ToolPackToolCatalogEntryModel>? entries) {
        return NormalizeToolCatalog(entries);
    }

    /// <summary>
    /// Creates a structured flow step descriptor.
    /// </summary>
    public static ToolPackFlowStepModel FlowStep(string goal, IEnumerable<string>? suggestedTools = null, string? notes = null) {
        if (string.IsNullOrWhiteSpace(goal)) {
            throw new ArgumentException("Flow step goal is required.", nameof(goal));
        }

        return new ToolPackFlowStepModel {
            Goal = goal.Trim(),
            SuggestedTools = NormalizeValues(suggestedTools),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
    }

    /// <summary>
    /// Creates a structured capability descriptor.
    /// </summary>
    public static ToolPackCapabilityModel Capability(string id, string summary, IEnumerable<string>? primaryTools = null, string? notes = null) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Capability id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(summary)) {
            throw new ArgumentException("Capability summary is required.", nameof(summary));
        }

        return new ToolPackCapabilityModel {
            Id = id.Trim(),
            Summary = summary.Trim(),
            PrimaryTools = NormalizeValues(primaryTools),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
    }

    /// <summary>
    /// Creates a structured entity handoff descriptor.
    /// </summary>
    public static ToolPackEntityHandoffModel EntityHandoff(
        string id,
        string summary,
        IEnumerable<string>? entityKinds = null,
        IEnumerable<string>? sourceTools = null,
        IEnumerable<string>? targetTools = null,
        IEnumerable<ToolPackEntityFieldMappingModel>? fieldMappings = null,
        string? notes = null) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Entity handoff id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(summary)) {
            throw new ArgumentException("Entity handoff summary is required.", nameof(summary));
        }

        return new ToolPackEntityHandoffModel {
            Id = id.Trim(),
            Summary = summary.Trim(),
            EntityKinds = NormalizeValues(entityKinds),
            SourceTools = NormalizeValues(sourceTools),
            TargetTools = NormalizeValues(targetTools),
            FieldMappings = NormalizeEntityFieldMappings(fieldMappings),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
    }

    /// <summary>
    /// Creates a source-field to target-argument mapping descriptor for entity handoffs.
    /// </summary>
    public static ToolPackEntityFieldMappingModel EntityFieldMapping(string sourceField, string targetArgument, string? normalization = null) {
        if (string.IsNullOrWhiteSpace(sourceField)) {
            throw new ArgumentException("Source field is required.", nameof(sourceField));
        }
        if (string.IsNullOrWhiteSpace(targetArgument)) {
            throw new ArgumentException("Target argument is required.", nameof(targetArgument));
        }

        return new ToolPackEntityFieldMappingModel {
            SourceField = sourceField.Trim(),
            TargetArgument = targetArgument.Trim(),
            Normalization = string.IsNullOrWhiteSpace(normalization) ? null : normalization.Trim()
        };
    }

    /// <summary>
    /// Builds a tool catalog from runtime tool instances.
    /// </summary>
    /// <param name="tools">Tool instances used for registration.</param>
    /// <returns>Normalized catalog entries ordered by registration sequence.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> CatalogFromTools(IEnumerable<ITool> tools) {
        var list = new List<ToolPackToolCatalogEntryModel>();
        foreach (var tool in tools ?? Array.Empty<ITool>()) {
            if (tool?.Definition is not ToolDefinition def || string.IsNullOrWhiteSpace(def.Name)) {
                continue;
            }

            var enrichedDefinition = ToolSelectionMetadata.Enrich(def, tool.GetType());
            var requiredArguments = ReadRequiredArguments(enrichedDefinition.Parameters);
            var argumentHints = ReadArgumentHints(enrichedDefinition.Parameters, requiredArguments);
            var supportsTableView = SupportsTableViewProjection(enrichedDefinition.Parameters);
            var routing = ToolSelectionMetadata.ResolveRouting(enrichedDefinition, tool.GetType());
            ToolAuthenticationContract? authentication = enrichedDefinition.Authentication;
            var authenticationArguments = NormalizeValues(authentication?.GetSchemaArgumentNames());

            list.Add(new ToolPackToolCatalogEntryModel {
                Name = enrichedDefinition.Name.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(enrichedDefinition.DisplayName)
                    ? null
                    : enrichedDefinition.DisplayName.Trim(),
                Category = NormalizeCategory(enrichedDefinition.Category),
                Tags = NormalizeTags(enrichedDefinition.Tags),
                Routing = new ToolPackToolRoutingModel {
                    Scope = routing.Scope,
                    Operation = routing.Operation,
                    Entity = routing.Entity,
                    Risk = routing.Risk,
                    Source = routing.IsExplicit
                        ? ToolRoutingTaxonomy.SourceExplicit
                        : ToolRoutingTaxonomy.SourceInferred
                },
                Description = enrichedDefinition.Description?.Trim() ?? string.Empty,
                RequiredArguments = requiredArguments,
                Arguments = argumentHints,
                SupportsTableViewProjection = supportsTableView,
                IsPackInfoTool = enrichedDefinition.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase),
                Traits = BuildToolTraits(argumentHints.Select(static x => x.Name), supportsTableView),
                Orchestration = BuildToolOrchestration(enrichedDefinition),
                IsWriteCapable = enrichedDefinition.WriteGovernance?.IsWriteCapable ?? false,
                RequiresWriteGovernance = enrichedDefinition.WriteGovernance?.RequiresGovernanceAuthorization ?? false,
                WriteGovernanceContractId = string.IsNullOrWhiteSpace(enrichedDefinition.WriteGovernance?.GovernanceContractId)
                    ? null
                    : enrichedDefinition.WriteGovernance!.GovernanceContractId,
                IsAuthenticationAware = authentication?.IsAuthenticationAware ?? false,
                RequiresAuthentication = authentication?.RequiresAuthentication ?? false,
                AuthenticationContractId = string.IsNullOrWhiteSpace(authentication?.AuthenticationContractId)
                    ? null
                    : authentication!.AuthenticationContractId,
                AuthenticationMode = ToAuthenticationModeId(authentication),
                AuthenticationArguments = authenticationArguments,
                SupportsConnectivityProbe = authentication?.SupportsConnectivityProbe ?? false,
                ProbeToolName = string.IsNullOrWhiteSpace(authentication?.ProbeToolName)
                    ? null
                    : authentication!.ProbeToolName
            });
        }

        return NormalizeToolCatalog(list);
    }
}
