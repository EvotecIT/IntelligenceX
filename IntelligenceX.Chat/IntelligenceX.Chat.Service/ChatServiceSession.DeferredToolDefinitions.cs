using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private void RememberDeferredToolDefinitionDescriptors(IReadOnlyList<ToolDefinition>? toolDefinitions) {
        if (toolDefinitions is not { Count: > 0 }) {
            return;
        }

        var descriptorUpdates = BuildToolDefinitionDtosFromRegistryDefinitions(toolDefinitions);
        if (descriptorUpdates.Length == 0) {
            return;
        }

        var existingDescriptors = Volatile.Read(ref _cachedToolDefinitions);
        var mergedDescriptors = MergeToolDefinitionDtos(existingDescriptors, descriptorUpdates);
        if (!ReferenceEquals(existingDescriptors, mergedDescriptors)) {
            Volatile.Write(ref _cachedToolDefinitions, mergedDescriptors);
        }
    }

    private IReadOnlyList<ToolDefinition> MergeMissingToolDefinitionsFromDeferredDescriptors(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyCollection<string>? requestedToolNames) {
        ArgumentNullException.ThrowIfNull(toolDefinitions);

        if (requestedToolNames is null || requestedToolNames.Count == 0) {
            return toolDefinitions;
        }

        var existingToolNames = new HashSet<string>(
            toolDefinitions
                .Select(static definition => NormalizeToolNameForAnswerPlan(definition?.Name))
                .Where(static toolName => toolName.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var mergedDefinitions = new List<ToolDefinition>(toolDefinitions.Count + requestedToolNames.Count);
        mergedDefinitions.AddRange(toolDefinitions);

        var addedAny = false;
        foreach (var requestedToolName in requestedToolNames) {
            var normalizedToolName = NormalizeToolNameForAnswerPlan(requestedToolName);
            if (normalizedToolName.Length == 0 || existingToolNames.Contains(normalizedToolName)) {
                continue;
            }

            if (!TryResolveDeferredToolDefinitionDto(normalizedToolName, out var descriptor)
                || !TryBuildSyntheticToolDefinition(descriptor, out var definition)) {
                continue;
            }

            mergedDefinitions.Add(definition);
            existingToolNames.Add(normalizedToolName);
            addedAny = true;
        }

        return addedAny ? mergedDefinitions : toolDefinitions;
    }

    private bool TryResolveDeferredToolDefinitionDto(string? toolName, out ToolDefinitionDto definition) {
        definition = null!;
        var normalizedToolName = NormalizeToolNameForAnswerPlan(toolName);
        if (normalizedToolName.Length == 0) {
            return false;
        }

        var cachedToolDefinitions = Volatile.Read(ref _cachedToolDefinitions);
        for (var i = 0; i < cachedToolDefinitions.Length; i++) {
            var candidate = cachedToolDefinitions[i];
            if (string.Equals(candidate.Name, normalizedToolName, StringComparison.OrdinalIgnoreCase)) {
                definition = candidate;
                return true;
            }
        }

        if (_deferredDescriptorPreviewToolDefinitions.Length == 0) {
            _ = TryGetDeferredDescriptorPreviewCapabilitySnapshot(out _);
        }

        var deferredToolDefinitions = _deferredDescriptorPreviewToolDefinitions;
        for (var i = 0; i < deferredToolDefinitions.Length; i++) {
            var candidate = deferredToolDefinitions[i];
            if (string.Equals(candidate.Name, normalizedToolName, StringComparison.OrdinalIgnoreCase)) {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildSyntheticToolDefinition(ToolDefinitionDto descriptor, out ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(descriptor);

        definition = null!;
        var normalizedName = NormalizeToolNameForAnswerPlan(descriptor.Name);
        if (normalizedName.Length == 0) {
            return false;
        }

        try {
            definition = new ToolDefinition(
                name: normalizedName,
                description: string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim(),
                parameters: TryParseToolParametersJson(descriptor.ParametersJson),
                displayName: string.IsNullOrWhiteSpace(descriptor.DisplayName) ? null : descriptor.DisplayName.Trim(),
                category: string.IsNullOrWhiteSpace(descriptor.Category) ? null : descriptor.Category.Trim(),
                tags: NormalizeSyntheticToolTags(descriptor.Tags),
                writeGovernance: BuildSyntheticWriteGovernanceContract(descriptor),
                authentication: BuildSyntheticAuthenticationContract(descriptor),
                routing: BuildSyntheticRoutingContract(descriptor),
                setup: BuildSyntheticSetupContract(descriptor),
                recovery: BuildSyntheticRecoveryContract(descriptor),
                execution: BuildSyntheticExecutionContract(descriptor));
            return true;
        } catch {
            return false;
        }
    }

    private static JsonObject? TryParseToolParametersJson(string? parametersJson) {
        var normalizedJson = (parametersJson ?? string.Empty).Trim();
        if (normalizedJson.Length == 0 || normalizedJson == "{}") {
            return null;
        }

        try {
            return JsonLite.Parse(normalizedJson)?.AsObject();
        } catch {
            return null;
        }
    }

    private static IReadOnlyList<string>? NormalizeSyntheticToolTags(IReadOnlyList<string>? tags) {
        if (tags is not { Count: > 0 }) {
            return null;
        }

        var normalizedTags = tags
            .Select(static tag => (tag ?? string.Empty).Trim())
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalizedTags.Length == 0 ? null : normalizedTags;
    }

    private static ToolWriteGovernanceContract? BuildSyntheticWriteGovernanceContract(ToolDefinitionDto descriptor) {
        if (!descriptor.IsWriteCapable
            && !descriptor.RequiresWriteGovernance
            && string.IsNullOrWhiteSpace(descriptor.WriteGovernanceContractId)) {
            return null;
        }

        return new ToolWriteGovernanceContract {
            IsWriteCapable = descriptor.IsWriteCapable || descriptor.RequiresWriteGovernance,
            RequiresGovernanceAuthorization = descriptor.RequiresWriteGovernance,
            GovernanceContractId = string.IsNullOrWhiteSpace(descriptor.WriteGovernanceContractId)
                ? ToolWriteGovernanceContract.DefaultContractId
                : descriptor.WriteGovernanceContractId!.Trim()
        };
    }

    private static ToolAuthenticationContract? BuildSyntheticAuthenticationContract(ToolDefinitionDto descriptor) {
        var authenticationArguments = NormalizeSyntheticToolArgumentList(descriptor.AuthenticationArguments);
        var supportsConnectivityProbe = descriptor.SupportsConnectivityProbe
                                        || !string.IsNullOrWhiteSpace(descriptor.ProbeToolName);
        var isAuthenticationAware = descriptor.RequiresAuthentication
                                    || supportsConnectivityProbe
                                    || !string.IsNullOrWhiteSpace(descriptor.AuthenticationContractId)
                                    || authenticationArguments.Length > 0;
        if (!isAuthenticationAware) {
            return null;
        }

        var runAsArgumentName = authenticationArguments.FirstOrDefault(argument =>
            string.Equals(argument, ToolAuthenticationArgumentNames.RunAsProfileId, StringComparison.OrdinalIgnoreCase));
        var probeArgumentName = authenticationArguments.FirstOrDefault(argument =>
            string.Equals(argument, ToolAuthenticationArgumentNames.ProbeId, StringComparison.OrdinalIgnoreCase));
        var profileArgumentName = authenticationArguments.FirstOrDefault(argument =>
            !string.Equals(argument, ToolAuthenticationArgumentNames.RunAsProfileId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(argument, ToolAuthenticationArgumentNames.ProbeId, StringComparison.OrdinalIgnoreCase));
        var mode = descriptor.RequiresAuthentication || authenticationArguments.Length > 0
            ? (runAsArgumentName is not null ? ToolAuthenticationMode.RunAsReference : ToolAuthenticationMode.ProfileReference)
            : ToolAuthenticationMode.None;

        return new ToolAuthenticationContract {
            IsAuthenticationAware = true,
            RequiresAuthentication = descriptor.RequiresAuthentication,
            AuthenticationContractId = string.IsNullOrWhiteSpace(descriptor.AuthenticationContractId)
                ? ToolAuthenticationContract.DefaultContractId
                : descriptor.AuthenticationContractId!.Trim(),
            Mode = mode,
            ProfileIdArgumentName = string.IsNullOrWhiteSpace(profileArgumentName)
                ? ToolAuthenticationArgumentNames.ProfileId
                : profileArgumentName,
            RunAsProfileIdArgumentName = string.IsNullOrWhiteSpace(runAsArgumentName)
                ? ToolAuthenticationArgumentNames.RunAsProfileId
                : runAsArgumentName,
            SupportsConnectivityProbe = supportsConnectivityProbe,
            ProbeToolName = string.IsNullOrWhiteSpace(descriptor.ProbeToolName) ? string.Empty : descriptor.ProbeToolName.Trim(),
            ProbeIdArgumentName = string.IsNullOrWhiteSpace(probeArgumentName)
                ? ToolAuthenticationArgumentNames.ProbeId
                : probeArgumentName
        };
    }

    private static ToolExecutionContract? BuildSyntheticExecutionContract(ToolDefinitionDto descriptor) {
        var targetScopeArguments = NormalizeSyntheticToolArgumentList(descriptor.TargetScopeArguments);
        var remoteHostArguments = NormalizeSyntheticToolArgumentList(descriptor.RemoteHostArguments);
        var executionScope = ToolExecutionScopes.Normalize(descriptor.ExecutionScope);
        var isExecutionAware = descriptor.IsExecutionAware
                               || !string.Equals(executionScope, ToolExecutionScopes.LocalOnly, StringComparison.OrdinalIgnoreCase)
                               || descriptor.SupportsRemoteExecution
                               || descriptor.SupportsTargetScoping
                               || descriptor.SupportsRemoteHostTargeting
                               || targetScopeArguments.Length > 0
                               || remoteHostArguments.Length > 0;
        if (!isExecutionAware) {
            return null;
        }

        return new ToolExecutionContract {
            IsExecutionAware = true,
            ExecutionContractId = string.IsNullOrWhiteSpace(descriptor.ExecutionContractId)
                ? ToolExecutionContract.DefaultContractId
                : descriptor.ExecutionContractId!.Trim(),
            ExecutionScope = executionScope,
            TargetScopeArguments = targetScopeArguments,
            RemoteHostArguments = remoteHostArguments
        };
    }

    private static ToolSetupContract? BuildSyntheticSetupContract(ToolDefinitionDto descriptor) {
        if (!descriptor.IsSetupAware
            && string.IsNullOrWhiteSpace(descriptor.SetupToolName)) {
            return null;
        }

        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = string.IsNullOrWhiteSpace(descriptor.SetupToolName) ? string.Empty : descriptor.SetupToolName.Trim()
        };
    }

    private static ToolRecoveryContract? BuildSyntheticRecoveryContract(ToolDefinitionDto descriptor) {
        var recoveryToolNames = NormalizeSyntheticToolArgumentList(descriptor.RecoveryToolNames);
        if (!descriptor.IsRecoveryAware
            && !descriptor.SupportsTransientRetry
            && descriptor.MaxRetryAttempts <= 0
            && recoveryToolNames.Length == 0) {
            return null;
        }

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = descriptor.SupportsTransientRetry || descriptor.MaxRetryAttempts > 0,
            MaxRetryAttempts = Math.Max(0, descriptor.MaxRetryAttempts),
            RecoveryToolNames = recoveryToolNames
        };
    }

    private static ToolRoutingContract? BuildSyntheticRoutingContract(ToolDefinitionDto descriptor) {
        var packId = ToolPackBootstrap.NormalizePackId(descriptor.PackId);
        var role = (descriptor.RoutingRole ?? string.Empty).Trim();
        var scope = (descriptor.RoutingScope ?? string.Empty).Trim();
        var operation = (descriptor.RoutingOperation ?? string.Empty).Trim();
        var entity = (descriptor.RoutingEntity ?? string.Empty).Trim();
        var risk = (descriptor.RoutingRisk ?? string.Empty).Trim();
        var routingSource = (descriptor.RoutingSource ?? string.Empty).Trim();
        var domainIntentFamily = (descriptor.DomainIntentFamily ?? string.Empty).Trim();
        var domainIntentActionId = (descriptor.DomainIntentActionId ?? string.Empty).Trim();
        if (domainIntentFamily.Length > 0 && domainIntentActionId.Length == 0) {
            domainIntentFamily = string.Empty;
        }

        if (packId.Length == 0
            && role.Length == 0
            && scope.Length == 0
            && operation.Length == 0
            && entity.Length == 0
            && risk.Length == 0
            && routingSource.Length == 0
            && domainIntentFamily.Length == 0
            && domainIntentActionId.Length == 0) {
            return null;
        }

        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = ToolRoutingContract.DefaultContractId,
            RoutingSource = routingSource.Length == 0 ? ToolRoutingTaxonomy.SourceExplicit : routingSource,
            PackId = packId,
            Role = role.Length == 0 ? ToolRoutingTaxonomy.RoleOperational : role,
            Scope = scope,
            Operation = operation,
            Entity = entity,
            Risk = risk,
            DomainIntentFamily = domainIntentFamily,
            DomainIntentActionId = domainIntentActionId,
            DomainIntentFamilyDisplayName = string.IsNullOrWhiteSpace(descriptor.DomainIntentFamilyDisplayName)
                ? string.Empty
                : descriptor.DomainIntentFamilyDisplayName.Trim(),
            DomainIntentFamilyReplyExample = string.IsNullOrWhiteSpace(descriptor.DomainIntentFamilyReplyExample)
                ? string.Empty
                : descriptor.DomainIntentFamilyReplyExample.Trim(),
            DomainIntentFamilyChoiceDescription = string.IsNullOrWhiteSpace(descriptor.DomainIntentFamilyChoiceDescription)
                ? string.Empty
                : descriptor.DomainIntentFamilyChoiceDescription.Trim()
        };
    }

    private static string[] NormalizeSyntheticToolArgumentList(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        return values
            .Select(static value => (value ?? string.Empty).Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
