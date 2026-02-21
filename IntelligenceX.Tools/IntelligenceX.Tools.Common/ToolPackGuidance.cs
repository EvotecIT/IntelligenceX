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
    public IReadOnlyList<ToolPackToolCatalogEntryModel> ToolCatalog { get; init; } = Array.Empty<ToolPackToolCatalogEntryModel>();

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
/// Tool-level catalog entry for pack guidance.
/// </summary>
public sealed class ToolPackToolCatalogEntryModel {
    /// <summary>
    /// Tool name as registered in the runtime tool registry.
    /// </summary>
    public string Name { get; init; } = string.Empty;

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
        "domain_controller", "search_base_dn", "path", "folder", "channel", "provider_name", "computer_name", "server"
    };
    private static readonly string[] MutatingActionArgumentNames = { "send", "dry_run", "confirm", "execute", "apply", "force", "enable", "disable", "allow_write" };
    private static readonly IReadOnlyList<string> AuthenticationArgumentNames =
        ToolAuthenticationArgumentNames.CanonicalArguments;

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

            var requiredArguments = ReadRequiredArguments(def.Parameters);
            var argumentHints = ReadArgumentHints(def.Parameters, requiredArguments);
            var supportsTableView = SupportsTableViewProjection(def.Parameters);
            ToolAuthenticationContract? authentication = def.Authentication;
            var authenticationArguments = NormalizeValues(authentication?.GetSchemaArgumentNames());

            list.Add(new ToolPackToolCatalogEntryModel {
                Name = def.Name.Trim(),
                Description = def.Description?.Trim() ?? string.Empty,
                RequiredArguments = requiredArguments,
                Arguments = argumentHints,
                SupportsTableViewProjection = supportsTableView,
                IsPackInfoTool = def.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase),
                Traits = BuildToolTraits(argumentHints.Select(static x => x.Name), supportsTableView),
                IsWriteCapable = def.WriteGovernance?.IsWriteCapable ?? false,
                RequiresWriteGovernance = def.WriteGovernance?.RequiresGovernanceAuthorization ?? false,
                WriteGovernanceContractId = string.IsNullOrWhiteSpace(def.WriteGovernance?.GovernanceContractId)
                    ? null
                    : def.WriteGovernance!.GovernanceContractId,
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

    /// <summary>
    /// Creates a standard pack guidance model used by <c>*_pack_info</c> tools.
    /// </summary>
    /// <param name="pack">Pack identifier.</param>
    /// <param name="engine">Engine/library name.</param>
    /// <param name="tools">Registered tool names.</param>
    /// <param name="recommendedFlow">Optional flow guidance.</param>
    /// <param name="flowSteps">Optional structured flow steps.</param>
    /// <param name="capabilities">Optional structured capability descriptors.</param>
    /// <param name="entityHandoffs">Optional structured entity handoff descriptors.</param>
    /// <param name="toolCatalog">Optional tool catalog derived from runtime registrations and schemas.</param>
    /// <param name="rawPayloadPolicy">Optional raw payload policy override.</param>
    /// <param name="viewProjectionPolicy">Optional projection policy override.</param>
    /// <param name="correlationGuidance">Optional correlation guidance.</param>
    /// <param name="viewFieldSuffix">Optional projection field suffix.</param>
    /// <param name="setupHints">Optional setup hints object.</param>
    /// <param name="safety">Optional safety hints object.</param>
    /// <param name="limits">Optional limits hints object.</param>
    /// <param name="note">Optional additional note.</param>
    /// <returns>Typed pack guidance model.</returns>
    public static ToolPackInfoModel Create(
        string pack,
        string engine,
        IReadOnlyList<string> tools,
        IEnumerable<string>? recommendedFlow = null,
        IEnumerable<ToolPackFlowStepModel>? flowSteps = null,
        IEnumerable<ToolPackCapabilityModel>? capabilities = null,
        IEnumerable<ToolPackEntityHandoffModel>? entityHandoffs = null,
        IEnumerable<ToolPackToolCatalogEntryModel>? toolCatalog = null,
        string? rawPayloadPolicy = null,
        string? viewProjectionPolicy = null,
        string? correlationGuidance = null,
        string viewFieldSuffix = "_view",
        object? setupHints = null,
        object? safety = null,
        object? limits = null,
        string? note = null) {
        if (string.IsNullOrWhiteSpace(pack)) {
            throw new ArgumentException("Pack id is required.", nameof(pack));
        }
        if (string.IsNullOrWhiteSpace(engine)) {
            throw new ArgumentException("Engine name is required.", nameof(engine));
        }

        var resolvedTools = NormalizeValues(tools);
        var flow = NormalizeValues(recommendedFlow, distinct: false);
        var resolvedFlowSteps = NormalizeFlowSteps(flowSteps);
        var resolvedCapabilities = NormalizeCapabilities(capabilities);
        var resolvedEntityHandoffs = NormalizeEntityHandoffs(entityHandoffs);
        var resolvedToolCatalog = NormalizeToolCatalog(toolCatalog);

        return new ToolPackInfoModel {
            Pack = pack.Trim(),
            Engine = engine.Trim(),
            GuidanceVersion = 1,
            OutputContract = new ToolPackOutputContractModel {
                RawPayloadPolicy = string.IsNullOrWhiteSpace(rawPayloadPolicy)
                    ? "Preserve raw payload fields for model reasoning and correlation."
                    : rawPayloadPolicy.Trim(),
                ViewProjectionPolicy = string.IsNullOrWhiteSpace(viewProjectionPolicy)
                    ? "Projection arguments are optional and view-only."
                    : viewProjectionPolicy.Trim(),
                ViewFieldSuffix = string.IsNullOrWhiteSpace(viewFieldSuffix) ? "_view" : viewFieldSuffix.Trim(),
                CorrelationGuidance = string.IsNullOrWhiteSpace(correlationGuidance) ? null : correlationGuidance.Trim()
            },
            SetupHints = setupHints,
            Safety = safety,
            Limits = limits,
            RecommendedFlow = flow,
            RecommendedFlowSteps = resolvedFlowSteps,
            Capabilities = resolvedCapabilities,
            EntityHandoffs = resolvedEntityHandoffs,
            ToolCatalog = resolvedToolCatalog,
            Tools = resolvedTools,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }

}
