using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Service.Persistence;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int ToolRoutingStatsStoreVersion = 1;
    private static readonly object ToolRoutingStatsStoreLock = new();

    private sealed class ToolRoutingStatsStoreDto {
        public int Version { get; set; } = ToolRoutingStatsStoreVersion;
        public Dictionary<string, ToolRoutingStatsStoreEntryDto> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ToolRoutingStatsStoreEntryDto {
        public int Invocations { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public long LastUsedUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
    }

    private static string ResolveDefaultToolRoutingStatsStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("tool-routing-stats.json");

    private string ResolveToolRoutingStatsStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "tool-routing-stats.json");

    private void TryRehydrateToolRoutingStats() {
        var path = ResolveToolRoutingStatsStorePath();
        var store = ReadToolRoutingStatsStoreNoThrow(path);
        if (store.Tools.Count == 0) {
            return;
        }

        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
            foreach (var pair in store.Tools) {
                var toolName = (pair.Key ?? string.Empty).Trim();
                var entry = pair.Value;
                if (toolName.Length == 0 || entry is null) {
                    continue;
                }

                _toolRoutingStats[toolName] = new ToolRoutingStats {
                    Invocations = Math.Max(0, entry.Invocations),
                    Successes = Math.Max(0, entry.Successes),
                    Failures = Math.Max(0, entry.Failures),
                    LastUsedUtcTicks = entry.LastUsedUtcTicks,
                    LastSuccessUtcTicks = entry.LastSuccessUtcTicks
                };
            }

            TrimToolRoutingStatsNoLock();
        }
    }

    private void PersistToolRoutingStatsSnapshot() {
        Dictionary<string, ToolRoutingStatsStoreEntryDto> snapshot;
        lock (_toolRoutingStatsLock) {
            if (_toolRoutingStats.Count == 0) {
                snapshot = new Dictionary<string, ToolRoutingStatsStoreEntryDto>(StringComparer.OrdinalIgnoreCase);
            } else {
                snapshot = _toolRoutingStats
                    .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                    .ToDictionary(
                        static pair => pair.Key.Trim(),
                        static pair => new ToolRoutingStatsStoreEntryDto {
                            Invocations = Math.Max(0, pair.Value.Invocations),
                            Successes = Math.Max(0, pair.Value.Successes),
                            Failures = Math.Max(0, pair.Value.Failures),
                            LastUsedUtcTicks = pair.Value.LastUsedUtcTicks,
                            LastSuccessUtcTicks = pair.Value.LastSuccessUtcTicks
                        },
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        var path = ResolveToolRoutingStatsStorePath();
        lock (ToolRoutingStatsStoreLock) {
            var store = new ToolRoutingStatsStoreDto {
                Tools = snapshot
            };
            PruneToolRoutingStatsStore(store);
            WriteToolRoutingStatsStoreNoThrow(path, store);
        }
    }

    private void ClearToolRoutingStatsSnapshots() {
        var path = ResolveToolRoutingStatsStorePath();
        lock (ToolRoutingStatsStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Tool routing stats store");
        }
    }

    private static ToolRoutingStatsStoreDto ReadToolRoutingStatsStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<ToolRoutingStatsStoreDto>(json),
            static store => store.Version == ToolRoutingStatsStoreVersion && store.Tools is not null,
            static store => {
                if (store.Tools.Comparer != StringComparer.OrdinalIgnoreCase) {
                    store.Tools = new Dictionary<string, ToolRoutingStatsStoreEntryDto>(store.Tools, StringComparer.OrdinalIgnoreCase);
                }
            },
            static () => new ToolRoutingStatsStoreDto(),
            "Tool routing stats store");
    }

    private static void WriteToolRoutingStatsStoreNoThrow(string path, ToolRoutingStatsStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Tool routing stats store");
    }

    private static void PruneToolRoutingStatsStore(ToolRoutingStatsStoreDto store) {
        if (store.Tools.Count <= MaxTrackedToolRoutingStats) {
            return;
        }

        var removeCount = store.Tools.Count - MaxTrackedToolRoutingStats;
        if (removeCount <= 0) {
            return;
        }

        var toRemove = store.Tools
            .Select(pair => {
                var entry = pair.Value;
                var ticks = entry?.LastUsedUtcTicks ?? 0;
                if (ticks <= 0) {
                    ticks = entry?.LastSuccessUtcTicks ?? 0;
                }
                return (ToolName: pair.Key, Ticks: ticks);
            })
            .OrderBy(static item => item.Ticks)
            .ThenBy(static item => item.ToolName, StringComparer.OrdinalIgnoreCase)
            .Take(removeCount)
            .Select(static item => item.ToolName)
            .ToArray();
        foreach (var toolName in toRemove) {
            store.Tools.Remove(toolName);
        }
    }
}
