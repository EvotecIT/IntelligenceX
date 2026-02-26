using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private sealed partial class ReplSession {
        private void RememberRecentHostTargets(IReadOnlyList<ToolCall> calls) {
            if (calls.Count == 0) {
                return;
            }

            var candidateKeys = GetScenarioInputKeyAliases("machine_name");
            if (candidateKeys.Count == 0) {
                return;
            }

            for (var i = 0; i < calls.Count; i++) {
                var args = calls[i].Arguments;
                if (args is null) {
                    continue;
                }

                for (var keyIndex = 0; keyIndex < candidateKeys.Count; keyIndex++) {
                    var candidateKey = candidateKeys[keyIndex];
                    if (!TryReadToolInputValuesByKey(args, candidateKey, out var values) || values.Count == 0) {
                        continue;
                    }

                    for (var valueIndex = 0; valueIndex < values.Count; valueIndex++) {
                        var normalized = NormalizeHostTargetCandidate(values[valueIndex]);
                        if (normalized.Length == 0) {
                            continue;
                        }

                        _recentHostTargets.RemoveAll(existing =>
                            string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
                        _recentHostTargets.Add(normalized);
                        if (_recentHostTargets.Count <= MaxRecentHostTargets) {
                            continue;
                        }

                        _recentHostTargets.RemoveAt(0);
                    }
                }
            }
        }

        private string[] GetRecentHostTargetsSnapshot() {
            if (_recentHostTargets.Count == 0) {
                return Array.Empty<string>();
            }

            var start = Math.Max(0, _recentHostTargets.Count - MaxRetryPromptHostTargets);
            return _recentHostTargets
                .Skip(start)
                .Take(MaxRetryPromptHostTargets)
                .ToArray();
        }

        private static string NormalizeHostTargetCandidate(string value) {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Length < 2 || candidate.Length > 128) {
                return string.Empty;
            }

            if (candidate.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
                return string.Empty;
            }

            return candidate;
        }

        private static List<string> OrderHostTargetCandidatesBySpecificity(IReadOnlyList<string> candidates) {
            if (candidates.Count <= 1) {
                return candidates as List<string> ?? candidates.ToList();
            }

            return candidates
                .Select(static (value, index) => new {
                    Value = value,
                    Index = index,
                    Score = ComputeHostTargetSpecificity(value)
                })
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Index)
                .Select(static candidate => candidate.Value)
                .ToList();
        }

        private static int ComputeHostTargetSpecificity(string value) {
            var candidate = NormalizeHostTargetCandidate(value);
            if (candidate.Length == 0) {
                return int.MinValue;
            }

            var score = candidate.Contains('.')
                ? HostTargetSpecificityFqdnBonus
                : HostTargetSpecificityShortNameBonus;
            if (Uri.CheckHostName(candidate) is UriHostNameType.IPv4 or UriHostNameType.IPv6) {
                score -= HostTargetSpecificityIpLiteralPenalty;
            }

            if (candidate.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) {
                score -= HostTargetSpecificityLocalhostPenalty;
            }

            return score;
        }

    }
}
