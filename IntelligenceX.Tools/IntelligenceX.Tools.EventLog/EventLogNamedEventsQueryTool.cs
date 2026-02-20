using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_named_events_query",
        "Query EventViewerX named-event rules (for example AD/Kerberos/GPO detections) with optional machine/time filters.",
        ToolSchema.Object(
                ("named_events", ToolSchema.Array(ToolSchema.String("Named event identifier (enum_name or query_name from eventlog_named_events_catalog)."), "Named events to execute.")),
                ("categories", ToolSchema.Array(ToolSchema.String("Named-event category from eventlog_named_events_catalog.").Enum(EventLogNamedEventsQueryShared.CategoryNames), "Optional category list used to expand named_events.")),
                ("machine_name", ToolSchema.String("Optional single remote machine name/FQDN. Omit for local machine.")),
                ("machine_names", ToolSchema.Array(ToolSchema.String(), "Optional remote machine names/FQDNs. Combined with machine_name.")),
                ("time_period", ToolSchema.String("Optional relative time window. Mutually exclusive with start_time_utc/end_time_utc.").Enum(EventLogNamedEventsQueryShared.TimePeriodNames)),
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
        if (!EventLogNamedEventsQueryShared.TryResolveNamedEvents(
                rawNamedEvents,
                rawCategories,
                out var namedEvents,
                out var categories,
                out var namedEventsError)) {
            return ToolResponse.Error("invalid_argument", namedEventsError ?? "Invalid named_events/categories filters.");
        }

        if (!EventLogNamedEventsQueryShared.TryResolveTimeWindow(
                arguments,
                out var startUtc,
                out var endUtc,
                out var timePeriod,
                out var timeError)) {
            return ToolResponse.Error("invalid_argument", timeError ?? "Invalid time range.");
        }

        var logNameFilter = ToolArgs.GetOptionalTrimmed(arguments, "log_name");
        var eventIds = ToolArgs.TryReadPositiveInt32Array(arguments?.GetArray("event_ids"), "event_ids", out var eventIdsError);
        if (!string.IsNullOrWhiteSpace(eventIdsError)) {
            return ToolResponse.Error("invalid_argument", eventIdsError);
        }
        var eventIdSet = eventIds is null ? null : new HashSet<int>(eventIds);

        var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");
        var maxThreads = ToolArgs.GetCappedInt32(arguments, "max_threads", 4, 1, EventLogNamedEventsQueryShared.MaxThreadsCap);
        var maxEventsPerNamedEvent = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("max_events_per_named_event"), maxEvents);
        var includePayload = arguments?.GetBoolean("include_payload") ?? true;

        var payloadKeys = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("payload_keys"));
        if (payloadKeys.Count > EventLogNamedEventsQueryShared.MaxPayloadKeys) {
            return ToolResponse.Error("invalid_argument", $"payload_keys supports at most {EventLogNamedEventsQueryShared.MaxPayloadKeys} values.");
        }
        var payloadKeySet = payloadKeys.Count > 0
            ? new HashSet<string>(payloadKeys.Select(EventLogNamedEventsQueryShared.ToSnakeCase), StringComparer.OrdinalIgnoreCase)
            : null;

        var machines = EventLogNamedEventsQueryShared.ResolveMachines(arguments, EventLogNamedEventsQueryShared.MaxMachines);

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

                var namedEventName = EventLogNamedEventsPayload.ResolveNamedEventName(item);
                if (maxEventsPerNamedEvent.HasValue) {
                    var currentCount = perNamedEventCount.TryGetValue(namedEventName, out var count) ? count : 0;
                    if (currentCount >= maxEventsPerNamedEvent.Value) {
                        filteredOut++;
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(logNameFilter)
                    && !string.Equals(item.GatheredLogName, logNameFilter, StringComparison.OrdinalIgnoreCase)) {
                    filteredOut++;
                    continue;
                }

                if (eventIdSet is not null && !eventIdSet.Contains(item.EventID)) {
                    filteredOut++;
                    continue;
                }

                rows.Add(ToRow(item, namedEventName, includePayload, payloadKeySet));
                perNamedEventCount[namedEventName] = perNamedEventCount.TryGetValue(namedEventName, out var current) ? current + 1 : 1;
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
            RequestedNamedEvents: namedEvents
                .Select(EventLogNamedEventsHelper.GetQueryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            EffectiveMachines: machines,
            StartTimeUtc: startUtc,
            EndTimeUtc: endUtc,
            MaxEvents: maxEvents,
            MaxThreads: maxThreads,
            Truncated: truncated,
            Events: rows);

        var entityHandoff = EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        return BuildAutoTableResponse(
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
                    meta.Add("event_ids", ToolJson.ToJsonArray(eventIdSet.OrderBy(static value => value)));
                }
                if (categories is not null && categories.Count > 0) {
                    meta.Add("categories", ToolJson.ToJsonArray(categories));
                }
                if (payloadKeySet is not null && payloadKeySet.Count > 0) {
                    meta.Add("payload_keys", ToolJson.ToJsonArray(payloadKeySet.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
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
                    meta.Add("time_period", EventLogNamedEventsQueryShared.ToSnakeCase(timePeriod.Value.ToString()));
                }
                meta.Add("entity_handoff", entityHandoff);
            });
    }

    private static NamedEventsQueryRow ToRow(
        EventObjectSlim item,
        string namedEvent,
        bool includePayload,
        HashSet<string>? payloadKeySet) {
        var fullPayload = EventLogNamedEventsPayload.ExtractPayload(item);
        var payload = includePayload
            ? EventLogNamedEventsPayload.ProjectPayload(fullPayload, payloadKeySet)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new NamedEventsQueryRow(
            NamedEvent: namedEvent,
            RuleType: item.GetType().Name,
            EventId: item.EventID,
            RecordId: item.RecordID,
            GatheredFrom: item.GatheredFrom,
            GatheredLogName: item.GatheredLogName,
            WhenUtc: EventLogNamedEventsPayload.ReadPayloadUtc(fullPayload, "when"),
            Who: EventLogNamedEventsPayload.ReadPayloadString(fullPayload, "who"),
            ObjectAffected: EventLogNamedEventsPayload.ReadPayloadString(fullPayload, "object_affected"),
            Computer: EventLogNamedEventsPayload.ReadPayloadString(fullPayload, "computer"),
            Action: EventLogNamedEventsPayload.ReadPayloadString(fullPayload, "action"),
            Payload: payload);
    }
}
