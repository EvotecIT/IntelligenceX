using System;
using System.Collections.Generic;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for pack-owned contract object construction.
/// </summary>
public static class ToolContractDefaults {
    /// <summary>
    /// Creates a single setup requirement descriptor.
    /// </summary>
    public static ToolSetupRequirement CreateRequirement(
        string requirementId,
        string requirementKind,
        IReadOnlyList<string>? hintKeys = null,
        bool isRequired = true) {
        return new ToolSetupRequirement {
            RequirementId = requirementId ?? string.Empty,
            Kind = requirementKind ?? string.Empty,
            IsRequired = isRequired,
            HintKeys = CloneOrEmpty(hintKeys)
        };
    }

    /// <summary>
    /// Creates a setup contract with the supplied requirement descriptors and setup hints.
    /// </summary>
    public static ToolSetupContract CreateSetup(
        string? setupToolName = null,
        IReadOnlyList<ToolSetupRequirement>? requirements = null,
        IReadOnlyList<string>? setupHintKeys = null) {
        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = setupToolName ?? string.Empty,
            Requirements = CloneRequirementsOrEmpty(requirements),
            SetupHintKeys = CloneOrEmpty(setupHintKeys)
        };
    }

    /// <summary>
    /// Creates a setup contract with a single required setup requirement.
    /// </summary>
    public static ToolSetupContract CreateRequiredSetup(
        string setupToolName,
        string requirementId,
        string requirementKind,
        IReadOnlyList<string> setupHintKeys,
        IReadOnlyList<string>? requirementHintKeys = null) {
        return CreateSetup(
            setupToolName: setupToolName,
            requirements: new[] {
                CreateRequirement(
                    requirementId: requirementId,
                    requirementKind: requirementKind,
                    hintKeys: requirementHintKeys ?? setupHintKeys,
                    isRequired: true)
            },
            setupHintKeys: setupHintKeys);
    }

    /// <summary>
    /// Creates a setup contract that only advertises hint keys.
    /// </summary>
    public static ToolSetupContract CreateHintOnlySetup(IReadOnlyList<string> setupHintKeys) {
        return new ToolSetupContract {
            IsSetupAware = true,
            SetupHintKeys = CloneOrEmpty(setupHintKeys)
        };
    }

    /// <summary>
    /// Creates a recovery contract with standard retry and recovery metadata.
    /// </summary>
    public static ToolRecoveryContract CreateRecovery(
        bool supportsTransientRetry,
        int maxRetryAttempts,
        IReadOnlyList<string>? retryableErrorCodes = null,
        IReadOnlyList<string>? recoveryToolNames = null,
        bool supportsAlternateEngines = false,
        IReadOnlyList<string>? alternateEngineIds = null) {
        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = supportsTransientRetry,
            MaxRetryAttempts = maxRetryAttempts,
            RetryableErrorCodes = CloneOrEmpty(retryableErrorCodes),
            SupportsAlternateEngines = supportsAlternateEngines,
            AlternateEngineIds = CloneOrEmpty(supportsAlternateEngines ? alternateEngineIds : null),
            RecoveryToolNames = CloneOrEmpty(recoveryToolNames)
        };
    }

    /// <summary>
    /// Creates a no-retry recovery contract.
    /// </summary>
    public static ToolRecoveryContract CreateNoRetryRecovery(IReadOnlyList<string>? recoveryToolNames = null) {
        return CreateRecovery(
            supportsTransientRetry: false,
            maxRetryAttempts: 0,
            recoveryToolNames: recoveryToolNames);
    }

    /// <summary>
    /// Creates a standard explicit routing contract while preserving reusable fields from an existing declaration.
    /// </summary>
    public static ToolRoutingContract CreateExplicitRoutingContract(
        ToolRoutingContract? existing,
        string packId,
        string role,
        string? domainIntentFamily,
        string? domainIntentActionId,
        IReadOnlyList<string>? defaultSignalTokens,
        bool requiresSelectionForFallback = false,
        IReadOnlyList<string>? fallbackSelectionKeys = null,
        IReadOnlyList<string>? fallbackHintKeys = null) {
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = packId ?? string.Empty,
            Role = role ?? string.Empty,
            DomainIntentFamily = domainIntentFamily ?? string.Empty,
            DomainIntentActionId = domainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0
                ? CloneOrEmpty(existing.DomainSignalTokens)
                : CloneOrEmpty(defaultSignalTokens),
            RequiresSelectionForFallback = requiresSelectionForFallback,
            FallbackSelectionKeys = CloneOrEmpty(fallbackSelectionKeys),
            FallbackHintKeys = CloneOrEmpty(fallbackHintKeys)
        };
    }

    /// <summary>
    /// Resolves an execution contract by preserving an explicit declaration or projecting standard execution traits.
    /// </summary>
    public static ToolExecutionContract? ResolveExecutionContractFromTraits(ToolDefinition definition, ToolRoutingContract routing) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(routing);

        if (definition.Execution is { IsExecutionAware: true }) {
            return definition.Execution;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Execution;
        }

        var traits = ToolExecutionTraitProjection.Project(definition);
        return new ToolExecutionContract {
            IsExecutionAware = true,
            ExecutionScope = traits.ExecutionScope,
            TargetScopeArguments = traits.TargetScopeArguments,
            RemoteHostArguments = traits.RemoteHostArguments
        };
    }

    /// <summary>
    /// Resolves a setup contract by preserving an explicit declaration or invoking a pack-owned factory.
    /// </summary>
    public static ToolSetupContract? ResolveSetupContract(
        ToolDefinition definition,
        Func<ToolDefinition, ToolSetupContract?> createSetup) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(createSetup);

        if (definition.Setup is { IsSetupAware: true }) {
            return definition.Setup;
        }

        return createSetup(definition) ?? definition.Setup;
    }

    /// <summary>
    /// Resolves a handoff contract by preserving an explicit declaration or invoking a pack-owned factory.
    /// </summary>
    public static ToolHandoffContract? ResolveHandoffContract(
        ToolDefinition definition,
        Func<ToolDefinition, ToolHandoffContract?> createHandoff) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(createHandoff);

        if (definition.Handoff is { IsHandoffAware: true }) {
            return definition.Handoff;
        }

        return createHandoff(definition) ?? definition.Handoff;
    }

    /// <summary>
    /// Resolves a recovery contract by preserving an explicit declaration or invoking a pack-owned factory.
    /// </summary>
    public static ToolRecoveryContract? ResolveRecoveryContract(
        ToolDefinition definition,
        Func<ToolDefinition, ToolRecoveryContract?> createRecovery) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(createRecovery);

        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        return createRecovery(definition) ?? definition.Recovery;
    }

    /// <summary>
    /// Creates a single handoff binding descriptor.
    /// </summary>
    public static ToolHandoffBinding CreateBinding(
        string sourceField,
        string targetArgument,
        bool isRequired = true,
        string? transformId = null) {
        return new ToolHandoffBinding {
            SourceField = sourceField ?? string.Empty,
            TargetArgument = targetArgument ?? string.Empty,
            IsRequired = isRequired,
            TransformId = transformId ?? string.Empty
        };
    }

    /// <summary>
    /// Creates a single outbound handoff route descriptor.
    /// </summary>
    public static ToolHandoffRoute CreateRoute(
        string targetPackId,
        string targetToolName,
        string reason,
        IReadOnlyList<ToolHandoffBinding> bindings,
        string? targetRole = null) {
        return new ToolHandoffRoute {
            TargetPackId = targetPackId ?? string.Empty,
            TargetToolName = targetToolName ?? string.Empty,
            TargetRole = targetRole ?? string.Empty,
            Reason = reason ?? string.Empty,
            Bindings = CloneBindingsOrEmpty(bindings)
        };
    }

    /// <summary>
    /// Creates a route where multiple source fields bind into the same target argument.
    /// </summary>
    public static ToolHandoffRoute CreateSharedTargetRoute(
        string targetPackId,
        string targetToolName,
        string reason,
        string targetArgument,
        IReadOnlyList<string> sourceFields,
        bool isRequired = false,
        string? targetRole = null) {
        var bindings = new ToolHandoffBinding[sourceFields?.Count ?? 0];
        for (var i = 0; i < bindings.Length; i++) {
            bindings[i] = CreateBinding(sourceFields![i], targetArgument, isRequired: isRequired);
        }

        return CreateRoute(
            targetPackId: targetPackId,
            targetToolName: targetToolName,
            reason: reason,
            bindings: bindings,
            targetRole: targetRole);
    }

    /// <summary>
    /// Creates multiple routes that all bind the same source fields into the same target argument.
    /// </summary>
    public static ToolHandoffRoute[] CreateSharedTargetRoutes(
        string targetPackId,
        string targetArgument,
        IReadOnlyList<string> sourceFields,
        IReadOnlyList<(string TargetToolName, string Reason)> routeDescriptors,
        bool isRequired = false,
        string? targetRole = null) {
        if (routeDescriptors is null || routeDescriptors.Count == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        var routes = new ToolHandoffRoute[routeDescriptors.Count];
        for (var i = 0; i < routeDescriptors.Count; i++) {
            var descriptor = routeDescriptors[i];
            routes[i] = CreateSharedTargetRoute(
                targetPackId: targetPackId,
                targetToolName: descriptor.TargetToolName,
                reason: descriptor.Reason,
                targetArgument: targetArgument,
                sourceFields: sourceFields,
                isRequired: isRequired,
                targetRole: targetRole);
        }

        return routes;
    }

    /// <summary>
    /// Creates a single-route handoff contract for the supplied route descriptors.
    /// </summary>
    public static ToolHandoffContract CreateHandoff(IReadOnlyList<ToolHandoffRoute> routes) {
        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = CloneRoutesOrEmpty(routes)
        };
    }

    /// <summary>
    /// Merges string groups into a trimmed, case-insensitive distinct array while preserving first-seen order.
    /// </summary>
    public static string[] MergeDistinctStrings(params IReadOnlyList<string>[] groups) {
        if (groups is null || groups.Length == 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Length; i++) {
            var group = groups[i];
            if (group is null || group.Count == 0) {
                continue;
            }

            for (var j = 0; j < group.Count; j++) {
                var candidate = (group[j] ?? string.Empty).Trim();
                if (candidate.Length == 0 || !seen.Add(candidate)) {
                    continue;
                }

                values.Add(candidate);
            }
        }

        return values.ToArray();
    }

    private static string[] CloneOrEmpty(IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var cloned = new string[values.Count];
        for (var i = 0; i < values.Count; i++) {
            cloned[i] = values[i] ?? string.Empty;
        }

        return cloned;
    }

    private static ToolSetupRequirement[] CloneRequirementsOrEmpty(IReadOnlyList<ToolSetupRequirement>? requirements) {
        if (requirements is null || requirements.Count == 0) {
            return Array.Empty<ToolSetupRequirement>();
        }

        var cloned = new ToolSetupRequirement[requirements.Count];
        for (var i = 0; i < requirements.Count; i++) {
            var requirement = requirements[i];
            cloned[i] = requirement is null
                ? new ToolSetupRequirement()
                : new ToolSetupRequirement {
                    RequirementId = requirement.RequirementId,
                    Kind = requirement.Kind,
                    IsRequired = requirement.IsRequired,
                    HintKeys = CloneOrEmpty(requirement.HintKeys)
                };
        }

        return cloned;
    }

    private static ToolHandoffBinding[] CloneBindingsOrEmpty(IReadOnlyList<ToolHandoffBinding>? bindings) {
        if (bindings is null || bindings.Count == 0) {
            return Array.Empty<ToolHandoffBinding>();
        }

        var cloned = new ToolHandoffBinding[bindings.Count];
        for (var i = 0; i < bindings.Count; i++) {
            var binding = bindings[i];
            cloned[i] = binding is null
                ? new ToolHandoffBinding()
                : new ToolHandoffBinding {
                    SourceField = binding.SourceField,
                    TargetArgument = binding.TargetArgument,
                    IsRequired = binding.IsRequired,
                    TransformId = binding.TransformId
                };
        }

        return cloned;
    }

    private static ToolHandoffRoute[] CloneRoutesOrEmpty(IReadOnlyList<ToolHandoffRoute>? routes) {
        if (routes is null || routes.Count == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        var cloned = new ToolHandoffRoute[routes.Count];
        for (var i = 0; i < routes.Count; i++) {
            var route = routes[i];
            cloned[i] = route is null
                ? new ToolHandoffRoute()
                : new ToolHandoffRoute {
                    TargetPackId = route.TargetPackId,
                    TargetToolName = route.TargetToolName,
                    TargetRole = route.TargetRole,
                    Reason = route.Reason,
                    Bindings = CloneBindingsOrEmpty(route.Bindings)
                };
        }

        return cloned;
    }
}
