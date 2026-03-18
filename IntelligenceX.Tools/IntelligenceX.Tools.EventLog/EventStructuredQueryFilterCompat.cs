using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EventViewerX;

internal sealed class EventStructuredQueryFilterInput {
    public IReadOnlyList<int>? EventIds { get; init; }
    public string? ProviderName { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
    public string? Level { get; init; }
    public string? Keywords { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<long>? RecordIds { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? NamedDataFilter { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? NamedDataExcludeFilter { get; init; }
}

internal sealed class EventStructuredQueryFilter {
    public IReadOnlyList<int>? EventIds { get; init; }
    public string? ProviderName { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
    public Level? Level { get; init; }
    public Keywords? Keywords { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<long>? RecordIds { get; init; }
    public Hashtable? NamedDataFilter { get; init; }
    public Hashtable? NamedDataExcludeFilter { get; init; }
}

internal static class EventStructuredQueryFilterService {
    internal const int MaxEventIds = 64;
    internal const int MaxRecordIds = 64;
    internal const int MaxNamedDataKeys = 16;
    internal const int MaxNamedDataValuesPerKey = 16;
    internal const int MaxNamedDataKeyLength = 128;
    internal const int MaxNamedDataValueLength = 256;

    internal static IReadOnlyList<string> LevelNames { get; } = new[] {
        "logalways",
        "critical",
        "error",
        "warning",
        "informational",
        "verbose"
    };

    internal static IReadOnlyList<string> KeywordNames { get; } = new[] {
        "none",
        "auditfailure",
        "auditsuccess",
        "correlationhint2",
        "eventlogclassic",
        "sqm",
        "wdidiagnostic",
        "wdicontext",
        "responsetime"
    };

    internal static bool TryNormalize(
        EventStructuredQueryFilterInput? input,
        out EventStructuredQueryFilter? filter,
        out string? error) {
        filter = null;
        error = null;

        if (input is null) {
            filter = new EventStructuredQueryFilter();
            return true;
        }

        var eventIds = NormalizePositiveInt32List(input.EventIds, MaxEventIds, "event_ids", out error);
        if (error is not null) {
            return false;
        }

        var recordIds = NormalizePositiveInt64List(input.RecordIds, MaxRecordIds, "event_record_ids", out error);
        if (error is not null) {
            return false;
        }

        if (!TryParseLevel(input.Level, out var level, out error)) {
            return false;
        }

        if (!TryParseKeywords(input.Keywords, out var keywords, out error)) {
            return false;
        }

        var namedDataFilter = ToHashtable(input.NamedDataFilter);
        var namedDataExcludeFilter = ToHashtable(input.NamedDataExcludeFilter);

        filter = new EventStructuredQueryFilter {
            EventIds = eventIds,
            ProviderName = NormalizeOptionalString(input.ProviderName),
            StartTimeUtc = input.StartTimeUtc,
            EndTimeUtc = input.EndTimeUtc,
            Level = level,
            Keywords = keywords,
            UserId = NormalizeOptionalString(input.UserId),
            RecordIds = recordIds,
            NamedDataFilter = namedDataFilter,
            NamedDataExcludeFilter = namedDataExcludeFilter
        };
        return true;
    }

    internal static bool HasAny(EventStructuredQueryFilter? filter) {
        return filter is not null
               && ((filter.EventIds?.Count ?? 0) > 0
                   || !string.IsNullOrWhiteSpace(filter.ProviderName)
                   || filter.StartTimeUtc.HasValue
                   || filter.EndTimeUtc.HasValue
                   || filter.Level.HasValue
                   || filter.Keywords.HasValue
                   || !string.IsNullOrWhiteSpace(filter.UserId)
                   || (filter.RecordIds?.Count ?? 0) > 0
                   || (filter.NamedDataFilter?.Count ?? 0) > 0
                   || (filter.NamedDataExcludeFilter?.Count ?? 0) > 0);
    }

    internal static string BuildXPath(EventStructuredQueryFilter? filter) {
        if (!HasAny(filter)) {
            return "*";
        }

        return SearchEvents.BuildWinEventFilter(
            id: filter?.EventIds?.Select(static value => value.ToString()).ToArray(),
            eventRecordId: filter?.RecordIds?.Select(static value => value.ToString()).ToArray(),
            startTime: filter?.StartTimeUtc,
            endTime: filter?.EndTimeUtc,
            providerName: string.IsNullOrWhiteSpace(filter?.ProviderName) ? null : new[] { filter!.ProviderName! },
            keywords: filter?.Keywords.HasValue == true ? new[] { (long)filter.Keywords.Value } : null,
            level: filter?.Level.HasValue == true ? new[] { filter.Level.Value.ToString() } : null,
            userId: string.IsNullOrWhiteSpace(filter?.UserId) ? null : new[] { filter!.UserId! },
            namedDataFilter: filter?.NamedDataFilter is null ? null : new[] { filter.NamedDataFilter },
            namedDataExcludeFilter: filter?.NamedDataExcludeFilter is null ? null : new[] { filter.NamedDataExcludeFilter },
            xpathOnly: true);
    }

    private static IReadOnlyList<int>? NormalizePositiveInt32List(
        IReadOnlyList<int>? values,
        int maxCount,
        string label,
        out string? error) {
        error = null;
        if (values is not { Count: > 0 }) {
            return values is { Count: 0 } ? Array.Empty<int>() : null;
        }

        if (values.Count > maxCount) {
            error = $"{label} supports at most {maxCount} values.";
            return null;
        }

        var normalized = new List<int>(values.Count);
        var seen = new HashSet<int>();
        for (var i = 0; i < values.Count; i++) {
            var value = values[i];
            if (value <= 0) {
                error = $"{label} values must be positive integers.";
                return null;
            }

            if (seen.Add(value)) {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    private static IReadOnlyList<long>? NormalizePositiveInt64List(
        IReadOnlyList<long>? values,
        int maxCount,
        string label,
        out string? error) {
        error = null;
        if (values is not { Count: > 0 }) {
            return values is { Count: 0 } ? Array.Empty<long>() : null;
        }

        if (values.Count > maxCount) {
            error = $"{label} supports at most {maxCount} values.";
            return null;
        }

        var normalized = new List<long>(values.Count);
        var seen = new HashSet<long>();
        for (var i = 0; i < values.Count; i++) {
            var value = values[i];
            if (value <= 0) {
                error = $"{label} values must be positive integers.";
                return null;
            }

            if (seen.Add(value)) {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    private static string? NormalizeOptionalString(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool TryParseLevel(string? raw, out Level? level, out string? error) {
        level = null;
        error = null;

        var normalized = NormalizeToken(raw);
        if (normalized.Length == 0) {
            return true;
        }

        if (int.TryParse(normalized, out var numericLevel)) {
            level = numericLevel switch {
                0 => EventViewerX.Level.LogAlways,
                1 => EventViewerX.Level.Critical,
                2 => EventViewerX.Level.Error,
                3 => EventViewerX.Level.Warning,
                4 => EventViewerX.Level.Informational,
                5 => EventViewerX.Level.Verbose,
                _ => null
            };

            if (level.HasValue) {
                return true;
            }
        }

        if (normalized == "information") {
            normalized = "informational";
        }

        level = normalized switch {
            "logalways" => EventViewerX.Level.LogAlways,
            "critical" => EventViewerX.Level.Critical,
            "error" => EventViewerX.Level.Error,
            "warning" => EventViewerX.Level.Warning,
            "informational" => EventViewerX.Level.Informational,
            "verbose" => EventViewerX.Level.Verbose,
            _ => null
        };

        if (level.HasValue) {
            return true;
        }

        error = $"level must be one of: {string.Join(", ", LevelNames)}.";
        return false;
    }

    private static bool TryParseKeywords(string? raw, out Keywords? keywords, out string? error) {
        keywords = null;
        error = null;

        var normalized = NormalizeToken(raw);
        if (normalized.Length == 0) {
            return true;
        }

        if (long.TryParse(normalized, out var numericKeyword)) {
            keywords = Enum.IsDefined(typeof(Keywords), numericKeyword)
                ? (Keywords)numericKeyword
                : null;
            if (keywords.HasValue) {
                return true;
            }
        }

        keywords = normalized switch {
            "none" => EventViewerX.Keywords.None,
            "auditfailure" => EventViewerX.Keywords.AuditFailure,
            "auditsuccess" => EventViewerX.Keywords.AuditSuccess,
            "correlationhint2" => EventViewerX.Keywords.CorrelationHint2,
            "eventlogclassic" => EventViewerX.Keywords.EventLogClassic,
            "sqm" => EventViewerX.Keywords.Sqm,
            "wdidiagnostic" => EventViewerX.Keywords.WdiDiagnostic,
            "wdicontext" => EventViewerX.Keywords.WdiContext,
            "responsetime" => EventViewerX.Keywords.ResponseTime,
            _ => null
        };

        if (keywords.HasValue) {
            return true;
        }

        error = $"keywords must be one of: {string.Join(", ", KeywordNames)}.";
        return false;
    }

    private static string NormalizeToken(string? value) {
        return new string((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(static ch => ch != '_' && ch != '-' && !char.IsWhiteSpace(ch))
            .ToArray());
    }

    private static Hashtable? ToHashtable(IReadOnlyDictionary<string, IReadOnlyList<string>>? source) {
        if (source is not { Count: > 0 }) {
            return null;
        }

        var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source) {
            var key = NormalizeOptionalString(pair.Key);
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            var values = pair.Value?.Where(static value => value is not null).ToArray() ?? Array.Empty<string>();
            table[key] = values.Length switch {
                0 => Array.Empty<string>(),
                1 => values[0],
                _ => values
            };
        }

        return table.Count == 0 ? null : table;
    }
}
