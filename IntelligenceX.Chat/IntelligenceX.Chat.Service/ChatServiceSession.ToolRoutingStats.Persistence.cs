using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int ToolRoutingStatsStoreVersion = 1;
    private static readonly object ToolRoutingStatsStoreLock = new();
    private static readonly JsonSerializerOptions ToolRoutingStatsStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

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

    private static string ResolveDefaultToolRoutingStatsStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "tool-routing-stats.json");
    }

    private string ResolveToolRoutingStatsStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "tool-routing-stats.json");
        }

        return ResolveDefaultToolRoutingStatsStorePath();
    }

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
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Tool routing stats store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static ToolRoutingStatsStoreDto ReadToolRoutingStatsStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new ToolRoutingStatsStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new ToolRoutingStatsStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new ToolRoutingStatsStoreDto();
            }

            var store = JsonSerializer.Deserialize<ToolRoutingStatsStoreDto>(json, ToolRoutingStatsStoreJsonOptions);
            if (store is null || store.Version != ToolRoutingStatsStoreVersion || store.Tools is null) {
                return new ToolRoutingStatsStoreDto();
            }

            if (store.Tools.Comparer != StringComparer.OrdinalIgnoreCase) {
                store.Tools = new Dictionary<string, ToolRoutingStatsStoreEntryDto>(store.Tools, StringComparer.OrdinalIgnoreCase);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Tool routing stats store read failed: {ex.GetType().Name}: {ex.Message}");
            return new ToolRoutingStatsStoreDto();
        }
    }

    private static void WriteToolRoutingStatsStoreNoThrow(string path, ToolRoutingStatsStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, ToolRoutingStatsStoreJsonOptions);
            var fileName = Path.GetFileName(path);
            var tmpName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            tmp = string.IsNullOrWhiteSpace(directory) ? tmpName : Path.Combine(directory!, tmpName);

            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                TryHardenPendingActionsStoreAclNoThrow(tmp);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path)) {
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            } else {
                File.Move(tmp, path);
            }

            TryHardenPendingActionsStoreAclNoThrow(path);
        } catch (Exception ex) {
            Trace.TraceWarning($"Tool routing stats store write failed: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp)) {
                try {
                    File.Delete(tmp);
                } catch {
                    // Best effort only.
                }
            }
        }
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
