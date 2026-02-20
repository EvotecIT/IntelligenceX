using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogEntityHandoff {
    private const int DefaultMaxCandidates = 50;
    private const int MaxCandidatesCap = 200;

    private sealed class CandidateAccumulator {
        public int Count { get; set; }
        public HashSet<string> SourceFields { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record EntityCandidateModel(
        string Value,
        int Count,
        IReadOnlyList<string> SourceFields);

    private sealed record TargetHintModel(
        string Tool,
        string Argument,
        IReadOnlyList<string> Values,
        string Note);

    private sealed record EntityHandoffModel(
        string Contract,
        int Version,
        int ScannedRows,
        int IdentityCandidatesTotal,
        int ComputerCandidatesTotal,
        IReadOnlyList<EntityCandidateModel> IdentityCandidates,
        IReadOnlyList<EntityCandidateModel> ComputerCandidates,
        IReadOnlyList<TargetHintModel> TargetHints);

    internal static JsonObject BuildFromRows<T>(
        IEnumerable<T> rows,
        Func<T, string?> whoSelector,
        Func<T, string?> objectAffectedSelector,
        Func<T, string?> computerSelector,
        int maxCandidates = DefaultMaxCandidates) {
        if (rows is null) {
            throw new ArgumentNullException(nameof(rows));
        }
        if (whoSelector is null) {
            throw new ArgumentNullException(nameof(whoSelector));
        }
        if (objectAffectedSelector is null) {
            throw new ArgumentNullException(nameof(objectAffectedSelector));
        }
        if (computerSelector is null) {
            throw new ArgumentNullException(nameof(computerSelector));
        }

        var boundedMaxCandidates = Math.Clamp(maxCandidates, 1, MaxCandidatesCap);
        var identityCandidates = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        var computerCandidates = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        var scannedRows = 0;

        foreach (var row in rows) {
            scannedRows++;

            AddCandidate(identityCandidates, whoSelector(row), "who");
            AddCandidate(identityCandidates, objectAffectedSelector(row), "object_affected");

            var computer = NormalizeCandidate(computerSelector(row));
            if (computer is not null) {
                AddCandidate(identityCandidates, computer, "computer");
                AddCandidate(computerCandidates, computer, "computer");
            }
        }

        var identityTop = ToCandidateModels(identityCandidates, boundedMaxCandidates);
        var computerTop = ToCandidateModels(computerCandidates, boundedMaxCandidates);
        var topIdentityValues = identityTop
            .Select(static x => x.Value)
            .ToArray();

        var model = new EntityHandoffModel(
            Contract: "eventlog_entity_handoff",
            Version: 1,
            ScannedRows: scannedRows,
            IdentityCandidatesTotal: identityCandidates.Count,
            ComputerCandidatesTotal: computerCandidates.Count,
            IdentityCandidates: identityTop,
            ComputerCandidates: computerTop,
            TargetHints: new[] {
                new TargetHintModel(
                    Tool: "ad_object_resolve",
                    Argument: "identities",
                    Values: topIdentityValues,
                    Note: "Pass deduplicated identity candidates as bulk identities input for AD resolution."),
                new TargetHintModel(
                    Tool: "ad_search",
                    Argument: "identity",
                    Values: topIdentityValues,
                    Note: "Use the first high-confidence candidate as identity, then iterate remaining values as needed.")
            });

        return ToolJson.ToJsonObjectSnakeCase(model);
    }

    private static void AddCandidate(
        Dictionary<string, CandidateAccumulator> store,
        string? rawValue,
        string sourceField) {
        var normalized = NormalizeCandidate(rawValue);
        if (normalized is null) {
            return;
        }

        if (!store.TryGetValue(normalized, out var accumulator)) {
            accumulator = new CandidateAccumulator();
            store[normalized] = accumulator;
        }

        accumulator.Count++;
        accumulator.SourceFields.Add(sourceField);
    }

    private static IReadOnlyList<EntityCandidateModel> ToCandidateModels(
        Dictionary<string, CandidateAccumulator> store,
        int maxCandidates) {
        return store
            .Select(static kvp => new EntityCandidateModel(
                Value: kvp.Key,
                Count: kvp.Value.Count,
                SourceFields: kvp.Value.SourceFields
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderByDescending(static row => row.Count)
            .ThenBy(static row => row.Value, StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates)
            .ToArray();
    }

    private static string? NormalizeCandidate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 1 && normalized[0] == '-') {
            return null;
        }

        return normalized;
    }
}

