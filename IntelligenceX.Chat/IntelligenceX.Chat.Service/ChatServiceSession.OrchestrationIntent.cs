using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal readonly record struct RequestedArtifactIntent(
        bool HasExplicitRequest,
        bool WantsTable,
        bool WantsVisual,
        string PreferredVisualType,
        string Reason) {
        internal static RequestedArtifactIntent None(string reason) =>
            new(HasExplicitRequest: false, WantsTable: false, WantsVisual: false, PreferredVisualType: string.Empty, Reason: reason);

        internal bool RequiresArtifact => HasExplicitRequest && (WantsTable || WantsVisual);
    }

    internal readonly record struct ContinuationIntent(
        bool ContinuationFollowUpTurn,
        bool CompactFollowUpTurn,
        bool HasPendingActionContext);

    internal readonly record struct RuntimeBootstrapState(
        bool StartupBootstrapCompleted,
        bool StartupBootstrapCompletedSuccessfully,
        bool HasCachedToolCatalog,
        bool ServingPersistedPreview) {
        internal bool RuntimeReady => StartupBootstrapCompletedSuccessfully && !ServingPersistedPreview;
    }

    internal readonly record struct TurnExecutionIntent(
        ContinuationIntent Continuation,
        RequestedArtifactIntent RequestedArtifact,
        RuntimeBootstrapState RuntimeBootstrap,
        bool HasToolActivity);

    private static TurnExecutionIntent ResolveTurnExecutionIntent(
        string userRequest,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool hasPendingActionContext,
        bool hasToolActivity,
        bool startupBootstrapCompleted,
        bool startupBootstrapCompletedSuccessfully,
        bool hasCachedToolCatalog,
        bool servingPersistedPreview) {
        return new TurnExecutionIntent(
            Continuation: new ContinuationIntent(
                ContinuationFollowUpTurn: continuationFollowUpTurn,
                CompactFollowUpTurn: compactFollowUpTurn,
                HasPendingActionContext: hasPendingActionContext),
            RequestedArtifact: ResolveRequestedArtifactIntent(userRequest),
            RuntimeBootstrap: new RuntimeBootstrapState(
                StartupBootstrapCompleted: startupBootstrapCompleted,
                StartupBootstrapCompletedSuccessfully: startupBootstrapCompletedSuccessfully,
                HasCachedToolCatalog: hasCachedToolCatalog,
                ServingPersistedPreview: servingPersistedPreview),
            HasToolActivity: hasToolActivity);
    }

    private static RequestedArtifactIntent ResolveRequestedArtifactIntent(string? userRequest) {
        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return RequestedArtifactIntent.None("empty_request");
        }

        var wantsTable = ContainsMarkdownTableContractSignal(request.AsSpan())
                         || ContainsNaturalLanguageArtifactToken(request, NaturalLanguageTableArtifactTokens);
        var wantsVisual = TryResolvePreferredVisualTypeFromVisualRequestSignal(request, out var preferredVisualType);
        if (!wantsVisual && wantsTable) {
            preferredVisualType = TableVisualType;
        }

        if (!wantsTable && !wantsVisual) {
            return RequestedArtifactIntent.None("no_requested_artifact_signal");
        }

        return new RequestedArtifactIntent(
            HasExplicitRequest: true,
            WantsTable: wantsTable,
            WantsVisual: wantsVisual && !string.Equals(preferredVisualType, TableVisualType, StringComparison.OrdinalIgnoreCase),
            PreferredVisualType: preferredVisualType,
            Reason: wantsVisual ? "visual_request_signal" : "table_request_signal");
    }

    private static bool IsRequestedArtifactSatisfied(RequestedArtifactIntent intent, string? assistantDraft) {
        if (!intent.RequiresArtifact) {
            return true;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0) {
            return false;
        }

        if (intent.WantsTable && !AssistantDraftContainsMarkdownTableArtifact(draft)) {
            return false;
        }

        if (!intent.WantsVisual) {
            return true;
        }

        if (!TryResolvePreferredVisualTypeFromVisualContractSignal(draft, out var draftVisualType)) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(intent.PreferredVisualType)
            || string.Equals(intent.PreferredVisualType, AutoVisualType, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(intent.PreferredVisualType, MermaidVisualType, StringComparison.OrdinalIgnoreCase)) {
            return string.Equals(draftVisualType, MermaidVisualType, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(draftVisualType, NetworkVisualType, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(intent.PreferredVisualType, draftVisualType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AssistantDraftContainsMarkdownTableArtifact(string draft) {
        if (string.IsNullOrWhiteSpace(draft)) {
            return false;
        }

        if (ContainsMarkdownTableContractSignal(draft.AsSpan())) {
            return true;
        }

        return draft.Contains("| ---", StringComparison.Ordinal)
               || draft.Contains("|---", StringComparison.Ordinal);
    }

    private static bool TryResolvePreferredVisualTypeFromVisualRequestSignal(
        string? text,
        out string preferredVisualType) {
        if (TryResolvePreferredVisualTypeFromVisualContractSignal(text, out preferredVisualType)) {
            return true;
        }

        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            preferredVisualType = string.Empty;
            return false;
        }

        var matchedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ContainsNaturalLanguageArtifactToken(value, NaturalLanguageDiagramArtifactTokens)) {
            matchedTypes.Add(MermaidVisualType);
        }

        if (ContainsNaturalLanguageArtifactToken(value, NaturalLanguageNetworkArtifactTokens)) {
            matchedTypes.Add(NetworkVisualType);
        }

        if (ContainsNaturalLanguageArtifactToken(value, NaturalLanguageChartArtifactTokens)) {
            matchedTypes.Add(ChartVisualType);
        }

        if (ContainsNaturalLanguageArtifactToken(value, NaturalLanguageTableArtifactTokens)) {
            matchedTypes.Add(TableVisualType);
        }

        if (matchedTypes.Count == 0) {
            preferredVisualType = string.Empty;
            return false;
        }

        if (matchedTypes.Contains(NetworkVisualType)) {
            preferredVisualType = NetworkVisualType;
            return true;
        }

        if (matchedTypes.Contains(MermaidVisualType)) {
            preferredVisualType = MermaidVisualType;
            return true;
        }

        if (matchedTypes.Contains(ChartVisualType)) {
            preferredVisualType = ChartVisualType;
            return true;
        }

        preferredVisualType = TableVisualType;
        return true;
    }

    private static bool ContainsNaturalLanguageArtifactToken(string text, IReadOnlyCollection<string> tokens) {
        if (string.IsNullOrWhiteSpace(text) || tokens.Count == 0) {
            return false;
        }

        var normalized = (text ?? string.Empty).Trim();
        var tokenStart = -1;
        for (var i = 0; i <= normalized.Length; i++) {
            var inToken = false;
            if (i < normalized.Length) {
                var ch = normalized[i];
                inToken = char.IsLetterOrDigit(ch) || ch is '-' or '_';
            }

            if (inToken) {
                if (tokenStart < 0) {
                    tokenStart = i;
                }

                continue;
            }

            if (tokenStart < 0) {
                continue;
            }

            var candidate = normalized.AsSpan(tokenStart, i - tokenStart);
            tokenStart = -1;
            var compact = NormalizeCompactToken(candidate);
            if (compact.Length > 0 && tokens.Contains(compact)) {
                return true;
            }
        }

        return false;
    }
}
