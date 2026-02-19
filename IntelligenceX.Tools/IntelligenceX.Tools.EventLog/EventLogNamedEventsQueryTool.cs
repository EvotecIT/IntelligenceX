using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Queries EventViewerX named-event rules and returns normalized rows for agent reasoning.
/// </summary>
public sealed class EventLogNamedEventsQueryTool : EventLogToolBase, ITool {
    private const int MaxNamedEvents = 24;
    private const int MaxCategoryFilters = 16;
    private const int MaxMachines = 32;
    private const int MaxPayloadKeys = 64;
    private const int MaxThreadsCap = 8;
    private const int MaxViewTop = 5000;

    private static readonly string[] CategoryNames = EventLogNamedEventsHelper.GetKnownCategories().ToArray();
    private static readonly string[] TimePeriodNames = Enum.GetValues<TimePeriod>()
        .Select(static value => ToSnakeCase(value.ToString()))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private static readonly IReadOnlyDictionary<string, TimePeriod> TimePeriodByName = BuildTimePeriodMap();

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_named_events_query",
        "Query EventViewerX named-event rules (for example AD/Kerberos/GPO detections) with optional machine/time filters.",
        ToolSchema.Object(
                ("named_events", ToolSchema.Array(ToolSchema.String("Named event identifier (enum_name or query_name from eventlog_named_events_catalog)."), "Named events to execute.")),
                ("categories", ToolSchema.Array(ToolSchema.String("Named-event category from eventlog_named_events_catalog.").Enum(CategoryNames), "Optional category list used to expand named_events.")),
                ("machine_name", ToolSchema.String("Optional single remote machine name/FQDN. Omit for local machine.")),
                ("machine_names", ToolSchema.Array(ToolSchema.String(), "Optional remote machine names/FQDNs. Combined with machine_name.")),
                ("time_period", ToolSchema.String("Optional relative time window. Mutually exclusive with start_time_utc/end_time_utc.").Enum(TimePeriodNames)),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("log_name", ToolSchema.String("Optional exact log-name filter applied to normalized rows (for example Security/System/Application).")),
                ("event_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional Event ID filter applied after rule evaluation.")),
                ("max_events_per_named_event", ToolSchema.Integer("Optional per-rule cap to prevent one named event from dominating results.")),
                ("max_events", ToolSchema.Integer("Maximum events to return (capped).")),
                ("max_threads", ToolSchema.Integer("Maximum query concurrency (capped).")),
                ("include_payload", ToolSchema.Boolean("When true, include payload object (default true).")),
                ("payload_keys", ToolSchema.Array(ToolSchema.String("Payload field name in snake_case."), "Optional payload key allowlist when include_payload=true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record NamedEventsQueryRow(
        string NamedEvent,
        string RuleType,
        int EventId,
        long? RecordId,
        string GatheredFrom,
        string GatheredLogName,
        string? WhenUtc,
        string? Who,
        string? ObjectAffected,
        string? Computer,
        string? Action,
        object Payload);

    private sealed record NamedEventsQueryResult(
        IReadOnlyList<string> RequestedNamedEvents,
        IReadOnlyList<string> EffectiveMachines,
        DateTime? StartTimeUtc,
        DateTime? EndTimeUtc,
        int MaxEvents,
        int MaxThreads,
        bool Truncated,
        IReadOnlyList<NamedEventsQueryRow> Events);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogNamedEventsQueryTool"/> class.
    /// </summary>
    public EventLogNamedEventsQueryTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var rawNamedEvents = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("named_events"));
        var rawCategories = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("categories"));
        if (rawNamedEvents.Count == 0 && rawCategories.Count == 0) {
            return ToolResponse.Error("invalid_argument", "Provide at least one of: named_events, categories.");
        }

        var namedEvents = new List<NamedEvents>();
        if (rawNamedEvents.Count > 0) {
            if (!EventLogNamedEventsHelper.TryParseMany(rawNamedEvents, MaxNamedEvents, out var parsedNamedEvents, out var namedEventsError)) {
                return ToolResponse.Error("invalid_argument", namedEventsError ?? "Invalid named_events argument.");
            }

            namedEvents.AddRange(parsedNamedEvents);
        }

        List<string>? categories = null;
        if (rawCategories.Count > 0) {
            if (!EventLogNamedEventsHelper.TryParseCategories(rawCategories, MaxCategoryFilters, out var parsedCategories, out var categoriesError)) {
                return ToolResponse.Error("invalid_argument", categoriesError ?? "Invalid categories argument.");
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
            return ToolResponse.Error("invalid_argument", "No named events resolved from provided filters.");
        }
        if (namedEvents.Count > MaxNamedEvents) {
            return ToolResponse.Error("invalid_argument", $"Resolved named events exceed limit ({MaxNamedEvents}). Narrow your filters.");
        }

        var timePeriodRaw = ToolArgs.GetOptionalTrimmed(arguments, "time_period");
        var hasExplicitTimeRange = !string.IsNullOrWhiteSpace(ToolArgs.GetOptionalTrimmed(arguments, "start_time_utc")) ||
                                   !string.IsNullOrWhiteSpace(ToolArgs.GetOptionalTrimmed(arguments, "end_time_utc"));
        if (!string.IsNullOrWhiteSpace(timePeriodRaw) && hasExplicitTimeRange) {
            return ToolResponse.Error("invalid_argument", "time_period cannot be combined with start_time_utc/end_time_utc.");
        }

        DateTime? startUtc;
        DateTime? endUtc;
        TimePeriod? timePeriod = null;
        if (!string.IsNullOrWhiteSpace(timePeriodRaw)) {
            if (!TryParseTimePeriod(timePeriodRaw, out var parsedTimePeriod, out var timePeriodError)) {
                return ToolResponse.Error("invalid_argument", timePeriodError ?? "Invalid time_period value.");
            }
            timePeriod = parsedTimePeriod;
            startUtc = null;
            endUtc = null;
        } else {
            if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out startUtc, out endUtc, out var timeErr)) {
                return ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range.");
            }
        }

        var logNameFilter = ToolArgs.GetOptionalTrimmed(arguments, "log_name");
        var eventIds = ToolArgs.TryReadPositiveInt32Array(arguments?.GetArray("event_ids"), "event_ids", out var eventIdsError);
        if (!string.IsNullOrWhiteSpace(eventIdsError)) {
            return ToolResponse.Error("invalid_argument", eventIdsError);
        }
        var eventIdSet = eventIds is null ? null : new HashSet<int>(eventIds);

        var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");
        var maxThreads = ToolArgs.GetCappedInt32(arguments, "max_threads", 4, 1, MaxThreadsCap);
        var maxEventsPerNamedEvent = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("max_events_per_named_event"), maxEvents);
        var includePayload = arguments?.GetBoolean("include_payload") ?? true;
        var payloadKeys = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("payload_keys"));
        if (payloadKeys.Count > MaxPayloadKeys) {
            return ToolResponse.Error("invalid_argument", $"payload_keys supports at most {MaxPayloadKeys} values.");
        }
        var payloadKeySet = payloadKeys.Count > 0
            ? new HashSet<string>(payloadKeys.Select(ToSnakeCase), StringComparer.OrdinalIgnoreCase)
            : null;

        var machines = ResolveMachines(arguments, maxItems: MaxMachines);

        var rows = new List<NamedEventsQueryRow>(Math.Min(maxEvents, 256));
        var perNamedEventCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filteredOut = 0;
        var truncated = false;

        try {
            await foreach (var item in SearchEvents.FindEventsByNamedEvents(
                               typeEventsList: namedEvents,
                               machineNames: machines.Count > 0 ? machines.Cast<string?>().ToList() : null,
                               startTime: startUtc,
                               endTime: endUtc,
                               timePeriod: timePeriod,
                               maxThreads: maxThreads,
                               maxEvents: maxEvents,
                               cancellationToken: cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                var namedEventName = ResolveNamedEventName(item);
                if (maxEventsPerNamedEvent.HasValue) {
                    var currentCount = perNamedEventCount.TryGetValue(namedEventName, out var c) ? c : 0;
                    if (currentCount >= maxEventsPerNamedEvent.Value) {
                        filteredOut++;
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(logNameFilter) &&
                    !string.Equals(item.GatheredLogName, logNameFilter, StringComparison.OrdinalIgnoreCase)) {
                    filteredOut++;
                    continue;
                }

                if (eventIdSet is not null && !eventIdSet.Contains(item.EventID)) {
                    filteredOut++;
                    continue;
                }

                rows.Add(ToRow(item, namedEventName, includePayload, payloadKeySet));
                perNamedEventCount[namedEventName] = perNamedEventCount.TryGetValue(namedEventName, out var count) ? count + 1 : 1;
                if (rows.Count >= maxEvents) {
                    truncated = true;
                    break;
                }
            }
        } catch (ArgumentException ex) {
            return ToolResponse.Error("invalid_argument", ex.Message);
        } catch (Exception ex) {
            return ErrorFromException(
                ex,
                defaultMessage: "Named events query failed.",
                invalidOperationErrorCode: "query_failed");
        }

        var result = new NamedEventsQueryResult(
            RequestedNamedEvents: namedEvents.Select(EventLogNamedEventsHelper.GetQueryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            EffectiveMachines: machines,
            StartTimeUtc: startUtc,
            EndTimeUtc: endUtc,
            MaxEvents: maxEvents,
            MaxThreads: maxThreads,
            Truncated: truncated,
            Events: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "events_view",
            title: "Named events (preview)",
            baseTruncated: truncated,
            scanned: rows.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("requested_named_events_count", namedEvents.Count);
                meta.Add("max_events", maxEvents);
                meta.Add("max_threads", maxThreads);
                meta.Add("include_payload", includePayload);
                if (maxEventsPerNamedEvent.HasValue) {
                    meta.Add("max_events_per_named_event", maxEventsPerNamedEvent.Value);
                }
                if (!string.IsNullOrWhiteSpace(logNameFilter)) {
                    meta.Add("log_name", logNameFilter);
                }
                if (eventIdSet is not null) {
                    meta.Add("event_ids", ToolJson.ToJsonArray(eventIdSet.OrderBy(static x => x)));
                }
                if (categories is not null && categories.Count > 0) {
                    meta.Add("categories", ToolJson.ToJsonArray(categories));
                }
                if (payloadKeySet is not null && payloadKeySet.Count > 0) {
                    meta.Add("payload_keys", ToolJson.ToJsonArray(payloadKeySet.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)));
                }
                if (machines.Count > 0) {
                    meta.Add("machines_count", machines.Count);
                }
                meta.Add("filtered_out", filteredOut);
                if (startUtc.HasValue) {
                    meta.Add("start_time_utc", ToolTime.FormatUtc(startUtc));
                }
                if (endUtc.HasValue) {
                    meta.Add("end_time_utc", ToolTime.FormatUtc(endUtc));
                }
                if (timePeriod.HasValue) {
                    meta.Add("time_period", ToSnakeCase(timePeriod.Value.ToString()));
                }
            });
        return response;
    }

    private static List<string> ResolveMachines(JsonObject? arguments, int maxItems) {
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

    private static NamedEventsQueryRow ToRow(
        EventObjectSlim item,
        string namedEvent,
        bool includePayload,
        HashSet<string>? payloadKeySet) {
        var fullPayload = ExtractPayload(item);
        var payload = includePayload
            ? ProjectPayload(fullPayload, payloadKeySet)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new NamedEventsQueryRow(
            NamedEvent: namedEvent,
            RuleType: item.GetType().Name,
            EventId: item.EventID,
            RecordId: item.RecordID,
            GatheredFrom: item.GatheredFrom,
            GatheredLogName: item.GatheredLogName,
            WhenUtc: ReadPayloadUtc(fullPayload, "when"),
            Who: ReadPayloadString(fullPayload, "who"),
            ObjectAffected: ReadPayloadString(fullPayload, "object_affected"),
            Computer: ReadPayloadString(fullPayload, "computer"),
            Action: ReadPayloadString(fullPayload, "action"),
            Payload: payload);
    }

    private static string ResolveNamedEventName(EventObjectSlim item) {
        return EventLogNamedEventsHelper.TryParseOne(item.Type, out var parsedNamedEvent)
            ? EventLogNamedEventsHelper.GetQueryName(parsedNamedEvent)
            : ToSnakeCase(item.Type);
    }

    private static Dictionary<string, object?> ExtractPayload(EventObjectSlim item) {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var type = item.GetType();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeField(field)) {
                continue;
            }

            var value = field.GetValue(item);
            payload[ToSnakeCase(field.Name)] = NormalizeValue(value);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!ShouldIncludeProperty(property)) {
                continue;
            }

            var key = ToSnakeCase(property.Name);
            if (payload.ContainsKey(key)) {
                continue;
            }

            object? value;
            try {
                value = property.GetValue(item);
            } catch {
                continue;
            }

            payload[key] = NormalizeValue(value);
        }

        return payload;
    }

    private static Dictionary<string, object?> ProjectPayload(
        Dictionary<string, object?> payload,
        HashSet<string>? payloadKeySet) {
        if (payloadKeySet is null || payloadKeySet.Count == 0) {
            return payload;
        }

        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in payloadKeySet) {
            if (payload.TryGetValue(key, out var value)) {
                projected[key] = value;
            }
        }

        return projected;
    }

    private static bool ShouldIncludeField(FieldInfo field) {
        if (field is null) {
            return false;
        }

        if (field.Name.StartsWith("_", StringComparison.Ordinal)) {
            return false;
        }

        if (string.Equals(field.Name, nameof(EventObjectSlim.EventID), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.Name, nameof(EventObjectSlim.RecordID), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.Name, nameof(EventObjectSlim.GatheredFrom), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.Name, nameof(EventObjectSlim.GatheredLogName), StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (string.Equals(field.FieldType.Name, "EventObject", StringComparison.Ordinal)) {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeProperty(PropertyInfo property) {
        if (property is null || !property.CanRead || property.GetMethod is null || !property.GetMethod.IsPublic) {
            return false;
        }

        if (property.GetIndexParameters().Length != 0) {
            return false;
        }

        return true;
    }

    private static object? NormalizeValue(object? value) {
        if (value is null) {
            return null;
        }

        if (value is DateTime dateTime) {
            return dateTime.ToUniversalTime().ToString("O");
        }

        if (value is DateTimeOffset dateTimeOffset) {
            return dateTimeOffset.ToUniversalTime().ToString("O");
        }

        if (value is Enum enumValue) {
            return enumValue.ToString();
        }

        return value;
    }

    private static string? ReadPayloadString(IReadOnlyDictionary<string, object?> payload, string key) {
        if (!payload.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ReadPayloadUtc(IReadOnlyDictionary<string, object?> payload, string key) {
        if (!payload.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        if (value is string text && DateTime.TryParse(text, out var parsed)) {
            return parsed.ToUniversalTime().ToString("O");
        }

        if (value is DateTime dateTime) {
            return dateTime.ToUniversalTime().ToString("O");
        }

        return value.ToString();
    }

    private static string ToSnakeCase(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return string.Empty;
        }

        return JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
    }

    private static bool TryParseTimePeriod(string raw, out TimePeriod timePeriod, out string? error) {
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

    private static IReadOnlyDictionary<string, TimePeriod> BuildTimePeriodMap() {
        var map = new Dictionary<string, TimePeriod>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Enum.GetValues<TimePeriod>()) {
            map[ToSnakeCase(value.ToString())] = value;
        }
        return map;
    }
}
