using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Normalized outbound handoff edge derived from tool contracts.
/// </summary>
public sealed record ToolOrchestrationHandoffEdge {
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

    /// <summary>
    /// Normalized route conditions ("source==expected") that must match before this handoff is eligible.
    /// </summary>
    public IReadOnlyList<string> ConditionPairs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional normalized follow-up kind token for this handoff edge.
    /// </summary>
    public string FollowUpKind { get; init; } = string.Empty;

    /// <summary>
    /// Optional follow-up priority hint for this handoff edge.
    /// Higher values indicate more important follow-up work.
    /// </summary>
    public int FollowUpPriority { get; init; }
}

/// <summary>
/// Normalized orchestration entry derived from tool contracts.
/// </summary>
public sealed record ToolOrchestrationCatalogEntry {
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Normalized pack identifier.
    /// </summary>
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    /// Routing role token.
    /// </summary>
    public string Role { get; init; } = ToolRoutingTaxonomy.RoleOperational;

    /// <summary>
    /// Indicates whether this tool is an orientation/pack-info tool.
    /// </summary>
    public bool IsPackInfoTool { get; init; }

    /// <summary>
    /// Indicates whether this tool is an environment-discovery tool.
    /// </summary>
    public bool IsEnvironmentDiscoverTool { get; init; }

    /// <summary>
    /// Routing source token.
    /// </summary>
    public string RoutingSource { get; init; } = ToolRoutingTaxonomy.SourceExplicit;

    /// <summary>
    /// Indicates whether tool exposes routing-aware metadata.
    /// </summary>
    public bool IsRoutingAware { get; init; }

    /// <summary>
    /// Routing scope token.
    /// </summary>
    public string Scope { get; init; } = ToolRoutingTaxonomy.ScopeGeneral;

    /// <summary>
    /// Routing operation token.
    /// </summary>
    public string Operation { get; init; } = ToolRoutingTaxonomy.OperationRead;

    /// <summary>
    /// Routing entity token.
    /// </summary>
    public string Entity { get; init; } = ToolRoutingTaxonomy.EntityResource;

    /// <summary>
    /// Routing risk token.
    /// </summary>
    public string Risk { get; init; } = ToolRoutingTaxonomy.RiskLow;

    /// <summary>
    /// Human-readable execution locality token projected from tool schema.
    /// </summary>
    public string ExecutionScope { get; init; } = "local_only";
    /// <summary>
    /// Indicates whether tool declares a structured execution contract.
    /// </summary>
    public bool IsExecutionAware { get; init; }
    /// <summary>
    /// Optional stable execution contract identifier.
    /// </summary>
    public string ExecutionContractId { get; init; } = string.Empty;
    /// <summary>
    /// Indicates whether the tool can execute in the local runtime.
    /// </summary>
    public bool SupportsLocalExecution { get; init; } = true;
    /// <summary>
    /// Indicates whether the tool can execute against remote targets or remote backends.
    /// </summary>
    public bool SupportsRemoteExecution { get; init; }

    /// <summary>
    /// Indicates whether the tool schema exposes target-scope arguments.
    /// </summary>
    public bool SupportsTargetScoping { get; init; }

    /// <summary>
    /// Canonical target-scope arguments projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> TargetScopeArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether the tool schema exposes remote-host targeting arguments.
    /// </summary>
    public bool SupportsRemoteHostTargeting { get; init; }

    /// <summary>
    /// Canonical remote-host targeting arguments projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> RemoteHostArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Canonical schema argument names projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> SchemaArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Canonical required schema argument names projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> RequiredSchemaArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional representative task examples published by the owning pack catalog.
    /// </summary>
    public IReadOnlyList<string> RepresentativeExamples { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates the tool is a pack-preferred first operational step from pack-owned guidance.
    /// </summary>
    public bool IsPackPreferredEntryTool { get; init; }

    /// <summary>
    /// Indicates the tool is a pack-preferred probe/preflight step from pack-owned guidance.
    /// </summary>
    public bool IsPackPreferredProbeTool { get; init; }

    /// <summary>
    /// Pack-owned recipe identifiers that explicitly include this tool.
    /// </summary>
    public IReadOnlyList<string> PackRecommendedRecipeIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Pack-owned recipe summary/when-to-use hints associated with this tool.
    /// </summary>
    public IReadOnlyList<string> PackRecommendedRecipeHints { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional pack-published probe-helper freshness window, in seconds, for background helper reuse.
    /// </summary>
    public int? PackProbeHelperFreshnessWindowSeconds { get; init; }

    /// <summary>
    /// Optional pack-published setup-helper freshness window, in seconds, for background helper reuse.
    /// </summary>
    public int? PackSetupHelperFreshnessWindowSeconds { get; init; }

    /// <summary>
    /// Optional pack-published recipe-helper freshness window, in seconds, for background helper reuse.
    /// </summary>
    public int? PackRecipeHelperFreshnessWindowSeconds { get; init; }

    /// <summary>
    /// Indicates whether the tool can perform mutating/write actions.
    /// </summary>
    public bool IsWriteCapable { get; init; }

    /// <summary>
    /// Indicates whether explicit write-governance authorization is required.
    /// </summary>
    public bool RequiresWriteGovernance { get; init; }

    /// <summary>
    /// Optional stable write-governance contract identifier.
    /// </summary>
    public string WriteGovernanceContractId { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether authentication is required for normal operation.
    /// </summary>
    public bool RequiresAuthentication { get; init; }

    /// <summary>
    /// Optional authentication contract identifier.
    /// </summary>
    public string AuthenticationContractId { get; init; } = string.Empty;

    /// <summary>
    /// Canonical authentication argument names exposed by the tool schema.
    /// </summary>
    public IReadOnlyList<string> AuthenticationArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether the tool supports connectivity/authentication probe workflows.
    /// </summary>
    public bool SupportsConnectivityProbe { get; init; }

    /// <summary>
    /// Optional probe tool name exposed by the authentication contract.
    /// </summary>
    public string ProbeToolName { get; init; } = string.Empty;

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
    /// This aggregate is independent from <see cref="SetupRequirementIds"/> and
    /// <see cref="SetupRequirementKinds"/>, which are each distinct lists projected separately.
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
    public IReadOnlyList<ToolOrchestrationHandoffEdge> HandoffEdges { get; init; } = Array.Empty<ToolOrchestrationHandoffEdge>();

    /// <summary>
    /// Indicates whether tool declares effective recovery behavior in normalized projection.
    /// This can remain true for flag-only contracts (for example transient retry support)
    /// even when detail lists like <see cref="RetryableErrorCodes"/> are empty.
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
/// Contract-first orchestration catalog built from tool definitions.
/// </summary>
public sealed class ToolOrchestrationCatalog {
    private sealed record PackCatalogToolMetadata(
        string PackId,
        ToolPackToolCatalogEntryModel Entry);

    private sealed record PackGuidanceToolMetadata(
        string PackId,
        PackGuidanceMetadata Guidance);

    private sealed record PackGuidanceMetadata(
        IReadOnlySet<string> PreferredEntryToolNames,
        IReadOnlySet<string> PreferredProbeToolNames,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RecipeIdsByToolName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RecipeHintsByToolName,
        int? ProbeHelperFreshnessWindowSeconds,
        int? SetupHelperFreshnessWindowSeconds,
        int? RecipeHelperFreshnessWindowSeconds);

    private readonly IReadOnlyDictionary<string, ToolOrchestrationCatalogEntry> _entriesByToolName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByPackId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByRole;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByPackAndRole;

    private ToolOrchestrationCatalog(
        Dictionary<string, ToolOrchestrationCatalogEntry> entriesByToolName,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByPackId,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByRole,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByPackAndRole) {
        _entriesByToolName = new ReadOnlyDictionary<string, ToolOrchestrationCatalogEntry>(
            new Dictionary<string, ToolOrchestrationCatalogEntry>(entriesByToolName, StringComparer.OrdinalIgnoreCase));
        _entriesByPackId = FreezeEntryListDictionary(entriesByPackId);
        _entriesByRole = FreezeEntryListDictionary(entriesByRole);
        _entriesByPackAndRole = FreezeEntryListDictionary(entriesByPackAndRole);
    }

    /// <summary>
    /// Total orchestration entries.
    /// </summary>
    public int Count => _entriesByToolName.Count;

    /// <summary>
    /// Entries by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, ToolOrchestrationCatalogEntry> EntriesByToolName => _entriesByToolName;

    /// <summary>
    /// Builds orchestration catalog from registry definitions.
    /// </summary>
    /// <param name="definitions">Tool definitions.</param>
    /// <returns>Normalized orchestration catalog.</returns>
    public static ToolOrchestrationCatalog Build(IReadOnlyList<ToolDefinition> definitions) {
        return Build(
            definitions,
            packCatalogEntriesByToolName: null,
            packGuidanceByPackId: null,
            packGuidanceByToolName: null);
    }

    private static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IReadOnlyDictionary<string, PackCatalogToolMetadata>? packCatalogEntriesByToolName,
        IReadOnlyDictionary<string, PackGuidanceMetadata>? packGuidanceByPackId,
        IReadOnlyDictionary<string, PackGuidanceToolMetadata>? packGuidanceByToolName) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

        var entriesByToolName = new Dictionary<string, ToolOrchestrationCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var toolName = NormalizeToken(definition.Name);
            if (toolName.Length == 0 || entriesByToolName.ContainsKey(toolName)) {
                continue;
            }

            var routingInfo = ToolSelectionMetadata.ResolveRouting(definition);
            var routing = definition.Routing;
            var packId = NormalizePackId(routing?.PackId);
            var schemaTraits = ToolSchemaTraitProjection.Project(definition);
            var normalizedSchemaArgumentNames = NormalizeDistinctTokens(ReadSchemaArgumentNames(definition.Parameters));
            var normalizedRequiredSchemaArgumentNames = NormalizeDistinctTokens(ReadRequiredSchemaArgumentNames(definition.Parameters));
            var execution = definition.Execution;
            var normalizedExecutionContractId = NormalizeToken(execution?.ExecutionContractId);
            var executionScope = ToolExecutionScopes.Resolve(schemaTraits.ExecutionScope, schemaTraits.SupportsRemoteHostTargeting);

            var role = NormalizeToken(routing?.Role);
            if (!ToolRoutingTaxonomy.IsAllowedRole(role)) {
                role = ToolRoutingTaxonomy.RoleOperational;
            }
            var isPackInfoTool = string.Equals(role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
            var isEnvironmentDiscoverTool = string.Equals(role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);

            var routingSource = NormalizeToken(routing?.RoutingSource);
            if (!ToolRoutingTaxonomy.IsAllowedSource(routingSource)) {
                routingSource = ToolRoutingTaxonomy.SourceExplicit;
            }

            var family = NormalizeToken(routing?.DomainIntentFamily);
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                normalizedFamily = string.Empty;
            }

            var actionId = NormalizeToken(routing?.DomainIntentActionId);
            var setup = definition.Setup;
            var handoff = definition.Handoff;
            var recovery = definition.Recovery;
            var writeGovernance = definition.WriteGovernance;
            var authentication = definition.Authentication;
            var handoffBindingCount = 0;
            var routes = handoff?.OutboundRoutes;
            var handoffEdges = new List<ToolOrchestrationHandoffEdge>();
            if (routes is { Count: > 0 }) {
                for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++) {
                    var route = routes[routeIndex];
                    var bindings = route?.Bindings;
                    if (bindings is null || bindings.Count == 0) {
                        continue;
                    }

                    var bindingPairs = new List<string>(bindings.Count);
                    for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++) {
                        var binding = bindings[bindingIndex];
                        var source = NormalizeToken(binding?.SourceField);
                        var target = NormalizeToken(binding?.TargetArgument);
                        if (source.Length == 0 || target.Length == 0) {
                            continue;
                        }

                        bindingPairs.Add(source + "->" + target);
                    }

                    var normalizedBindingPairs = NormalizeTokensPreserveMultiplicity(bindingPairs);
                    if (normalizedBindingPairs.Count == 0) {
                        continue;
                    }

                    var conditionPairs = new List<string>(route?.Conditions?.Count ?? 0);
                    if (route?.Conditions is { Count: > 0 }) {
                        for (var conditionIndex = 0; conditionIndex < route.Conditions.Count; conditionIndex++) {
                            var condition = route.Conditions[conditionIndex];
                            var normalizedCondition = NormalizeHandoffConditionPair(condition?.SourceField, condition?.ExpectedValue);
                            if (normalizedCondition.Length == 0) {
                                continue;
                            }

                            conditionPairs.Add(normalizedCondition);
                        }
                    }

                    var normalizedConditionPairs = NormalizeTokensPreserveMultiplicity(conditionPairs);

                    handoffBindingCount += normalizedBindingPairs.Count;
                    handoffEdges.Add(new ToolOrchestrationHandoffEdge {
                        TargetPackId = NormalizePackId(route?.TargetPackId),
                        TargetToolName = NormalizeToken(route?.TargetToolName),
                        TargetRole = NormalizeToken(route?.TargetRole),
                        BindingCount = normalizedBindingPairs.Count,
                        BindingPairs = FreezeStringList(normalizedBindingPairs),
                        ConditionPairs = FreezeStringList(normalizedConditionPairs),
                        FollowUpKind = ToolHandoffFollowUpKinds.Normalize(route?.FollowUpKind),
                        FollowUpPriority = ToolHandoffFollowUpPriorities.Normalize(route?.FollowUpPriority ?? 0)
                    });
                }
            }

            var setupRequirementIds = new List<string>();
            var setupRequirementKinds = new List<string>();
            var setupRequirementPairs = new List<string>();
            var setupHintKeys = new List<string>();
            if (setup?.SetupHintKeys is { Count: > 0 }) {
                for (var hintIndex = 0; hintIndex < setup.SetupHintKeys.Count; hintIndex++) {
                    setupHintKeys.Add(setup.SetupHintKeys[hintIndex]);
                }
            }
            if (setup?.Requirements is { Count: > 0 }) {
                for (var requirementIndex = 0; requirementIndex < setup.Requirements.Count; requirementIndex++) {
                    var requirement = setup.Requirements[requirementIndex];
                    var requirementId = requirement?.RequirementId ?? string.Empty;
                    var requirementKind = requirement?.Kind ?? string.Empty;
                    setupRequirementIds.Add(requirementId);
                    setupRequirementKinds.Add(requirementKind);
                    var normalizedRequirementId = NormalizeToken(requirementId);
                    var normalizedRequirementKind = NormalizeToken(requirementKind);
                    if (normalizedRequirementId.Length > 0 && normalizedRequirementKind.Length > 0) {
                        setupRequirementPairs.Add(normalizedRequirementId + "|" + normalizedRequirementKind);
                    }
                    if (requirement?.HintKeys is not { Count: > 0 }) {
                        continue;
                    }

                    for (var hintIndex = 0; hintIndex < requirement.HintKeys.Count; hintIndex++) {
                        setupHintKeys.Add(requirement.HintKeys[hintIndex]);
                    }
                }
            }
            var retryableErrorCodes = NormalizeDistinctTokens(recovery?.RetryableErrorCodes);
            var alternateEngineIds = NormalizeDistinctTokens(recovery?.AlternateEngineIds);
            var recoveryToolNames = NormalizeDistinctTokens(recovery?.RecoveryToolNames);
            var alternateEngineCount = alternateEngineIds.Length;
            var normalizedSetupRequirementIds = NormalizeDistinctTokens(setupRequirementIds);
            var normalizedSetupRequirementKinds = NormalizeDistinctTokens(setupRequirementKinds);
            var normalizedSetupRequirementPairs = NormalizeDistinctTokens(setupRequirementPairs);
            var normalizedSetupHintKeys = NormalizeDistinctTokens(setupHintKeys);
            var normalizedSetupToolName = NormalizeToken(setup?.SetupToolName);
            var normalizedWriteGovernanceContractId = NormalizeToken(writeGovernance?.GovernanceContractId);
            var normalizedAuthenticationContractId = NormalizeToken(authentication?.AuthenticationContractId);
            var normalizedAuthenticationArguments = NormalizeDistinctTokens(authentication?.GetSchemaArgumentNames());
            var normalizedProbeToolName = NormalizeToken(authentication?.ProbeToolName);
            var isSetupAware = setup?.IsSetupAware == true
                               && (normalizedSetupRequirementPairs.Length > 0
                                   || normalizedSetupHintKeys.Length > 0
                                   || normalizedSetupToolName.Length > 0);
            var normalizedHandoffEdges = handoffEdges
                .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var frozenHandoffEdges = FreezeHandoffEdges(normalizedHandoffEdges);
            var normalizedHandoffContractId = NormalizeToken(handoff?.HandoffContractId);
            var isHandoffAware = handoff?.IsHandoffAware == true && frozenHandoffEdges.Count > 0;
            var normalizedRecoveryContractId = NormalizeToken(recovery?.RecoveryContractId);
            var maxRetryAttempts = Math.Max(0, recovery?.MaxRetryAttempts ?? 0);
            var supportsTransientRetry = recovery?.SupportsTransientRetry == true;
            var supportsAlternateEngines = recovery?.SupportsAlternateEngines == true;
            var isRecoveryAware = recovery?.IsRecoveryAware == true
                                  && (normalizedRecoveryContractId.Length > 0
                                      || retryableErrorCodes.Length > 0
                                      || alternateEngineCount > 0
                                      || recoveryToolNames.Length > 0
                                      || supportsTransientRetry
                                      || supportsAlternateEngines
                                      || maxRetryAttempts > 0);

            var entry = new ToolOrchestrationCatalogEntry {
                ToolName = toolName,
                PackId = packId,
                Role = role,
                IsPackInfoTool = isPackInfoTool,
                IsEnvironmentDiscoverTool = isEnvironmentDiscoverTool,
                RoutingSource = routingSource,
                IsRoutingAware = routing?.IsRoutingAware == true,
                Scope = NormalizeToken(routingInfo.Scope, ToolRoutingTaxonomy.ScopeGeneral),
                Operation = NormalizeToken(routingInfo.Operation, ToolRoutingTaxonomy.OperationRead),
                Entity = NormalizeToken(routingInfo.Entity, ToolRoutingTaxonomy.EntityResource),
                Risk = NormalizeToken(routingInfo.Risk, ToolRoutingTaxonomy.RiskLow),
                ExecutionScope = executionScope,
                IsExecutionAware = execution?.IsExecutionAware == true,
                ExecutionContractId = normalizedExecutionContractId,
                SupportsLocalExecution = !string.Equals(executionScope, ToolExecutionScopes.RemoteOnly, StringComparison.OrdinalIgnoreCase),
                SupportsRemoteExecution = ToolExecutionScopes.IsRemoteCapable(executionScope),
                SupportsTargetScoping = schemaTraits.SupportsTargetScoping,
                TargetScopeArguments = FreezeStringList(schemaTraits.TargetScopeArguments),
                SupportsRemoteHostTargeting = schemaTraits.SupportsRemoteHostTargeting,
                RemoteHostArguments = FreezeStringList(schemaTraits.RemoteHostArguments),
                SchemaArgumentNames = FreezeStringList(normalizedSchemaArgumentNames),
                RequiredSchemaArgumentNames = FreezeStringList(normalizedRequiredSchemaArgumentNames),
                IsWriteCapable = writeGovernance?.IsWriteCapable == true,
                RequiresWriteGovernance = writeGovernance?.RequiresGovernanceAuthorization == true,
                WriteGovernanceContractId = normalizedWriteGovernanceContractId,
                RequiresAuthentication = authentication?.RequiresAuthentication == true,
                AuthenticationContractId = normalizedAuthenticationContractId,
                AuthenticationArguments = FreezeStringList(normalizedAuthenticationArguments),
                SupportsConnectivityProbe = authentication?.SupportsConnectivityProbe == true,
                ProbeToolName = normalizedProbeToolName,
                DomainIntentFamily = normalizedFamily,
                DomainIntentActionId = actionId,
                IsSetupAware = isSetupAware,
                SetupRequirementCount = normalizedSetupRequirementPairs.Length,
                SetupToolName = normalizedSetupToolName,
                SetupContractId = NormalizeToken(setup?.SetupContractId),
                SetupRequirementIds = FreezeStringList(normalizedSetupRequirementIds),
                SetupRequirementKinds = FreezeStringList(normalizedSetupRequirementKinds),
                SetupHintKeys = FreezeStringList(normalizedSetupHintKeys),
                IsHandoffAware = isHandoffAware,
                HandoffRouteCount = frozenHandoffEdges.Count,
                HandoffBindingCount = handoffBindingCount,
                HandoffContractId = normalizedHandoffContractId,
                HandoffEdges = frozenHandoffEdges,
                IsRecoveryAware = isRecoveryAware,
                SupportsTransientRetry = supportsTransientRetry,
                MaxRetryAttempts = maxRetryAttempts,
                SupportsAlternateEngines = supportsAlternateEngines,
                AlternateEngineCount = alternateEngineCount,
                RecoveryContractId = normalizedRecoveryContractId,
                RecoveryToolCount = recoveryToolNames.Length,
                RetryableErrorCodes = FreezeStringList(retryableErrorCodes),
                AlternateEngineIds = FreezeStringList(alternateEngineIds),
                RecoveryToolNames = FreezeStringList(recoveryToolNames)
            };
            if (packCatalogEntriesByToolName is not null
                && packCatalogEntriesByToolName.TryGetValue(toolName, out var packCatalogMetadata)) {
                entry = ApplyPackCatalogOverlay(entry, packCatalogMetadata);
            }
            if (packGuidanceByPackId is not null) {
                if (entry.PackId.Length > 0
                    && packGuidanceByPackId.TryGetValue(entry.PackId, out var packGuidanceMetadata)) {
                    entry = ApplyPackGuidanceOverlay(entry, packGuidanceMetadata, entry.PackId);
                } else if (packGuidanceByToolName is not null
                           && packGuidanceByToolName.TryGetValue(toolName, out var packGuidanceToolMetadata)) {
                    entry = ApplyPackGuidanceOverlay(entry, packGuidanceToolMetadata.Guidance, packGuidanceToolMetadata.PackId);
                }
            }

            entriesByToolName[toolName] = entry;
        }

        var entriesByPackId = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0)
            .GroupBy(static entry => entry.PackId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        var entriesByRole = entriesByToolName.Values
            .GroupBy(static entry => entry.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        var entriesByPackAndRole = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0 && entry.Role.Length > 0)
            .GroupBy(static entry => BuildPackRoleKey(entry.PackId, entry.Role), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        return new ToolOrchestrationCatalog(entriesByToolName, entriesByPackId, entriesByRole, entriesByPackAndRole);
    }

    /// <summary>
    /// Builds orchestration catalog from registry definitions and runtime packs.
    /// </summary>
    public static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IEnumerable<IToolPack>? packs) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

        var packCatalogEntriesByToolName = BuildPackCatalogEntryIndex(packs);
        var packGuidanceByPackId = BuildPackGuidanceIndex(packs);
        var packGuidanceByToolName = BuildPackGuidanceToolIndex(packGuidanceByPackId);
        return Build(definitions, packCatalogEntriesByToolName, packGuidanceByPackId, packGuidanceByToolName);
    }

    /// <summary>
    /// Returns true when an entry exists for the provided tool name.
    /// </summary>
    public bool TryGetEntry(string? toolName, out ToolOrchestrationCatalogEntry entry) {
        var normalized = NormalizeToken(toolName);
        return _entriesByToolName.TryGetValue(normalized, out entry!);
    }

    /// <summary>
    /// Returns true when a normalized pack id is available for the provided tool name.
    /// </summary>
    public bool TryGetPackId(string? toolName, out string packId) {
        packId = string.Empty;
        if (!TryGetEntry(toolName, out var entry) || entry.PackId.Length == 0) {
            return false;
        }

        packId = entry.PackId;
        return true;
    }

    /// <summary>
    /// Gets all entries for a pack id.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByPackId(string? packId) {
        var normalized = NormalizePackId(packId);
        if (normalized.Length == 0 || !_entriesByPackId.TryGetValue(normalized, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    /// <summary>
    /// Gets all entries for a routing role.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByRole(string? role) {
        var normalized = NormalizeToken(role);
        if (normalized.Length == 0 || !_entriesByRole.TryGetValue(normalized, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    /// <summary>
    /// Gets all entries for a pack/role pair.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByPackAndRole(string? packId, string? role) {
        var normalizedPackId = NormalizePackId(packId);
        var normalizedRole = NormalizeToken(role);
        if (normalizedPackId.Length == 0 || normalizedRole.Length == 0) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        var key = BuildPackRoleKey(normalizedPackId, normalizedRole);
        if (!_entriesByPackAndRole.TryGetValue(key, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    private static string BuildPackRoleKey(string packId, string role) {
        return packId + "|" + role;
    }

    private static IReadOnlyDictionary<string, PackCatalogToolMetadata>? BuildPackCatalogEntryIndex(IEnumerable<IToolPack>? packs) {
        if (packs is null) {
            return null;
        }

        var index = new Dictionary<string, PackCatalogToolMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs) {
            if (pack is not IToolPackCatalogProvider catalogProvider) {
                continue;
            }

            var normalizedPackId = NormalizePackId(pack.Descriptor.Id);
            var catalogEntries = catalogProvider.GetToolCatalog();
            if (catalogEntries is null || catalogEntries.Count == 0) {
                continue;
            }

            for (var i = 0; i < catalogEntries.Count; i++) {
                var catalogEntry = catalogEntries[i];
                if (catalogEntry is null) {
                    continue;
                }

                var toolName = NormalizeToken(catalogEntry.Name);
                if (toolName.Length == 0 || index.ContainsKey(toolName)) {
                    continue;
                }

                index[toolName] = new PackCatalogToolMetadata(normalizedPackId, catalogEntry);
            }
        }

        return index.Count == 0 ? null : index;
    }

    private static IReadOnlyDictionary<string, PackGuidanceMetadata>? BuildPackGuidanceIndex(IEnumerable<IToolPack>? packs) {
        if (packs is null) {
            return null;
        }

        var index = new Dictionary<string, PackGuidanceMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs) {
            if (pack is not IToolPackGuidanceProvider guidanceProvider) {
                continue;
            }

            var normalizedPackId = NormalizePackId(pack.Descriptor.Id);
            if (normalizedPackId.Length == 0 || index.ContainsKey(normalizedPackId)) {
                continue;
            }

            var guidance = guidanceProvider.GetPackGuidance();
            if (guidance is null) {
                continue;
            }

            var preferredEntryToolNames = new HashSet<string>(
                NormalizeDistinctTokens(guidance.RuntimeCapabilities?.PreferredEntryTools),
                StringComparer.OrdinalIgnoreCase);
            var preferredProbeToolNames = new HashSet<string>(
                NormalizeDistinctTokens(guidance.RuntimeCapabilities?.PreferredProbeTools),
                StringComparer.OrdinalIgnoreCase);
            var recipeIdsByToolName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var recipeHintsByToolName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            if (guidance.RecommendedRecipes is { Count: > 0 }) {
                for (var recipeIndex = 0; recipeIndex < guidance.RecommendedRecipes.Count; recipeIndex++) {
                    var recipe = guidance.RecommendedRecipes[recipeIndex];
                    var recipeId = NormalizeToken(recipe?.Id);
                    if (recipeId.Length == 0) {
                        continue;
                    }

                    var recipeHint = BuildPackRecipeHint(recipe);
                    var recipeToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (recipe?.Steps is { Count: > 0 }) {
                        for (var stepIndex = 0; stepIndex < recipe.Steps.Count; stepIndex++) {
                            var suggestedTools = NormalizeDistinctTokens(recipe.Steps[stepIndex]?.SuggestedTools);
                            for (var toolIndex = 0; toolIndex < suggestedTools.Length; toolIndex++) {
                                recipeToolNames.Add(suggestedTools[toolIndex]);
                            }
                        }
                    }

                    var verificationTools = NormalizeDistinctTokens(recipe?.VerificationTools);
                    for (var verificationIndex = 0; verificationIndex < verificationTools.Length; verificationIndex++) {
                        recipeToolNames.Add(verificationTools[verificationIndex]);
                    }

                    foreach (var toolName in recipeToolNames) {
                        AddPackRecipeValue(recipeIdsByToolName, toolName, recipeId);
                        AddPackRecipeValue(recipeHintsByToolName, toolName, recipeHint);
                    }
                }
            }

            index[normalizedPackId] = new PackGuidanceMetadata(
                PreferredEntryToolNames: preferredEntryToolNames,
                PreferredProbeToolNames: preferredProbeToolNames,
                RecipeIdsByToolName: FreezeStringListDictionary(recipeIdsByToolName),
                RecipeHintsByToolName: FreezeStringListDictionary(recipeHintsByToolName),
                ProbeHelperFreshnessWindowSeconds: NormalizeOptionalPositiveInt(guidance.RuntimeCapabilities?.ProbeHelperFreshnessWindowSeconds),
                SetupHelperFreshnessWindowSeconds: NormalizeOptionalPositiveInt(guidance.RuntimeCapabilities?.SetupHelperFreshnessWindowSeconds),
                RecipeHelperFreshnessWindowSeconds: NormalizeOptionalPositiveInt(guidance.RuntimeCapabilities?.RecipeHelperFreshnessWindowSeconds));
        }

        return index.Count == 0 ? null : index;
    }

    private static IReadOnlyDictionary<string, PackGuidanceToolMetadata>? BuildPackGuidanceToolIndex(
        IReadOnlyDictionary<string, PackGuidanceMetadata>? packGuidanceByPackId) {
        if (packGuidanceByPackId is null || packGuidanceByPackId.Count == 0) {
            return null;
        }

        var claimedPackIdsByToolName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in packGuidanceByPackId) {
            var packId = NormalizePackId(pair.Key);
            if (packId.Length == 0) {
                continue;
            }

            foreach (var toolName in EnumerateGuidanceToolNames(pair.Value)) {
                if (!claimedPackIdsByToolName.TryGetValue(toolName, out var claimedPackIds)) {
                    claimedPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    claimedPackIdsByToolName[toolName] = claimedPackIds;
                }

                claimedPackIds.Add(packId);
            }
        }

        var index = new Dictionary<string, PackGuidanceToolMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in claimedPackIdsByToolName) {
            if (pair.Value.Count != 1) {
                continue;
            }

            var packId = pair.Value.First();
            if (!packGuidanceByPackId.TryGetValue(packId, out var guidance)) {
                continue;
            }

            index[pair.Key] = new PackGuidanceToolMetadata(packId, guidance);
        }

        return index.Count == 0 ? null : index;
    }

    private static ToolOrchestrationCatalogEntry ApplyPackCatalogOverlay(
        ToolOrchestrationCatalogEntry entry,
        PackCatalogToolMetadata overlay) {
        var overlayEntry = overlay.Entry;
        var normalizedPackId = NormalizeOverlayPackId(overlayEntry.Routing?.PackId, entry.PackId, overlay.PackId);
        var normalizedRole = NormalizeOverlayRole(overlayEntry.Routing?.Role, entry.Role);
        var normalizedScope = NormalizeToken(overlayEntry.Routing?.Scope, entry.Scope);
        var normalizedOperation = NormalizeToken(overlayEntry.Routing?.Operation, entry.Operation);
        var normalizedEntity = NormalizeToken(overlayEntry.Routing?.Entity, entry.Entity);
        var normalizedRisk = NormalizeOverlayRisk(overlayEntry.Routing?.Risk, entry.Risk);
        var normalizedRoutingSource = NormalizeOverlayRoutingSource(overlayEntry.Routing?.Source, entry.RoutingSource);
        var isPackInfoTool = overlayEntry.IsPackInfoTool
                             || entry.IsPackInfoTool
                             || string.Equals(normalizedRole, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
        var isEnvironmentDiscoverTool = overlayEntry.IsEnvironmentDiscoverTool
                                        || entry.IsEnvironmentDiscoverTool
                                        || string.Equals(normalizedRole, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
        var normalizedDomainIntentFamily = NormalizeOverlayDomainIntentFamily(
            overlayEntry.Routing?.DomainIntentFamily,
            entry.DomainIntentFamily);
        var normalizedDomainIntentActionId = NormalizeOverlayDomainIntentActionId(
            overlayEntry.Routing?.DomainIntentActionId,
            normalizedDomainIntentFamily,
            entry.DomainIntentFamily,
            entry.DomainIntentActionId);

        var targetScopeArguments = NormalizeDistinctTokens(overlayEntry.Traits?.TargetScopeArguments);
        var remoteHostArguments = NormalizeDistinctTokens(overlayEntry.Traits?.RemoteHostArguments);
        var resolvedTargetScopeArguments = targetScopeArguments.Length > 0
            ? targetScopeArguments
            : entry.TargetScopeArguments;
        var resolvedRemoteHostArguments = remoteHostArguments.Length > 0
            ? remoteHostArguments
            : entry.RemoteHostArguments;
        var supportsTargetScoping = overlayEntry.Traits?.SupportsTargetScoping == true
                                    || targetScopeArguments.Length > 0
                                    || entry.SupportsTargetScoping
                                    || entry.TargetScopeArguments.Count > 0;
        var supportsRemoteHostTargeting = overlayEntry.Traits?.SupportsRemoteHostTargeting == true
                                          || remoteHostArguments.Length > 0
                                          || entry.SupportsRemoteHostTargeting
                                          || entry.RemoteHostArguments.Count > 0;
        var executionScope = NormalizeOverlayExecutionScope(
            overlayEntry.Traits?.ExecutionScope,
            entry.ExecutionScope,
            supportsRemoteHostTargeting);
        var representativeExamples = FreezeRepresentativeExamples(overlayEntry.RepresentativeExamples);
        var writeGovernanceContractId = NormalizeToken(overlayEntry.WriteGovernanceContractId, entry.WriteGovernanceContractId);
        var authenticationArguments = NormalizeDistinctTokens(overlayEntry.AuthenticationArguments);
        var authenticationContractId = NormalizeToken(overlayEntry.AuthenticationContractId, entry.AuthenticationContractId);
        var probeToolName = NormalizeToken(overlayEntry.ProbeToolName, entry.ProbeToolName);

        var setupToolName = NormalizeToken(overlayEntry.Setup?.SetupToolName, entry.SetupToolName);
        var setupRequirementIds = NormalizeDistinctTokens(overlayEntry.Setup?.RequirementIds);
        var resolvedSetupRequirementIds = setupRequirementIds.Length > 0
            ? setupRequirementIds
            : entry.SetupRequirementIds;
        var setupHintKeys = NormalizeDistinctTokens(overlayEntry.Setup?.HintKeys);
        var resolvedSetupHintKeys = setupHintKeys.Length > 0
            ? setupHintKeys
            : entry.SetupHintKeys;
        var isSetupAware = overlayEntry.Setup?.IsSetupAware == true
                           || entry.IsSetupAware
                           || setupToolName.Length > 0
                           || resolvedSetupRequirementIds.Count > 0
                           || resolvedSetupHintKeys.Count > 0;

        var handoffEdges = BuildOverlayHandoffEdges(overlayEntry.Handoff);
        var resolvedHandoffEdges = handoffEdges.Count > 0
            ? handoffEdges
            : entry.HandoffEdges;
        var handoffBindingCount = resolvedHandoffEdges.Sum(static edge => edge.BindingCount);
        var isHandoffAware = overlayEntry.Handoff?.IsHandoffAware == true || entry.IsHandoffAware || resolvedHandoffEdges.Count > 0;

        var retryableErrorCodes = NormalizeDistinctTokens(overlayEntry.Recovery?.RetryableErrorCodes);
        var resolvedRetryableErrorCodes = retryableErrorCodes.Length > 0
            ? retryableErrorCodes
            : entry.RetryableErrorCodes;
        var recoveryToolNames = NormalizeDistinctTokens(overlayEntry.Recovery?.RecoveryToolNames);
        var resolvedRecoveryToolNames = recoveryToolNames.Length > 0
            ? recoveryToolNames
            : entry.RecoveryToolNames;
        var supportsTransientRetry = overlayEntry.Recovery?.SupportsTransientRetry == true || entry.SupportsTransientRetry;
        var maxRetryAttempts = Math.Max(entry.MaxRetryAttempts, Math.Max(0, overlayEntry.Recovery?.MaxRetryAttempts ?? 0));
        var isRecoveryAware = overlayEntry.Recovery?.IsRecoveryAware == true
                              || entry.IsRecoveryAware
                              || resolvedRetryableErrorCodes.Count > 0
                              || resolvedRecoveryToolNames.Count > 0
                              || supportsTransientRetry
                              || maxRetryAttempts > 0;

        return entry with {
            PackId = normalizedPackId,
            Role = normalizedRole,
            IsPackInfoTool = isPackInfoTool,
            IsEnvironmentDiscoverTool = isEnvironmentDiscoverTool,
            Scope = normalizedScope,
            Operation = normalizedOperation,
            Entity = normalizedEntity,
            Risk = normalizedRisk,
            RoutingSource = normalizedRoutingSource,
            IsRoutingAware = true,
            ExecutionScope = executionScope,
            SupportsTargetScoping = supportsTargetScoping,
            TargetScopeArguments = FreezeStringList(resolvedTargetScopeArguments),
            SupportsRemoteHostTargeting = supportsRemoteHostTargeting,
            RemoteHostArguments = FreezeStringList(resolvedRemoteHostArguments),
            RepresentativeExamples = representativeExamples,
            IsWriteCapable = overlayEntry.IsWriteCapable || entry.IsWriteCapable,
            RequiresWriteGovernance = overlayEntry.RequiresWriteGovernance || entry.RequiresWriteGovernance,
            WriteGovernanceContractId = writeGovernanceContractId,
            RequiresAuthentication = overlayEntry.RequiresAuthentication || entry.RequiresAuthentication,
            AuthenticationContractId = authenticationContractId,
            AuthenticationArguments = FreezeStringList(authenticationArguments.Length > 0 ? authenticationArguments : entry.AuthenticationArguments),
            SupportsConnectivityProbe = overlayEntry.SupportsConnectivityProbe || entry.SupportsConnectivityProbe,
            ProbeToolName = probeToolName,
            DomainIntentFamily = normalizedDomainIntentFamily,
            DomainIntentActionId = normalizedDomainIntentActionId,
            IsSetupAware = isSetupAware,
            SetupRequirementCount = isSetupAware
                ? Math.Max(entry.SetupRequirementCount, resolvedSetupRequirementIds.Count)
                : 0,
            SetupToolName = setupToolName,
            SetupRequirementIds = FreezeStringList(resolvedSetupRequirementIds),
            SetupRequirementKinds = isSetupAware ? entry.SetupRequirementKinds : Array.Empty<string>(),
            SetupHintKeys = FreezeStringList(resolvedSetupHintKeys),
            IsHandoffAware = isHandoffAware,
            HandoffRouteCount = resolvedHandoffEdges.Count,
            HandoffBindingCount = handoffBindingCount,
            HandoffEdges = resolvedHandoffEdges,
            IsRecoveryAware = isRecoveryAware,
            SupportsTransientRetry = supportsTransientRetry,
            MaxRetryAttempts = maxRetryAttempts,
            SupportsAlternateEngines = entry.SupportsAlternateEngines,
            AlternateEngineCount = entry.AlternateEngineCount,
            RecoveryToolCount = resolvedRecoveryToolNames.Count,
            RetryableErrorCodes = FreezeStringList(resolvedRetryableErrorCodes),
            AlternateEngineIds = entry.AlternateEngineIds,
            RecoveryToolNames = FreezeStringList(resolvedRecoveryToolNames)
        };
    }

    private static IEnumerable<string> EnumerateGuidanceToolNames(PackGuidanceMetadata guidance) {
        foreach (var toolName in guidance.PreferredEntryToolNames) {
            if (toolName.Length > 0) {
                yield return toolName;
            }
        }

        foreach (var toolName in guidance.PreferredProbeToolNames) {
            if (toolName.Length > 0) {
                yield return toolName;
            }
        }

        foreach (var pair in guidance.RecipeIdsByToolName) {
            if (pair.Key.Length > 0) {
                yield return pair.Key;
            }
        }

        foreach (var pair in guidance.RecipeHintsByToolName) {
            if (pair.Key.Length > 0) {
                yield return pair.Key;
            }
        }
    }

    private static ToolOrchestrationCatalogEntry ApplyPackGuidanceOverlay(
        ToolOrchestrationCatalogEntry entry,
        PackGuidanceMetadata guidance,
        string? overlayPackId) {
        var toolName = NormalizeToken(entry.ToolName);
        var recipeIds = guidance.RecipeIdsByToolName.TryGetValue(toolName, out var matchedRecipeIds)
            ? matchedRecipeIds
            : Array.Empty<string>();
        var recipeHints = guidance.RecipeHintsByToolName.TryGetValue(toolName, out var matchedRecipeHints)
            ? matchedRecipeHints
            : Array.Empty<string>();
        var normalizedOverlayPackId = NormalizePackId(overlayPackId);

        return entry with {
            PackId = entry.PackId.Length > 0 ? entry.PackId : normalizedOverlayPackId,
            IsPackPreferredEntryTool = guidance.PreferredEntryToolNames.Contains(toolName),
            IsPackPreferredProbeTool = guidance.PreferredProbeToolNames.Contains(toolName),
            PackRecommendedRecipeIds = FreezeStringList(recipeIds),
            PackRecommendedRecipeHints = FreezeStringList(recipeHints),
            PackProbeHelperFreshnessWindowSeconds = guidance.ProbeHelperFreshnessWindowSeconds,
            PackSetupHelperFreshnessWindowSeconds = guidance.SetupHelperFreshnessWindowSeconds,
            PackRecipeHelperFreshnessWindowSeconds = guidance.RecipeHelperFreshnessWindowSeconds
        };
    }

    private static int? NormalizeOptionalPositiveInt(int? value) {
        return value.HasValue && value.Value > 0
            ? value.Value
            : null;
    }

    private static IReadOnlyList<string> ReadSchemaArgumentNames(JsonObject? schema) {
        if (schema is null) {
            return Array.Empty<string>();
        }

        var properties = schema.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(properties.Count);
        foreach (var pair in properties) {
            var normalized = NormalizeToken(pair.Key);
            if (normalized.Length > 0) {
                names.Add(normalized);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> ReadRequiredSchemaArgumentNames(JsonObject? schema) {
        if (schema is null) {
            return Array.Empty<string>();
        }

        var required = schema.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(required.Count);
        for (var i = 0; i < required.Count; i++) {
            var normalized = NormalizeToken(required[i]?.ToString());
            if (normalized.Length > 0) {
                names.Add(normalized);
            }
        }

        return names;
    }

    private static IReadOnlyList<ToolOrchestrationHandoffEdge> BuildOverlayHandoffEdges(ToolPackToolHandoffModel? handoff) {
        if (handoff?.Routes is not { Count: > 0 }) {
            return Array.Empty<ToolOrchestrationHandoffEdge>();
        }

        var edges = new List<ToolOrchestrationHandoffEdge>();
        for (var i = 0; i < handoff.Routes.Count; i++) {
            var route = handoff.Routes[i];
            var targetPackId = NormalizePackId(route?.TargetPackId);
            var targetToolName = NormalizeToken(route?.TargetToolName);
            var targetRole = NormalizeToken(route?.TargetRole);
            var bindingPairs = NormalizeTokensPreserveMultiplicity(route?.BindingPairs);
            var conditionPairs = NormalizeTokensPreserveMultiplicity(route?.ConditionPairs);
            if (targetPackId.Length == 0
                && targetToolName.Length == 0
                && targetRole.Length == 0
                && bindingPairs.Count == 0
                && conditionPairs.Count == 0) {
                continue;
            }

            edges.Add(new ToolOrchestrationHandoffEdge {
                TargetPackId = targetPackId,
                TargetToolName = targetToolName,
                TargetRole = targetRole,
                BindingCount = bindingPairs.Count,
                BindingPairs = FreezeStringList(bindingPairs),
                ConditionPairs = FreezeStringList(conditionPairs)
            });
        }

        return edges.Count == 0
            ? Array.Empty<ToolOrchestrationHandoffEdge>()
            : FreezeHandoffEdges(
                edges
                    .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static string NormalizeOverlayRisk(string? value, string fallback) {
        var normalized = NormalizeToken(value, fallback);
        return ToolRoutingTaxonomy.IsAllowedRisk(normalized) ? normalized : fallback;
    }

    private static string NormalizeOverlayRoutingSource(string? value, string fallback) {
        var normalized = NormalizeToken(value, fallback);
        return ToolRoutingTaxonomy.IsAllowedSource(normalized) ? normalized : fallback;
    }

    private static string NormalizeOverlayPackId(string? value, string fallback, string overlayFallback) {
        var normalized = NormalizePackId(value);
        if (normalized.Length > 0) {
            return normalized;
        }

        var fallbackPackId = NormalizePackId(fallback);
        if (fallbackPackId.Length > 0) {
            return fallbackPackId;
        }

        return NormalizePackId(overlayFallback);
    }

    private static string NormalizeOverlayRole(string? value, string fallback) {
        var normalized = NormalizeToken(value);
        return ToolRoutingTaxonomy.IsAllowedRole(normalized) ? normalized : fallback;
    }

    private static string NormalizeHandoffConditionPair(string? sourceField, string? expectedValue) {
        var normalizedSource = NormalizeHandoffConditionSourceField(sourceField);
        var normalizedExpected = NormalizeToken(expectedValue);
        return normalizedSource.Length > 0 && normalizedExpected.Length > 0
            ? normalizedSource + "==" + normalizedExpected
            : string.Empty;
    }

    private static string NormalizeHandoffConditionSourceField(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0
            ? string.Empty
            : normalized.Replace("\\", "/", StringComparison.Ordinal);
    }

    private static string NormalizeOverlayDomainIntentFamily(string? value, string fallback) {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0) {
            return fallback;
        }

        return ToolSelectionMetadata.TryNormalizeDomainIntentFamily(normalized, out var family)
            ? family
            : fallback;
    }

    private static string NormalizeOverlayDomainIntentActionId(
        string? value,
        string normalizedFamily,
        string fallbackFamily,
        string fallbackActionId) {
        var normalizedActionId = NormalizeToken(value);
        if (normalizedActionId.Length > 0) {
            return normalizedActionId;
        }

        if (normalizedFamily.Length > 0
            && !string.Equals(normalizedFamily, fallbackFamily, StringComparison.OrdinalIgnoreCase)) {
            return ToolSelectionMetadata.GetDefaultDomainIntentActionId(normalizedFamily);
        }

        return fallbackActionId;
    }

    private static string NormalizeOverlayExecutionScope(string? value, string fallback, bool supportsRemoteHostTargeting) {
        var normalized = NormalizeToken(value);
        if (string.Equals(normalized, "local_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "local_or_remote", StringComparison.OrdinalIgnoreCase)) {
            return normalized;
        }

        if (supportsRemoteHostTargeting) {
            return "local_or_remote";
        }

        return NormalizeToken(fallback, "local_only");
    }

    private static string NormalizePackId(string? value) {
        return ToolPackBootstrap.NormalizePackId(value);
    }

    private static string NormalizeToken(string? value, string fallback = "") {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string[] NormalizeDistinctTokens(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            var normalized = NormalizeToken(value);
            if (normalized.Length == 0) {
                continue;
            }

            unique.Add(normalized);
        }

        return unique.Count == 0
            ? Array.Empty<string>()
            : unique.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> NormalizeTokensPreserveMultiplicity(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        foreach (var value in values) {
            var token = NormalizeToken(value);
            if (token.Length == 0) {
                continue;
            }

            normalized.Add(token);
        }

        return normalized.Count == 0
            ? Array.Empty<string>()
            : normalized;
    }

    private static IReadOnlyList<string> FreezeStringList(IReadOnlyList<string> values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var copy = new string[values.Count];
        for (var i = 0; i < values.Count; i++) {
            copy[i] = values[i];
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<string> FreezeRepresentativeExamples(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0 || normalized.Contains(value, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            normalized.Add(value);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : Array.AsReadOnly(normalized.ToArray());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FreezeStringListDictionary(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values) {
        if (values is null || values.Count == 0) {
            return new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        }

        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values) {
            var key = NormalizeToken(pair.Key);
            if (key.Length == 0 || copy.ContainsKey(key)) {
                continue;
            }

            copy[key] = FreezeStringList(pair.Value);
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
    }

    private static string BuildPackRecipeHint(ToolPackRecipeModel? recipe) {
        if (recipe is null) {
            return string.Empty;
        }

        var summary = NormalizeToken(recipe.Summary);
        var whenToUse = NormalizeToken(recipe.WhenToUse);
        if (summary.Length > 0 && whenToUse.Length > 0) {
            return summary + " " + whenToUse;
        }

        return summary.Length > 0 ? summary : whenToUse;
    }

    private static void AddPackRecipeValue(
        IDictionary<string, IReadOnlyList<string>> valuesByToolName,
        string toolName,
        string value) {
        var normalizedToolName = NormalizeToken(toolName);
        var normalizedValue = NormalizeToken(value);
        if (normalizedToolName.Length == 0 || normalizedValue.Length == 0) {
            return;
        }

        if (!valuesByToolName.TryGetValue(normalizedToolName, out var existingValues)) {
            valuesByToolName[normalizedToolName] = new[] { normalizedValue };
            return;
        }

        if (existingValues.Contains(normalizedValue, StringComparer.OrdinalIgnoreCase)) {
            return;
        }

        valuesByToolName[normalizedToolName] = existingValues.Concat(new[] { normalizedValue }).ToArray();
    }

    private static IReadOnlyList<ToolOrchestrationHandoffEdge> FreezeHandoffEdges(
        IReadOnlyList<ToolOrchestrationHandoffEdge> edges) {
        if (edges is null || edges.Count == 0) {
            return Array.Empty<ToolOrchestrationHandoffEdge>();
        }

        var copy = new ToolOrchestrationHandoffEdge[edges.Count];
        for (var i = 0; i < edges.Count; i++) {
            copy[i] = edges[i];
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<ToolOrchestrationCatalogEntry> FreezeEntryList(
        IEnumerable<ToolOrchestrationCatalogEntry> entries) {
        if (entries is null) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        var copy = entries.ToArray();
        return copy.Length == 0
            ? Array.Empty<ToolOrchestrationCatalogEntry>()
            : Array.AsReadOnly(copy);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> FreezeEntryListDictionary(
        IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> source) {
        if (source is null || source.Count == 0) {
            return new ReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(
                new Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(StringComparer.OrdinalIgnoreCase));
        }

        var copy = new Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0 || copy.ContainsKey(key)) {
                continue;
            }

            copy[key] = FreezeEntryList(pair.Value);
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(copy);
    }
}
