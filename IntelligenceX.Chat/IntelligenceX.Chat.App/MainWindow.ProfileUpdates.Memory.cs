using System;
using System.Collections.Generic;
using System.Diagnostics;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private static IReadOnlyList<string> BuildLocalContextFallbackLines(ConversationRuntime conversation, string userText) {
        ArgumentNullException.ThrowIfNull(conversation);
        return DesktopChatTurnProtocol.BuildLocalContextFallbackLines(
            conversation.ThreadId,
            conversation.Messages,
            userText);
    }

    private IReadOnlyList<string> BuildPersistentMemoryContextLines(string userText) {
        if (!_persistentMemoryEnabled) {
            return Array.Empty<string>();
        }

        var selection = DesktopChatMemorySelector.Select(_appState.MemoryFacts, userText);
        _appState.MemoryFacts = selection.NormalizedFacts;
        RememberLastMemoryDebugSnapshot(selection);
        Trace.WriteLine(
            $"[memory-selection] available={selection.NormalizedFacts.Count} "
            + $"candidates={selection.CandidateFacts} selected={selection.Lines.Count} "
            + $"queryTokens={selection.UserTokenCount} topScore={selection.TopScore:0.###}");
        return selection.Lines;
    }

    private void RememberLastMemoryDebugSnapshot(DesktopChatMemorySelection selection) {
        var quality = ComputeMemoryDebugQuality(
            selection.AverageSelectedRelevance,
            selection.AverageSelectedSimilarity,
            selection.Lines.Count);
        MemoryDebugSnapshot snapshot;
        lock (_memoryDiagnosticsSync) {
            _memoryDebugSequence = unchecked(_memoryDebugSequence + 1);
            snapshot = new MemoryDebugSnapshot {
                UpdatedUtc = DateTime.UtcNow,
                Sequence = _memoryDebugSequence,
                AvailableFacts = selection.NormalizedFacts.Count,
                CandidateFacts = selection.CandidateFacts,
                SelectedFacts = selection.Lines.Count,
                UserTokenCount = selection.UserTokenCount,
                TopScore = selection.TopScore,
                TopSemanticSimilarity = selection.TopSimilarity,
                AverageSelectedSimilarity = selection.AverageSelectedSimilarity,
                AverageSelectedRelevance = selection.AverageSelectedRelevance,
                CacheEntries = 0,
                Quality = quality
            };
            _lastMemoryDebugSnapshot = snapshot;
            _memoryDebugHistory.Add(snapshot);
            if (_memoryDebugHistory.Count > 24) {
                _memoryDebugHistory.RemoveRange(0, _memoryDebugHistory.Count - 24);
            }
        }
    }

    private static string ComputeMemoryDebugQuality(
        double averageSelectedRelevance,
        double averageSelectedSimilarity,
        int selectedFacts) {
        if (selectedFacts <= 0) {
            return "none";
        }

        var relevance = double.IsFinite(averageSelectedRelevance)
            ? Math.Clamp(averageSelectedRelevance, 0d, 1d)
            : 0d;
        var similarity = double.IsFinite(averageSelectedSimilarity)
            ? Math.Clamp(averageSelectedSimilarity, 0d, 1d)
            : 0d;
        if (relevance >= 0.62d && similarity <= 0.78d) {
            return "good";
        }

        return relevance >= 0.46d ? "ok" : "low";
    }

    private static List<ChatMemoryFactState> NormalizeMemoryFacts(List<ChatMemoryFactState>? facts) {
        return DesktopChatMemorySelector.NormalizeFacts(facts);
    }
}
