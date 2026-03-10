using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXStoreCatalogHelper {
    internal sealed record StoredRunInfo(
        string RunId,
        DateTimeOffset StartedUtc,
        DateTimeOffset? EndedUtc,
        double? DurationSeconds,
        string Policy,
        double? TtlDays,
        bool? AcceptStale,
        string MatchMode,
        string RawMode,
        int? PlannedTasks,
        int? CompletedTasks,
        int? EligibleForestFamilies,
        int? EligibleDomainFamilies,
        int? EligibleDcFamilies,
        int StoredResultCount,
        string ToolVersion);

    internal sealed record StoredRunRow(
        string ScopeGroup,
        string ScopeId,
        string Domain,
        string DomainController,
        string RuleName,
        string OverallStatus,
        DateTimeOffset CompletedUtc,
        int TestsSecurityCount,
        int TestsHealthCount,
        int PenaltySecurity,
        int PenaltyHealth,
        int PenaltyTotal);

    internal sealed record StoredRunData(
        StoredRunInfo Run,
        IReadOnlyList<StoredRunRow> Rows);

    internal readonly record struct StoredRunScores(
        int SecurityScore,
        int HealthScore,
        int OverallScore,
        int SecurityPenaltyTotal,
        int HealthPenaltyTotal,
        int TotalPenalty);

    internal static bool TryResolveStoreDirectory(
        TestimoXToolOptions options,
        string? inputPath,
        string toolName,
        out string fullPath,
        out string errorResponse) {
        fullPath = string.Empty;
        errorResponse = string.Empty;

        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.AllowedStoreRoots.Count == 0) {
            errorResponse = ToolResultV2.Error(
                errorCode: "access_denied",
                error: "TestimoX store inspection is disabled (AllowedStoreRoots is empty).",
                hints: new[] {
                    $"Configure AllowedStoreRoots before calling {toolName}.",
                    "Provide a store_directory inside an allowed root."
                },
                isTransient: false);
            return false;
        }

        if (!ToolPaths.TryResolveAllowedExistingDirectory(
                inputPath ?? string.Empty,
                options.AllowedStoreRoots,
                out fullPath,
                out var errorCode,
                out var error,
                out var hints)) {
            errorResponse = ToolResultV2.Error(
                errorCode: errorCode,
                error: error,
                hints: hints,
                isTransient: false);
            return false;
        }

        return true;
    }

    internal static IReadOnlyList<StoredRunInfo> ListRuns(string storeDirectory) {
        var rows = new List<StoredRunInfo>();
        var runsDirectory = Path.Combine(storeDirectory, "runs");
        if (!Directory.Exists(runsDirectory)) {
            return rows;
        }

        foreach (var runDirectory in Directory.GetDirectories(runsDirectory)) {
            var runId = Path.GetFileName(runDirectory);
            if (string.IsNullOrWhiteSpace(runId)) {
                continue;
            }

            var runJsonPath = Path.Combine(runDirectory, "run.json");
            if (!File.Exists(runJsonPath)) {
                continue;
            }

            try {
                using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath, new UTF8Encoding(false)));
                var root = document.RootElement;
                var startedUtc = TryReadDateTimeOffset(root, "Started") ?? DateTimeOffset.MinValue;
                var endedUtc = TryReadDateTimeOffset(root, "Ended");
                var durationSeconds = endedUtc.HasValue && startedUtc != DateTimeOffset.MinValue
                    ? Math.Max(0, (endedUtc.Value - startedUtc).TotalSeconds)
                    : (double?)null;
                var indexPath = Path.Combine(runDirectory, "index.jsonl");
                rows.Add(new StoredRunInfo(
                    RunId: runId,
                    StartedUtc: startedUtc,
                    EndedUtc: endedUtc,
                    DurationSeconds: durationSeconds,
                    Policy: TryReadString(root, "Policy"),
                    TtlDays: TryReadDouble(root, "TtlDays"),
                    AcceptStale: TryReadBoolean(root, "AcceptStale"),
                    MatchMode: TryReadString(root, "Match"),
                    RawMode: TryReadString(root, "Raw"),
                    PlannedTasks: TryReadInt32(root, "PlannedTasks"),
                    CompletedTasks: TryReadInt32(root, "CompletedTasks"),
                    EligibleForestFamilies: TryReadInt32(root, "EligibleForestFamilies"),
                    EligibleDomainFamilies: TryReadInt32(root, "EligibleDomainFamilies"),
                    EligibleDcFamilies: TryReadInt32(root, "EligibleDcFamilies"),
                    StoredResultCount: CountIndexEntries(indexPath),
                    ToolVersion: TryReadString(root, "ToolVersion")));
            } catch {
                // Ignore unreadable run metadata and continue enumerating the catalog.
            }
        }

        return rows;
    }

    internal static bool TryLoadRun(
        string storeDirectory,
        string runId,
        out StoredRunData? runData,
        out string? errorResponse) {
        runData = null;
        errorResponse = null;

        if (string.IsNullOrWhiteSpace(runId)) {
            errorResponse = ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: "run_id is required.",
                isTransient: false);
            return false;
        }

        var run = ListRuns(storeDirectory)
            .FirstOrDefault(candidate => string.Equals(candidate.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (run is null) {
            errorResponse = ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Stored TestimoX run '{runId}' was not found in the requested store_directory.",
                hints: new[] { "Call testimox_runs_list first to inspect available run_id values." },
                isTransient: false);
            return false;
        }

        var rows = LoadRunRows(storeDirectory, run.RunId);
        runData = new StoredRunData(run, rows);
        return true;
    }

    internal static StoredRunScores ComputeScores(IEnumerable<StoredRunRow> rows) {
        var materialized = rows?.ToArray() ?? Array.Empty<StoredRunRow>();
        var securityPenaltyTotal = materialized.Sum(static row => row.PenaltySecurity);
        var healthPenaltyTotal = materialized.Sum(static row => row.PenaltyHealth);
        var totalPenalty = materialized.Sum(static row => row.PenaltyTotal);
        var securityTests = Math.Max(1, materialized.Sum(static row => row.TestsSecurityCount));
        var healthTests = Math.Max(1, materialized.Sum(static row => row.TestsHealthCount));
        var securityBudget = 100.0 + (0.25 * securityTests);
        var healthBudget = 100.0 + (0.25 * healthTests);
        var securityScore = (int)Math.Max(0, Math.Round(100.0 - ((securityPenaltyTotal / securityBudget) * 100.0)));
        var healthScore = (int)Math.Max(0, Math.Round(100.0 - ((healthPenaltyTotal / healthBudget) * 100.0)));
        return new StoredRunScores(
            SecurityScore: securityScore,
            HealthScore: healthScore,
            OverallScore: Math.Min(securityScore, healthScore),
            SecurityPenaltyTotal: securityPenaltyTotal,
            HealthPenaltyTotal: healthPenaltyTotal,
            TotalPenalty: totalPenalty);
    }

    private static IReadOnlyList<StoredRunRow> LoadRunRows(string storeDirectory, string runId) {
        var rows = new List<StoredRunRow>();
        var indexPath = Path.Combine(storeDirectory, "runs", runId, "index.jsonl");
        if (!File.Exists(indexPath)) {
            return rows;
        }

        foreach (var line in File.ReadLines(indexPath, new UTF8Encoding(false))) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            try {
                using var document = JsonDocument.Parse(line);
                var relativePath = TryReadString(document.RootElement, "path");
                if (string.IsNullOrWhiteSpace(relativePath)) {
                    continue;
                }

                var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(storeDirectory, normalizedRelativePath);
                var row = ReadEnvelope(fullPath);
                if (row is not null) {
                    rows.Add(row);
                }
            } catch {
                // Skip malformed index lines.
            }
        }

        return rows;
    }

    private static StoredRunRow? ReadEnvelope(string fullPath) {
        try {
            using var document = TryParseEnvelopeDocument(fullPath);
            if (document is null) {
                return null;
            }

            var root = document.RootElement;
            return new StoredRunRow(
                ScopeGroup: TryReadString(root, "ScopeGroup"),
                ScopeId: TryReadString(root, "ScopeId"),
                Domain: TryReadString(root, "Domain"),
                DomainController: TryReadString(root, "DomainController"),
                RuleName: TryReadString(root, "RuleName"),
                OverallStatus: TryReadString(root, "OverallStatus"),
                CompletedUtc: TryReadDateTimeOffset(root, "CompletedUtc") ?? DateTimeOffset.MinValue,
                TestsSecurityCount: TryReadInt32(root, "TestsSecurityCount") ?? 0,
                TestsHealthCount: TryReadInt32(root, "TestsHealthCount") ?? 0,
                PenaltySecurity: TryReadInt32(root, "PenaltySecurity") ?? 0,
                PenaltyHealth: TryReadInt32(root, "PenaltyHealth") ?? 0,
                PenaltyTotal: TryReadInt32(root, "PenaltyTotal") ?? 0);
        } catch {
            return null;
        }
    }

    private static JsonDocument? TryParseEnvelopeDocument(string fullPath) {
        if (!File.Exists(fullPath)) {
            return null;
        }

        try {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
#if NETFRAMEWORK
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);
            return JsonDocument.Parse(gzip);
#else
            try {
                using var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: false);
                return JsonDocument.Parse(brotli);
            } catch {
                stream.Position = 0;
                using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);
                return JsonDocument.Parse(gzip);
            }
#endif
        } catch {
            try {
                return JsonDocument.Parse(File.ReadAllText(fullPath, new UTF8Encoding(false)));
            } catch {
                return null;
            }
        }
    }

    private static int CountIndexEntries(string indexPath) {
        if (!File.Exists(indexPath)) {
            return 0;
        }

        try {
            return File.ReadLines(indexPath, new UTF8Encoding(false))
                .Count(static line => !string.IsNullOrWhiteSpace(line));
        } catch {
            return 0;
        }
    }

    private static string TryReadString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return string.Empty;
        }

        return node.ValueKind == JsonValueKind.String
            ? node.GetString()?.Trim() ?? string.Empty
            : node.ToString()?.Trim() ?? string.Empty;
    }

    private static int? TryReadInt32(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }

        try {
            return node.ValueKind switch {
                JsonValueKind.Number => node.GetInt32(),
                JsonValueKind.String when int.TryParse(node.GetString(), out var parsed) => parsed,
                _ => null
            };
        } catch {
            return null;
        }
    }

    private static double? TryReadDouble(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }

        try {
            return node.ValueKind switch {
                JsonValueKind.Number => node.GetDouble(),
                JsonValueKind.String when double.TryParse(node.GetString(), out var parsed) => parsed,
                _ => null
            };
        } catch {
            return null;
        }
    }

    private static bool? TryReadBoolean(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }

        return node.ValueKind switch {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(node.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }

        try {
            return node.ValueKind switch {
                JsonValueKind.String when DateTimeOffset.TryParse(node.GetString(), out var parsed) => parsed,
                _ => null
            };
        } catch {
            return null;
        }
    }
}
