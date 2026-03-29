using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int DeferredChatPackActivationExplicitMatchScore = 8;
    private const int DeferredChatPackActivationDirectNameMatchScore = 6;
    private const int DeferredChatPackActivationRoutingTokenHitScore = 2;
    private const int DeferredChatPackActivationMinimumStrongScore = 4;

    private readonly record struct DeferredToolPreferenceHints(
        string[] PreferredPackIds,
        string[] PreferredToolNames,
        bool HasAnyMatches,
        string[] ActivatablePackIds,
        string[] MatchedToolNames);

    private readonly record struct DeferredChatActivationPlan(
        string[] PrimaryPackIds,
        string[] HandoffPrewarmPackIds);

    private readonly record struct DeferredHandoffPreferenceHints(
        string[] PreferredPackIds,
        string[] PreferredToolNames);

    private readonly record struct DeferredToolMatchCandidate(
        string ToolName,
        string PackId,
        int ExplicitMatchCount,
        int DirectNameMatchCount,
        int Score);

    private readonly record struct DeferredChatPackActivationCandidate(
        string PackId,
        int ExplicitMatchCount,
        int DirectNameMatchCount,
        int MaxScore,
        int AggregateScore,
        int MatchCount);

    private static string BuildDeferredActivatedPackToolScopeMessage(IReadOnlyList<string> packIds) {
        if (packIds is not { Count: > 0 }) {
            return "Scoped live tool schemas to the descriptor-matched active pack for this turn.";
        }

        var normalizedPackIds = NormalizeDistinctStrings(
            packIds.Select(static packId => ToolPackBootstrap.NormalizePackId(packId)),
            maxItems: 4);
        if (normalizedPackIds.Length == 0) {
            return "Scoped live tool schemas to the descriptor-matched active pack for this turn.";
        }

        return normalizedPackIds.Length == 1
            ? $"Scoped live tool schemas to active pack '{normalizedPackIds[0]}' for this turn."
            : $"Scoped live tool schemas to active packs '{string.Join(", ", normalizedPackIds)}' for this turn.";
    }

    private static string BuildDeferredActivatedPackRoundScopeMessage(IReadOnlyList<string> packIds) {
        if (packIds is not { Count: > 0 }) {
            return "Refreshed live tool schemas to the active pack after the latest tool round.";
        }

        var normalizedPackIds = NormalizeDistinctStrings(
            packIds.Select(static packId => ToolPackBootstrap.NormalizePackId(packId)),
            maxItems: 4);
        if (normalizedPackIds.Length == 0) {
            return "Refreshed live tool schemas to the active pack after the latest tool round.";
        }

        return normalizedPackIds.Length == 1
            ? $"Refreshed live tool schemas to active pack '{normalizedPackIds[0]}' after the latest tool round."
            : $"Refreshed live tool schemas to active packs '{string.Join(", ", normalizedPackIds)}' after the latest tool round.";
    }

    private static string BuildDeferredHandoffTargetPackActivationMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activated a handoff target pack for the next same-turn tool phase."
            : $"Activated handoff target pack '{normalizedPackId}' for the next same-turn tool phase.";
    }

    private static string BuildDeferredHandoffTargetPackActivationReadyMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Handoff target pack was already active for the next same-turn tool phase."
            : $"Handoff target pack '{normalizedPackId}' was already active for the next same-turn tool phase.";
    }

    private static string BuildDeferredHandoffTargetPackActivationPendingMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activating a handoff target pack for the next same-turn tool phase..."
            : $"Activating handoff target pack '{normalizedPackId}' for the next same-turn tool phase...";
    }

    private static string BuildDeferredChatPackActivationMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activated a descriptor-matched pack before chat routing."
            : $"Activated descriptor-matched pack '{normalizedPackId}' before chat routing.";
    }

    private static string BuildDeferredChatPackActivationReadyMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Descriptor-matched pack was already active before chat routing."
            : $"Descriptor-matched pack '{normalizedPackId}' was already active before chat routing.";
    }

    private static string BuildDeferredChatPackActivationPendingMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activating a descriptor-matched pack before chat routing..."
            : $"Activating descriptor-matched pack '{normalizedPackId}' before chat routing...";
    }

    private static string BuildDeferredBootstrapAvoidancePackActivationMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activated a descriptor-matched pack before waiting for tooling bootstrap."
            : $"Activated descriptor-matched pack '{normalizedPackId}' before waiting for tooling bootstrap.";
    }

    private static string BuildDeferredBootstrapAvoidancePackActivationReadyMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Descriptor-matched pack was already active before waiting for tooling bootstrap."
            : $"Descriptor-matched pack '{normalizedPackId}' was already active before waiting for tooling bootstrap.";
    }

    private static string BuildDeferredBootstrapAvoidancePackActivationPendingMessage(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        return normalizedPackId.Length == 0
            ? "Activating a descriptor-matched pack before waiting for tooling bootstrap..."
            : $"Activating descriptor-matched pack '{normalizedPackId}' before waiting for tooling bootstrap...";
    }

    private static bool ShouldAttemptDeferredChatPackActivation(
        bool isChatRequest,
        bool startupToolingBootstrapStarted,
        bool hasExplicitToolEnableSelectors,
        bool continuationContractDetected,
        bool executionContractApplies,
        bool hasPendingActionContext,
        bool hasToolActivity) {
        return isChatRequest
               && !startupToolingBootstrapStarted
               && !hasExplicitToolEnableSelectors
               && !continuationContractDetected
               && !executionContractApplies
               && !hasPendingActionContext
               && !hasToolActivity;
    }

    private async Task<string[]> TryActivateDeferredHandoffTargetPacksAfterRoundAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        IReadOnlyList<ToolDefinition> activeDefinitions,
        IReadOnlyList<ToolCall> recentCalls,
        bool hasExplicitToolEnableSelectors,
        bool continuationContractDetected,
        bool executionContractApplies,
        bool hasPendingActionContext,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(activeDefinitions);
        ArgumentNullException.ThrowIfNull(recentCalls);

        if (activeDefinitions.Count == 0
            || recentCalls.Count == 0
            || hasExplicitToolEnableSelectors
            || continuationContractDetected
            || executionContractApplies
            || hasPendingActionContext
            || !TryResolveSingleExecutedPackId(recentCalls, activeDefinitions, out var executedPackId)) {
            return Array.Empty<string>();
        }

        var inactiveTargetPackIds = CollectCrossPackHandoffTargetPackIds(recentCalls, activeDefinitions, executedPackId)
            .Where(static packId => packId.Length > 0)
            .Where(packId => !IsPackCurrentlyActive(packId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inactiveTargetPackIds.Length == 0) {
            return Array.Empty<string>();
        }

        var activatedPackIds = new List<string>(inactiveTargetPackIds.Length);
        for (var i = 0; i < inactiveTargetPackIds.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var packId = inactiveTargetPackIds[i];
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: BuildDeferredHandoffTargetPackActivationPendingMessage(packId))
                .ConfigureAwait(false);

            if (!TryActivatePackOnDemand(packId, out _)) {
                continue;
            }

            activatedPackIds.Add(packId);
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: BuildDeferredHandoffTargetPackActivationMessage(packId))
                .ConfigureAwait(false);
        }

        return activatedPackIds.Count == 0
            ? Array.Empty<string>()
            : NormalizeDistinctStrings(activatedPackIds, maxItems: 8);
    }

    private async Task<bool> TryPrepareDeferredChatToolingForRequestAsync(
        StreamWriter writer,
        string requestId,
        ChatRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);

        var normalizedThreadId = (request.ThreadId ?? string.Empty).Trim();
        var shouldAttemptRoutingPhaseActivation = ShouldAttemptDeferredChatPackActivation(
                isChatRequest: true,
                startupToolingBootstrapStarted: Volatile.Read(ref _startupToolingBootstrapTask) is not null,
                hasExplicitToolEnableSelectors: HasExplicitToolEnableSelectors(request.Options),
                continuationContractDetected: TryReadContinuationContractFromRequestText(request.Text ?? string.Empty, out _, out _),
                executionContractApplies: ShouldEnforceExecuteOrExplainContract(request.Text ?? string.Empty),
                hasPendingActionContext: normalizedThreadId.Length > 0 && HasFreshPendingActionsContext(normalizedThreadId),
                hasToolActivity: normalizedThreadId.Length > 0 && HasFreshThreadToolEvidence(normalizedThreadId));
        if (shouldAttemptRoutingPhaseActivation
            && await TryActivateDeferredMatchedPacksAsync(
                    writer,
                    requestId,
                    normalizedThreadId,
                    request.Text ?? string.Empty,
                    request.Options,
                    BuildDeferredChatPackActivationReadyMessage,
                    BuildDeferredChatPackActivationPendingMessage,
                    BuildDeferredChatPackActivationMessage,
                    cancellationToken)
                .ConfigureAwait(false)) {
            return true;
        }

        if (Volatile.Read(ref _startupToolingBootstrapTask) is not null) {
            return false;
        }

        return await TryActivateDeferredMatchedPacksAsync(
                writer,
                requestId,
                normalizedThreadId,
                request.Text ?? string.Empty,
                request.Options,
                BuildDeferredBootstrapAvoidancePackActivationReadyMessage,
                BuildDeferredBootstrapAvoidancePackActivationPendingMessage,
                BuildDeferredBootstrapAvoidancePackActivationMessage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryActivateDeferredMatchedPacksAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string requestText,
        ChatRequestOptions? options,
        Func<string, string> activeStatusMessageFactory,
        Func<string, string> pendingStatusMessageFactory,
        Func<string, string> activatedStatusMessageFactory,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(activeStatusMessageFactory);
        ArgumentNullException.ThrowIfNull(pendingStatusMessageFactory);
        ArgumentNullException.ThrowIfNull(activatedStatusMessageFactory);

        if (!TryResolveDeferredActivationPlanForChatRequest(requestText, options, out var activationPlan)
            || (activationPlan.PrimaryPackIds.Length == 0 && activationPlan.HandoffPrewarmPackIds.Length == 0)) {
            return false;
        }

        var prepared = await TryActivateDeferredPackIdsAsync(
                writer,
                requestId,
                threadId,
                activationPlan.PrimaryPackIds,
                activeStatusMessageFactory,
                pendingStatusMessageFactory,
                activatedStatusMessageFactory,
                cancellationToken)
            .ConfigureAwait(false);
        prepared |= await TryActivateDeferredPackIdsAsync(
                writer,
                requestId,
                threadId,
                activationPlan.HandoffPrewarmPackIds,
                BuildDeferredHandoffTargetPackActivationReadyMessage,
                BuildDeferredHandoffTargetPackActivationPendingMessage,
                BuildDeferredHandoffTargetPackActivationMessage,
                cancellationToken)
            .ConfigureAwait(false);
        return prepared;
    }

    private async Task<bool> TryActivateDeferredPackIdsAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        IReadOnlyList<string> packIds,
        Func<string, string> activeStatusMessageFactory,
        Func<string, string> pendingStatusMessageFactory,
        Func<string, string> activatedStatusMessageFactory,
        CancellationToken cancellationToken) {
        if (packIds is not { Count: > 0 }) {
            return false;
        }

        var prepared = false;
        for (var i = 0; i < packIds.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var packId = ToolPackBootstrap.NormalizePackId(packIds[i]);
            if (packId.Length == 0) {
                continue;
            }

            if (_packs.Any(pack => string.Equals(
                    ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id),
                    packId,
                    StringComparison.OrdinalIgnoreCase))) {
                prepared = true;
                await TryWriteStatusAsync(
                        writer,
                        requestId,
                        threadId,
                        status: ChatStatusCodes.Routing,
                        message: activeStatusMessageFactory(packId))
                    .ConfigureAwait(false);
                continue;
            }

            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: pendingStatusMessageFactory(packId))
                .ConfigureAwait(false);

            if (TryActivatePackOnDemand(packId, out _)) {
                prepared = true;
                await TryWriteStatusAsync(
                        writer,
                        requestId,
                        threadId,
                        status: ChatStatusCodes.Routing,
                        message: activatedStatusMessageFactory(packId))
                    .ConfigureAwait(false);
            }
        }

        return prepared;
    }

    private bool TryResolveDeferredActivationPlanForChatRequest(
        string requestText,
        ChatRequestOptions? options,
        [NotNullWhen(true)] out DeferredChatActivationPlan activationPlan) {
        var hints = ResolveDeferredToolPreferenceHints(
            requestText,
            options,
            maxPreferredPackIds: 1,
            maxPreferredToolNames: 1);
        var primaryPackIds = NormalizeDistinctStrings(hints.ActivatablePackIds ?? Array.Empty<string>(), maxItems: 1);
        if (primaryPackIds.Length == 0) {
            activationPlan = default;
            return false;
        }

        var handoffPrewarmPackIds = Array.Empty<string>();
        var matchedToolNames = NormalizeDistinctStrings(hints.MatchedToolNames ?? Array.Empty<string>(), maxItems: 8);
        if (matchedToolNames.Length > 0) {
            var primaryPackIdSet = new HashSet<string>(primaryPackIds, StringComparer.OrdinalIgnoreCase);
            var matchedToolNameSet = new HashSet<string>(matchedToolNames, StringComparer.OrdinalIgnoreCase);
            handoffPrewarmPackIds = GetDeferredToolDefinitionsForBootstrapDecision(options)
                .Where(definition => definition is not null
                                     && primaryPackIdSet.Contains(ToolPackBootstrap.NormalizePackId(definition.PackId))
                                     && matchedToolNameSet.Contains(NormalizeToolNameForAnswerPlan(definition.Name)))
                .SelectMany(static definition => definition.HandoffTargetPackIds ?? Array.Empty<string>())
                .Select(static packId => ToolPackBootstrap.NormalizePackId(packId))
                .Where(packId => packId.Length > 0 && !primaryPackIdSet.Contains(packId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        activationPlan = new DeferredChatActivationPlan(primaryPackIds, handoffPrewarmPackIds);
        return true;
    }

    private bool TryResolveDeferredActivationPackIdsForChatRequest(
        string requestText,
        ChatRequestOptions? options,
        [NotNullWhen(true)] out string[]? packIds) {
        packIds = TryResolveDeferredActivationPlanForChatRequest(requestText, options, out var activationPlan)
            ? activationPlan.PrimaryPackIds
            : Array.Empty<string>();
        return packIds.Length > 0;
    }

    private DeferredToolPreferenceHints ResolveDeferredToolPreferenceHints(
        string requestText,
        ChatRequestOptions? options,
        int maxPreferredPackIds,
        int maxPreferredToolNames) {
        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (string.IsNullOrWhiteSpace(userRequest) || ShouldSkipWeightedRouting(userRequest)) {
            return new DeferredToolPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>(),
                HasAnyMatches: false,
                ActivatablePackIds: Array.Empty<string>(),
                MatchedToolNames: Array.Empty<string>());
        }

        var candidateDefinitions = GetDeferredToolDefinitionsForBootstrapDecision(options);
        if (candidateDefinitions.Length == 0) {
            return new DeferredToolPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>(),
                HasAnyMatches: false,
                ActivatablePackIds: Array.Empty<string>(),
                MatchedToolNames: Array.Empty<string>());
        }

        var explicitRequestedToolNames = BuildExplicitRequestedToolNameSet(userRequest);
        var routingTokens = TokenizeRoutingTokens(userRequest, maxTokens: 16);
        var focusTokens = ResolveWeightedRoutingFocusTokens(requestText, routingTokens);
        var searchTexts = new string[candidateDefinitions.Length];
        for (var i = 0; i < candidateDefinitions.Length; i++) {
            searchTexts[i] = BuildDeferredToolRoutingSearchText(candidateDefinitions[i]);
        }

        var maxTokenSupport = Math.Max(1, (int)Math.Ceiling(candidateDefinitions.Length * 0.55d));
        var toolCandidates = new List<DeferredToolMatchCandidate>(candidateDefinitions.Length);
        var candidatesByPackId = new Dictionary<string, DeferredChatPackActivationCandidate>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < candidateDefinitions.Length; i++) {
            var definition = candidateDefinitions[i];
            var normalizedPackId = ToolPackBootstrap.NormalizePackId(definition.PackId);
            var normalizedToolName = NormalizeToolNameForAnswerPlan(definition.Name);
            if (normalizedPackId.Length == 0 || normalizedToolName.Length == 0) {
                continue;
            }

            var explicitToolMatch = IsExplicitRequestedToolMatch(definition.Name, explicitRequestedToolNames);
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            var tokenHits = CountSupportedDeferredToolTokenHits(searchTexts, searchTexts[i], routingTokens, maxTokenSupport);
            var focusHits = CountSupportedDeferredToolTokenHits(searchTexts, searchTexts[i], focusTokens, maxTokenSupport);
            var score = 0;
            if (explicitToolMatch) {
                score += DeferredChatPackActivationExplicitMatchScore;
            }

            if (directNameMatch) {
                score += DeferredChatPackActivationDirectNameMatchScore;
            }

            score += tokenHits * DeferredChatPackActivationRoutingTokenHitScore;
            score += focusHits;
            if (score <= 0) {
                continue;
            }

            toolCandidates.Add(new DeferredToolMatchCandidate(
                ToolName: normalizedToolName,
                PackId: normalizedPackId,
                ExplicitMatchCount: explicitToolMatch ? 1 : 0,
                DirectNameMatchCount: directNameMatch ? 1 : 0,
                Score: score));

            if (!candidatesByPackId.TryGetValue(normalizedPackId, out var candidate)) {
                candidate = new DeferredChatPackActivationCandidate(
                    PackId: normalizedPackId,
                    ExplicitMatchCount: 0,
                    DirectNameMatchCount: 0,
                    MaxScore: 0,
                    AggregateScore: 0,
                    MatchCount: 0);
            }

            candidatesByPackId[normalizedPackId] = candidate with {
                ExplicitMatchCount = candidate.ExplicitMatchCount + (explicitToolMatch ? 1 : 0),
                DirectNameMatchCount = candidate.DirectNameMatchCount + (directNameMatch ? 1 : 0),
                MaxScore = Math.Max(candidate.MaxScore, score),
                AggregateScore = candidate.AggregateScore + score,
                MatchCount = candidate.MatchCount + 1
            };
        }

        if (candidatesByPackId.Count == 0) {
            return new DeferredToolPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>(),
                HasAnyMatches: false,
                ActivatablePackIds: Array.Empty<string>(),
                MatchedToolNames: Array.Empty<string>());
        }

        var orderedPackCandidates = OrderDeferredActivationPackCandidates(candidatesByPackId);
        var packRank = orderedPackCandidates
            .Select((candidate, index) => (candidate.PackId, Index: index))
            .ToDictionary(static entry => entry.PackId, static entry => entry.Index, StringComparer.OrdinalIgnoreCase);
        var preferredPackIds = orderedPackCandidates
            .Select(static candidate => candidate.PackId)
            .ToArray();
        var preferredToolNames = toolCandidates
            .OrderBy(candidate => packRank.TryGetValue(candidate.PackId, out var index) ? index : int.MaxValue)
            .ThenByDescending(static candidate => candidate.ExplicitMatchCount)
            .ThenByDescending(static candidate => candidate.DirectNameMatchCount)
            .ThenByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.ToolName)
            .ToArray();
        var handoffPreferenceHints = ResolveDeferredHandoffPreferenceHints(candidateDefinitions, toolCandidates, packRank);
        preferredPackIds = NormalizeDistinctStrings(
            preferredPackIds.Concat(handoffPreferenceHints.PreferredPackIds),
            Math.Max(0, maxPreferredPackIds));
        preferredToolNames = NormalizeDistinctStrings(
            preferredToolNames.Concat(handoffPreferenceHints.PreferredToolNames),
            Math.Max(0, maxPreferredToolNames));
        var matchedToolNames = toolCandidates
            .OrderBy(candidate => packRank.TryGetValue(candidate.PackId, out var index) ? index : int.MaxValue)
            .ThenByDescending(static candidate => candidate.ExplicitMatchCount)
            .ThenByDescending(static candidate => candidate.DirectNameMatchCount)
            .ThenByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activatablePackIds = TryResolveStrongDeferredActivationPackCandidate(orderedPackCandidates, out var activatablePackId)
            ? new[] { activatablePackId }
            : Array.Empty<string>();
        return new DeferredToolPreferenceHints(
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            HasAnyMatches: preferredPackIds.Length > 0 || preferredToolNames.Length > 0,
            ActivatablePackIds: activatablePackIds,
            MatchedToolNames: matchedToolNames);
    }

    private static DeferredHandoffPreferenceHints ResolveDeferredHandoffPreferenceHints(
        IReadOnlyList<ToolDefinitionDto> candidateDefinitions,
        IReadOnlyList<DeferredToolMatchCandidate> toolCandidates,
        IReadOnlyDictionary<string, int> packRank) {
        if (candidateDefinitions.Count == 0 || toolCandidates.Count == 0) {
            return new DeferredHandoffPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>());
        }

        var matchedToolNames = new HashSet<string>(
            toolCandidates
                .Select(static candidate => candidate.ToolName)
                .Where(static toolName => toolName.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (matchedToolNames.Count == 0) {
            return new DeferredHandoffPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>());
        }

        var matchingDefinitions = candidateDefinitions
            .Where(definition => definition is not null
                                 && matchedToolNames.Contains(NormalizeToolNameForAnswerPlan(definition.Name)))
            .ToArray();
        if (matchingDefinitions.Length == 0) {
            return new DeferredHandoffPreferenceHints(
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>());
        }

        var handoffPackIds = matchingDefinitions
            .SelectMany(static definition => definition.HandoffTargetPackIds ?? Array.Empty<string>())
            .Select(static packId => ToolPackBootstrap.NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(packId => packRank.TryGetValue(packId, out var index) ? index : int.MaxValue)
            .ThenBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var handoffToolNames = matchingDefinitions
            .SelectMany(static definition => definition.HandoffTargetToolNames ?? Array.Empty<string>())
            .Select(static toolName => NormalizeToolNameForAnswerPlan(toolName))
            .Where(static toolName => toolName.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(toolName => ResolveDeferredHandoffToolRank(toolName, candidateDefinitions, packRank))
            .ThenBy(static toolName => toolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DeferredHandoffPreferenceHints(
            PreferredPackIds: handoffPackIds,
            PreferredToolNames: handoffToolNames);
    }

    private static int ResolveDeferredHandoffToolRank(
        string toolName,
        IReadOnlyList<ToolDefinitionDto> candidateDefinitions,
        IReadOnlyDictionary<string, int> packRank) {
        for (var i = 0; i < candidateDefinitions.Count; i++) {
            var definition = candidateDefinitions[i];
            if (!string.Equals(
                    NormalizeToolNameForAnswerPlan(definition.Name),
                    toolName,
                    StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var packId = ToolPackBootstrap.NormalizePackId(definition.PackId);
            if (packRank.TryGetValue(packId, out var index)) {
                return index;
            }
        }

        return int.MaxValue;
    }

    private bool TryApplyDeferredActivatedPackToolScope(
        string requestText,
        ChatRequestOptions? options,
        IReadOnlyList<ToolDefinition> definitions,
        bool hasExplicitToolEnableSelectors,
        bool continuationContractDetected,
        bool executionContractApplies,
        bool hasPendingActionContext,
        bool hasToolActivity,
        [NotNullWhen(true)] out IReadOnlyList<ToolDefinition>? scopedDefinitions,
        [NotNullWhen(true)] out string[]? scopedPackIds) {
        scopedDefinitions = null;
        scopedPackIds = null;

        if (definitions is not { Count: > 1 }
            || !ShouldAttemptDeferredChatPackActivation(
                isChatRequest: true,
                startupToolingBootstrapStarted: false,
                hasExplicitToolEnableSelectors: hasExplicitToolEnableSelectors,
                continuationContractDetected: continuationContractDetected,
                executionContractApplies: executionContractApplies,
                hasPendingActionContext: hasPendingActionContext,
                hasToolActivity: hasToolActivity)
            || !TryResolveDeferredActivationPackIdsForChatRequest(requestText, options, out var requestedPackIds)
            || requestedPackIds.Length == 0) {
            return false;
        }

        var activePackIds = requestedPackIds
            .Select(static packId => ToolPackBootstrap.NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Where(IsPackCurrentlyActive)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activePackIds.Length == 0) {
            return false;
        }

        var activePackIdSet = new HashSet<string>(activePackIds, StringComparer.OrdinalIgnoreCase);
        var filteredDefinitions = definitions
            .Where(definition => activePackIdSet.Contains(ResolveToolPackId(definition, _toolOrchestrationCatalog)))
            .ToArray();
        filteredDefinitions = OrderScopedDefinitionsForDeferredActivation(requestText, options, filteredDefinitions).ToArray();
        if (filteredDefinitions.Length == 0 || filteredDefinitions.Length >= definitions.Count) {
            return false;
        }

        scopedDefinitions = filteredDefinitions;
        scopedPackIds = activePackIds;
        return true;
    }

    private bool TryApplyDeferredActivatedPackToolScopeAfterRound(
        string requestText,
        ChatRequestOptions? options,
        IReadOnlyList<ToolDefinition> activeDefinitions,
        IReadOnlyList<ToolCall> recentCalls,
        bool hasExplicitToolEnableSelectors,
        bool continuationContractDetected,
        bool executionContractApplies,
        bool hasPendingActionContext,
        IReadOnlyList<ToolDefinition>? currentVisibleDefinitions,
        [NotNullWhen(true)] out IReadOnlyList<ToolDefinition>? scopedDefinitions,
        [NotNullWhen(true)] out string[]? scopedPackIds) {
        scopedDefinitions = null;
        scopedPackIds = null;

        if (activeDefinitions is not { Count: > 1 }
            || recentCalls is not { Count: > 0 }
            || hasExplicitToolEnableSelectors
            || continuationContractDetected
            || executionContractApplies
            || hasPendingActionContext
            || !TryResolveDeferredActivationPackIdsForChatRequest(requestText, options, out var requestedPackIds)
            || requestedPackIds.Length == 0) {
            return false;
        }

        var activePackIds = requestedPackIds
            .Select(static packId => ToolPackBootstrap.NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Where(IsPackCurrentlyActive)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activePackIds.Length != 1) {
            return false;
        }

        var candidatePackId = activePackIds[0];
        if (!TryResolveSingleExecutedPackId(recentCalls, activeDefinitions, out var executedPackId)
            || !string.Equals(executedPackId, candidatePackId, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var handoffPackIds = CollectCrossPackHandoffTargetPackIds(recentCalls, activeDefinitions, candidatePackId);
        var scopePackIds = handoffPackIds.Length > 0
            ? NormalizeDistinctStrings(new[] { candidatePackId }.Concat(handoffPackIds), maxItems: 8)
            : new[] { candidatePackId };
        var scopePackIdSet = new HashSet<string>(scopePackIds, StringComparer.OrdinalIgnoreCase);
        var filteredDefinitions = activeDefinitions
            .Where(definition => scopePackIdSet.Contains(ResolveToolPackId(definition, _toolOrchestrationCatalog)))
            .ToArray();
        filteredDefinitions = TrimRedundantSiblingHelperToolsFromRoundScope(filteredDefinitions, recentCalls).ToArray();
        filteredDefinitions = OrderScopedDefinitionsByPreferredTargets(
            filteredDefinitions,
            CollectRoundHandoffTargetToolNames(recentCalls, activeDefinitions)).ToArray();
        var comparisonDefinitions = currentVisibleDefinitions ?? activeDefinitions;
        if (filteredDefinitions.Length == 0 || HasEquivalentToolDefinitionSet(filteredDefinitions, comparisonDefinitions)) {
            return false;
        }

        scopedDefinitions = filteredDefinitions;
        scopedPackIds = scopePackIds;
        return true;
    }

    private IReadOnlyList<ToolDefinition> TrimRedundantSiblingHelperToolsFromRoundScope(
        IReadOnlyList<ToolDefinition> scopedDefinitions,
        IReadOnlyList<ToolCall> recentCalls) {
        if (scopedDefinitions is not { Count: > 1 } || recentCalls is not { Count: > 0 }) {
            return scopedDefinitions ?? Array.Empty<ToolDefinition>();
        }

        var handoffTargetToolNames = CollectRoundHandoffTargetToolNames(recentCalls, scopedDefinitions);
        var exactTargetToolNames = BuildExactContractTargetToolNameSet(Array.Empty<string>(), handoffTargetToolNames);
        if (exactTargetToolNames.Count == 0) {
            return scopedDefinitions;
        }

        var helperDemandByToolName = BuildContractHelperDemandByToolName(scopedDefinitions, _toolOrchestrationCatalog);
        var directHelperDemandByToolName = BuildContractHelperDemandByToolName(scopedDefinitions, toolOrchestrationCatalog: null);
        foreach (var entry in directHelperDemandByToolName) {
            AddContractHelperDemand(helperDemandByToolName, entry.Key, entry.Value);
        }

        var suppressibleExactTargetPackIds = ResolveExactTargetPackIdsWithoutRequiredHelpers(
            scopedDefinitions,
            _toolOrchestrationCatalog,
            exactTargetToolNames);
        suppressibleExactTargetPackIds.UnionWith(ResolveExactTargetPackIdsWithoutRequiredHelpers(
            scopedDefinitions,
            toolOrchestrationCatalog: null,
            exactTargetToolNames));
        if (suppressibleExactTargetPackIds.Count == 0) {
            return scopedDefinitions;
        }

        return TrimRedundantSiblingHelperTools(
            scopedDefinitions,
            scopedDefinitions,
            scopedDefinitions.Count,
            _toolOrchestrationCatalog,
            exactTargetToolNames,
            helperDemandByToolName,
            suppressibleExactTargetPackIds);
    }

    private IReadOnlyList<ToolDefinition> OrderScopedDefinitionsForDeferredActivation(
        string requestText,
        ChatRequestOptions? options,
        IReadOnlyList<ToolDefinition> scopedDefinitions) {
        if (scopedDefinitions is not { Count: > 1 }) {
            return scopedDefinitions ?? Array.Empty<ToolDefinition>();
        }

        var hints = ResolveDeferredToolPreferenceHints(
            requestText,
            options,
            maxPreferredPackIds: 1,
            maxPreferredToolNames: 4);
        var preferredToolNames = NormalizeDistinctStrings(
            (hints.PreferredToolNames ?? Array.Empty<string>())
            .Concat(BuildExplicitRequestedToolNameSet(ExtractPrimaryUserRequest(requestText)) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
            maxItems: 4);
        return OrderScopedDefinitionsByPreferredTargets(scopedDefinitions, preferredToolNames);
    }

    private IReadOnlyList<ToolDefinition> OrderScopedDefinitionsByPreferredTargets(
        IReadOnlyList<ToolDefinition> scopedDefinitions,
        IReadOnlyList<string> preferredToolNames) {
        if (scopedDefinitions is not { Count: > 1 } || preferredToolNames is not { Count: > 0 }) {
            return scopedDefinitions ?? Array.Empty<ToolDefinition>();
        }

        var preferredToolNameOrder = preferredToolNames
            .Select((toolName, index) => (ToolName: NormalizeToolNameForAnswerPlan(toolName), Index: index))
            .Where(static entry => entry.ToolName.Length > 0)
            .DistinctBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static entry => entry.ToolName, static entry => entry.Index, StringComparer.OrdinalIgnoreCase);
        if (preferredToolNameOrder.Count == 0) {
            return scopedDefinitions;
        }

        var helperDemandByToolName = BuildContractHelperDemandByToolName(scopedDefinitions, _toolOrchestrationCatalog);
        return scopedDefinitions
            .Select((definition, index) => (Definition: definition, Index: index))
            .OrderBy(entry => preferredToolNameOrder.TryGetValue(NormalizeToolNameForAnswerPlan(entry.Definition.Name), out var targetIndex) ? targetIndex : int.MaxValue)
            .ThenByDescending(entry => preferredToolNameOrder.ContainsKey(NormalizeToolNameForAnswerPlan(entry.Definition.Name)))
            .ThenByDescending(entry => GetContractHelperDemand(entry.Definition.Name, helperDemandByToolName))
            .ThenBy(entry => entry.Index)
            .Select(static entry => entry.Definition)
            .ToArray();
    }

    private string[] CollectRoundHandoffTargetToolNames(
        IReadOnlyList<ToolCall> recentCalls,
        IReadOnlyList<ToolDefinition> activeDefinitions) {
        if (recentCalls is null || recentCalls.Count == 0 || activeDefinitions is null || activeDefinitions.Count == 0) {
            return Array.Empty<string>();
        }

        var targetToolNames = new List<string>();
        for (var i = 0; i < recentCalls.Count; i++) {
            var toolName = NormalizeToolNameForAnswerPlan(recentCalls[i]?.Name);
            if (toolName.Length == 0) {
                continue;
            }

            if (TryGetToolDefinitionByName(activeDefinitions, toolName, out var definition)
                && definition.Handoff?.IsHandoffAware == true
                && definition.Handoff.OutboundRoutes.Count > 0) {
                for (var routeIndex = 0; routeIndex < definition.Handoff.OutboundRoutes.Count; routeIndex++) {
                    var targetToolName = NormalizeToolNameForAnswerPlan(definition.Handoff.OutboundRoutes[routeIndex].TargetToolName);
                    if (targetToolName.Length > 0) {
                        targetToolNames.Add(targetToolName);
                    }
                }

                continue;
            }

            if (!_toolOrchestrationCatalog.TryGetEntry(toolName, out var entry)
                || entry.HandoffEdges.Count == 0) {
                continue;
            }

            for (var edgeIndex = 0; edgeIndex < entry.HandoffEdges.Count; edgeIndex++) {
                var targetToolName = NormalizeToolNameForAnswerPlan(entry.HandoffEdges[edgeIndex].TargetToolName);
                if (targetToolName.Length > 0) {
                    targetToolNames.Add(targetToolName);
                }
            }
        }

        return NormalizeDistinctStrings(targetToolNames, MaxPlannerContextHandoffTargets);
    }

    private static bool HasEquivalentToolDefinitionSet(
        IReadOnlyList<ToolDefinition> left,
        IReadOnlyList<ToolDefinition> right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count != right.Count) {
            return false;
        }

        var leftNames = new HashSet<string>(
            left.Select(static definition => (definition.Name ?? string.Empty).Trim())
                .Where(static name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (leftNames.Count != right.Count) {
            return false;
        }

        for (var i = 0; i < right.Count; i++) {
            var name = (right[i].Name ?? string.Empty).Trim();
            if (name.Length == 0 || !leftNames.Contains(name)) {
                return false;
            }
        }

        return true;
    }

    private bool TryResolveSingleExecutedPackId(
        IReadOnlyList<ToolCall> recentCalls,
        IReadOnlyList<ToolDefinition> activeDefinitions,
        [NotNullWhen(true)] out string? packId) {
        packId = null;

        var resolvedPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < recentCalls.Count; i++) {
            var toolName = (recentCalls[i]?.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var resolvedPackId = string.Empty;
            if (_toolOrchestrationCatalog.TryGetEntry(toolName, out var entry)) {
                resolvedPackId = ToolPackBootstrap.NormalizePackId(entry.PackId);
            }

            if (resolvedPackId.Length == 0
                && TryGetToolDefinitionByName(activeDefinitions, toolName, out var definition)) {
                resolvedPackId = ResolveToolPackId(definition, _toolOrchestrationCatalog);
            }

            if (resolvedPackId.Length == 0) {
                continue;
            }

            resolvedPackIds.Add(resolvedPackId);
            if (resolvedPackIds.Count > 1) {
                return false;
            }
        }

        if (resolvedPackIds.Count != 1) {
            return false;
        }

        packId = resolvedPackIds.First();
        return true;
    }

    private string[] CollectCrossPackHandoffTargetPackIds(
        IReadOnlyList<ToolCall> recentCalls,
        IReadOnlyList<ToolDefinition> activeDefinitions,
        string sourcePackId) {
        var normalizedSourcePackId = ToolPackBootstrap.NormalizePackId(sourcePackId);
        if (normalizedSourcePackId.Length == 0) {
            return Array.Empty<string>();
        }

        var targetPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < recentCalls.Count; i++) {
            var toolName = (recentCalls[i]?.Name ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            if (TryGetToolDefinitionByName(activeDefinitions, toolName, out var definition)
                && definition.Handoff?.IsHandoffAware == true
                && definition.Handoff.OutboundRoutes.Count > 0) {
                for (var routeIndex = 0; routeIndex < definition.Handoff.OutboundRoutes.Count; routeIndex++) {
                    var targetPackId = ToolPackBootstrap.NormalizePackId(definition.Handoff.OutboundRoutes[routeIndex].TargetPackId);
                    if (targetPackId.Length == 0) {
                        continue;
                    }

                    if (!string.Equals(targetPackId, normalizedSourcePackId, StringComparison.OrdinalIgnoreCase)) {
                        targetPackIds.Add(targetPackId);
                    }
                }

                continue;
            }

            if (!_toolOrchestrationCatalog.TryGetEntry(toolName, out var entry)
                || !entry.IsHandoffAware
                || entry.HandoffEdges.Count == 0) {
                continue;
            }

            for (var edgeIndex = 0; edgeIndex < entry.HandoffEdges.Count; edgeIndex++) {
                var targetPackId = ToolPackBootstrap.NormalizePackId(entry.HandoffEdges[edgeIndex].TargetPackId);
                if (targetPackId.Length == 0) {
                    continue;
                }

                if (!string.Equals(targetPackId, normalizedSourcePackId, StringComparison.OrdinalIgnoreCase)) {
                    targetPackIds.Add(targetPackId);
                }
            }
        }

        return targetPackIds.Count == 0
            ? Array.Empty<string>()
            : targetPackIds.ToArray();
    }

    private bool IsPackCurrentlyActive(string packId) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        return _packs.Any(pack => string.Equals(
            ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id),
            normalizedPackId,
            StringComparison.OrdinalIgnoreCase));
    }

    private static DeferredChatPackActivationCandidate[] OrderDeferredActivationPackCandidates(
        IReadOnlyDictionary<string, DeferredChatPackActivationCandidate> candidatesByPackId) {
        ArgumentNullException.ThrowIfNull(candidatesByPackId);

        return candidatesByPackId.Values
            .OrderByDescending(static candidate => candidate.ExplicitMatchCount)
            .ThenByDescending(static candidate => candidate.DirectNameMatchCount)
            .ThenByDescending(static candidate => candidate.MaxScore)
            .ThenByDescending(static candidate => candidate.AggregateScore)
            .ThenByDescending(static candidate => candidate.MatchCount)
            .ThenBy(static candidate => candidate.PackId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryResolveStrongDeferredActivationPackCandidate(
        IReadOnlyList<DeferredChatPackActivationCandidate> orderedCandidates,
        [NotNullWhen(true)] out string? packId) {
        packId = null;
        if (orderedCandidates.Count == 0) {
            return false;
        }

        var topCandidate = orderedCandidates[0];
        if (topCandidate.MaxScore < DeferredChatPackActivationMinimumStrongScore
            && topCandidate.ExplicitMatchCount == 0
            && topCandidate.DirectNameMatchCount == 0) {
            return false;
        }

        if (orderedCandidates.Count > 1) {
            var secondCandidate = orderedCandidates[1];
            var ambiguousExplicitMatch = topCandidate.ExplicitMatchCount > 0
                                         && secondCandidate.ExplicitMatchCount > 0
                                         && secondCandidate.MaxScore >= topCandidate.MaxScore;
            var ambiguousDirectMatch = topCandidate.DirectNameMatchCount > 0
                                       && secondCandidate.DirectNameMatchCount > 0
                                       && secondCandidate.MaxScore >= topCandidate.MaxScore;
            var ambiguousHeuristicMatch = topCandidate.ExplicitMatchCount == 0
                                          && topCandidate.DirectNameMatchCount == 0
                                          && secondCandidate.MaxScore >= topCandidate.MaxScore - 1
                                          && secondCandidate.AggregateScore >= topCandidate.AggregateScore - 1;
            if (ambiguousExplicitMatch || ambiguousDirectMatch || ambiguousHeuristicMatch) {
                return false;
            }
        }

        packId = topCandidate.PackId;
        return true;
    }

    private bool TryActivatePackOnDemand(
        string packId,
        [NotNullWhen(true)] out string[]? activationWarnings) {
        activationWarnings = null;

        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        lock (_startupToolingBootstrapLock) {
            if (_packs.Any(pack => string.Equals(
                    ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id),
                    normalizedPackId,
                    StringComparison.OrdinalIgnoreCase))) {
                return false;
            }

            var activationWarningsBuffer = new List<string>();
            var runtimePolicyOptions = BuildRuntimePolicyOptions(_options);
            var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
                runtimePolicyOptions,
                warning => RecordBootstrapWarning(activationWarningsBuffer, warning));
            var bootstrapOptions = ToolPackBootstrap.CreateRuntimeBootstrapOptions(
                _options,
                runtimePolicyContext,
                warning => RecordBootstrapWarning(activationWarningsBuffer, warning));
            var activationResult = ToolPackBootstrap.ActivatePackOnDemand(bootstrapOptions, normalizedPackId, _packs);
            if (activationResult.Packs.Count == 0) {
                activationWarnings = NormalizeDistinctStrings(activationWarningsBuffer, maxItems: 32);
                return false;
            }

            var combinedPacks = _packs
                .Concat(activationResult.Packs)
                .GroupBy(static pack => ToolPackBootstrap.NormalizePackId(pack.Descriptor.Id), StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last())
                .ToArray();
            var registry = new ToolRegistry {
                RequireExplicitRoutingMetadata = runtimePolicyContext.Options.RequireExplicitRoutingMetadata
            };
            ToolPackBootstrap.RegisterAll(
                registry,
                combinedPacks,
                toolPackIdsByToolName: null,
                warning => RecordBootstrapWarning(activationWarningsBuffer, warning));

            var definitions = registry.GetDefinitions();
            var activeToolDefinitions = BuildToolDefinitionDtosFromRegistryDefinitions(definitions);
            var mergedToolDefinitions = MergeToolDefinitionDtos(
                Volatile.Read(ref _cachedToolDefinitions),
                activeToolDefinitions);
            var activeOrchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, combinedPacks);
            var runtimePolicyDiagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, runtimePolicyContext);
            var routingCatalogDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
            var mergedPackAvailability = MergePackAvailability(_packAvailability, activationResult.PackAvailability);
            var mergedPluginAvailability = MergePluginAvailability(_pluginAvailability, activationResult.PluginAvailability);
            var mergedPluginCatalog = MergePluginCatalog(_pluginCatalog, activationResult.PluginCatalog);
            var startupWarnings = NormalizeDistinctStrings(
                _startupWarnings
                    .Concat(activationWarningsBuffer)
                    .Append(BuildOnDemandPackActivationWarning(
                        normalizedPackId,
                        activationResult.Packs.Count,
                        definitions.Count)),
                maxItems: 64);
            var pluginSearchPaths = _pluginSearchPaths.Length > 0
                ? _pluginSearchPaths
                : NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);

            ApplyLiveToolingBootstrapState(
                registry,
                mergedToolDefinitions,
                combinedPacks,
                mergedPackAvailability,
                mergedPluginAvailability,
                mergedPluginCatalog,
                pluginSearchPaths,
                startupWarnings,
                _startupBootstrap ?? new SessionStartupBootstrapTelemetryDto(),
                runtimePolicyDiagnostics,
                routingCatalogDiagnostics,
                activeOrchestrationCatalog);
            ClearToolRoutingCaches(preserveConversationState: true);
            activationWarnings = NormalizeDistinctStrings(activationWarningsBuffer, maxItems: 32);
            return true;
        }
    }

    private static ToolDefinitionDto[] MergeToolDefinitionDtos(
        IReadOnlyList<ToolDefinitionDto>? existing,
        IReadOnlyList<ToolDefinitionDto>? updates) {
        var merged = new Dictionary<string, ToolDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in existing ?? Array.Empty<ToolDefinitionDto>()) {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            merged[definition.Name.Trim()] = definition;
        }

        foreach (var definition in updates ?? Array.Empty<ToolDefinitionDto>()) {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            merged[definition.Name.Trim()] = definition;
        }

        return merged.Values
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ToolPackAvailabilityInfo[] MergePackAvailability(
        IReadOnlyList<ToolPackAvailabilityInfo>? existing,
        IReadOnlyList<ToolPackAvailabilityInfo>? updates) {
        var merged = new Dictionary<string, ToolPackAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var availability in existing ?? Array.Empty<ToolPackAvailabilityInfo>()) {
            if (availability is not ToolPackAvailabilityInfo resolvedAvailability) {
                continue;
            }

            var normalizedPackId = ToolPackBootstrap.NormalizePackId(resolvedAvailability.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            merged[normalizedPackId] = resolvedAvailability;
        }

        foreach (var availability in updates ?? Array.Empty<ToolPackAvailabilityInfo>()) {
            if (availability is not ToolPackAvailabilityInfo resolvedAvailability) {
                continue;
            }

            var normalizedPackId = ToolPackBootstrap.NormalizePackId(resolvedAvailability.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            merged[normalizedPackId] = resolvedAvailability;
        }

        return merged.Values
            .OrderBy(static availability => availability.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static availability => availability.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ToolPluginAvailabilityInfo[] MergePluginAvailability(
        IReadOnlyList<ToolPluginAvailabilityInfo>? existing,
        IReadOnlyList<ToolPluginAvailabilityInfo>? updates) {
        var merged = new Dictionary<string, ToolPluginAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var availability in existing ?? Array.Empty<ToolPluginAvailabilityInfo>()) {
            if (availability is not ToolPluginAvailabilityInfo resolvedAvailability) {
                continue;
            }

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId(resolvedAvailability.Id);
            if (normalizedPluginId.Length == 0) {
                continue;
            }

            merged[normalizedPluginId] = resolvedAvailability;
        }

        foreach (var availability in updates ?? Array.Empty<ToolPluginAvailabilityInfo>()) {
            if (availability is not ToolPluginAvailabilityInfo resolvedAvailability) {
                continue;
            }

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId(resolvedAvailability.Id);
            if (normalizedPluginId.Length == 0) {
                continue;
            }

            merged[normalizedPluginId] = resolvedAvailability;
        }

        return merged.Values
            .OrderBy(static availability => availability.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static availability => availability.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ToolPluginCatalogInfo[] MergePluginCatalog(
        IReadOnlyList<ToolPluginCatalogInfo>? existing,
        IReadOnlyList<ToolPluginCatalogInfo>? updates) {
        var merged = new Dictionary<string, ToolPluginCatalogInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in existing ?? Array.Empty<ToolPluginCatalogInfo>()) {
            if (catalog is not ToolPluginCatalogInfo resolvedCatalog) {
                continue;
            }

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId(resolvedCatalog.Id);
            if (normalizedPluginId.Length == 0) {
                continue;
            }

            merged[normalizedPluginId] = resolvedCatalog;
        }

        foreach (var catalog in updates ?? Array.Empty<ToolPluginCatalogInfo>()) {
            if (catalog is not ToolPluginCatalogInfo resolvedCatalog) {
                continue;
            }

            var normalizedPluginId = ToolPackBootstrap.NormalizePackId(resolvedCatalog.Id);
            if (normalizedPluginId.Length == 0) {
                continue;
            }

            merged[normalizedPluginId] = resolvedCatalog;
        }

        return merged.Values
            .OrderBy(static catalog => catalog.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static catalog => catalog.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildOnDemandPackActivationWarning(string packId, int packsLoaded, int totalTools) {
        return $"[startup] on_demand_pack_activation pack='{packId}' packs_loaded='{Math.Max(0, packsLoaded)}' tools='{Math.Max(0, totalTools)}'";
    }
}
