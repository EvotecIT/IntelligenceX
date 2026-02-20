using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogNamedEventsQueryShared {
    internal const int MaxNamedEvents = 24;
    internal const int MaxCategoryFilters = 16;
    internal const int MaxMachines = 32;
    internal const int MaxPayloadKeys = 64;
    internal const int MaxThreadsCap = 8;

    internal static readonly string[] CategoryNames = EventLogNamedEventsHelper.GetKnownCategories().ToArray();
    internal static readonly string[] TimePeriodNames = Enum.GetValues<TimePeriod>()
        .Select(static value => ToSnakeCase(value.ToString()))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly IReadOnlyDictionary<string, TimePeriod> TimePeriodByName = BuildTimePeriodMap();

    internal static bool TryResolveNamedEvents(
        IReadOnlyList<string> rawNamedEvents,
        IReadOnlyList<string> rawCategories,
        out List<NamedEvents> namedEvents,
        out List<string>? categories,
        out string? error) {
        namedEvents = new List<NamedEvents>();
        categories = null;
        error = null;

        if (rawNamedEvents.Count == 0 && rawCategories.Count == 0) {
            error = "Provide at least one of: named_events, categories.";
            return false;
        }

        if (rawNamedEvents.Count > 0) {
            if (!EventLogNamedEventsHelper.TryParseMany(rawNamedEvents, MaxNamedEvents, out var parsedNamedEvents, out var namedEventsError)) {
                error = namedEventsError ?? "Invalid named_events argument.";
                return false;
            }

            namedEvents.AddRange(parsedNamedEvents);
        }

        if (rawCategories.Count > 0) {
            if (!EventLogNamedEventsHelper.TryParseCategories(rawCategories, MaxCategoryFilters, out var parsedCategories, out var categoriesError)) {
                error = categoriesError ?? "Invalid categories argument.";
                return false;
            }

            categories = parsedCategories;
            var categorySet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
            foreach (var namedEvent in Enum.GetValues<NamedEvents>()) {
                if (!categorySet.Contains(EventLogNamedEventsHelper.GetCategory(namedEvent))) {
                    continue;
                }

                if (!namedEvents.Contains(namedEvent)) {
                    namedEvents.Add(namedEvent);
                }
            }
        }

        if (namedEvents.Count == 0) {
            error = "No named events resolved from provided filters.";
            return false;
        }

        if (namedEvents.Count > MaxNamedEvents) {
            error = $"Resolved named events exceed limit ({MaxNamedEvents}). Narrow your filters.";
            return false;
        }

        return true;
    }

    internal static bool TryResolveTimeWindow(
        JsonObject? arguments,
        out DateTime? startUtc,
        out DateTime? endUtc,
        out TimePeriod? timePeriod,
        out string? error) {
        error = null;
        timePeriod = null;
        startUtc = null;
        endUtc = null;

        var timePeriodRaw = ToolArgs.GetOptionalTrimmed(arguments, "time_period");
        var hasExplicitTimeRange = !string.IsNullOrWhiteSpace(ToolArgs.GetOptionalTrimmed(arguments, "start_time_utc"))
                                   || !string.IsNullOrWhiteSpace(ToolArgs.GetOptionalTrimmed(arguments, "end_time_utc"));

        if (!string.IsNullOrWhiteSpace(timePeriodRaw) && hasExplicitTimeRange) {
            error = "time_period cannot be combined with start_time_utc/end_time_utc.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(timePeriodRaw)) {
            if (!TryParseTimePeriod(timePeriodRaw, out var parsedTimePeriod, out var timePeriodError)) {
                error = timePeriodError ?? "Invalid time_period value.";
                return false;
            }

            timePeriod = parsedTimePeriod;
            return true;
        }

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out startUtc, out endUtc, out var timeErr)) {
            error = timeErr ?? "Invalid time range.";
            return false;
        }

        return true;
    }

    internal static bool TryParseTimePeriod(string raw, out TimePeriod timePeriod, out string? error) {
        timePeriod = default;
        error = null;

        var normalized = ToSnakeCase(raw);
        if (string.IsNullOrWhiteSpace(normalized)) {
            error = "time_period is required when provided.";
            return false;
        }

        if (TimePeriodByName.TryGetValue(normalized, out timePeriod)) {
            return true;
        }

        error = $"time_period must be one of: {string.Join(", ", TimePeriodNames)}.";
        return false;
    }

    internal static List<string> ResolveMachines(JsonObject? arguments, int maxItems = MaxMachines) {
        var machines = new List<string>();

        var singleMachine = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        if (!string.IsNullOrWhiteSpace(singleMachine)) {
            machines.Add(singleMachine);
        }

        var machineNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("machine_names"));
        for (var i = 0; i < machineNames.Count; i++) {
            var machine = machineNames[i];
            if (string.IsNullOrWhiteSpace(machine)) {
                continue;
            }

            if (!machines.Contains(machine, StringComparer.OrdinalIgnoreCase)) {
                machines.Add(machine);
            }

            if (machines.Count >= maxItems) {
                break;
            }
        }

        return machines;
    }

    internal static string ToSnakeCase(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return string.Empty;
        }

        return JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
    }

    private static IReadOnlyDictionary<string, TimePeriod> BuildTimePeriodMap() {
        var map = new Dictionary<string, TimePeriod>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Enum.GetValues<TimePeriod>()) {
            map[ToSnakeCase(value.ToString())] = value;
        }
        return map;
    }
}
