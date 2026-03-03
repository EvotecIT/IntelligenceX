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
            .NoAdditionalProperties(),
        category: "eventlog",
        tags: new[] {
            "named_events",
            "correlation"
        });

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
        IReadOnlyList<NamedEventsQueryRow> Events,
        IReadOnlyList<ToolNextActionModel> NextActions,
        string Cursor,
        string ResumeToken,
        string FlowId,
        string StepId,
        IReadOnlyDictionary<string, string> Checkpoint,
        IReadOnlyDictionary<string, string> Handoff,
        double Confidence);

    private sealed record NamedEventsQueryRequest(
        IReadOnlyList<NamedEvents> NamedEvents,
        IReadOnlyList<string> EffectiveMachines,
        DateTime? StartUtc,
        DateTime? EndUtc,
        TimePeriod? TimePeriod,
        IReadOnlyList<string>? Categories,
        string? LogNameFilter,
        HashSet<int>? EventIdSet,
        int MaxEvents,
        int MaxThreads,
        int? MaxEventsPerNamedEvent,
        bool IncludePayload,
        HashSet<string>? PayloadKeySet);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogNamedEventsQueryTool"/> class.
    /// </summary>
    public EventLogNamedEventsQueryTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<NamedEventsQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var rawNamedEvents = reader.DistinctStringArray("named_events");
            var rawCategories = reader.DistinctStringArray("categories");
            if (!EventLogNamedEventsQueryShared.TryResolveNamedEvents(
                    rawNamedEvents,
                    rawCategories,
                    out var namedEvents,
                    out var categories,
                    out var namedEventsError)) {
                return ToolRequestBindingResult<NamedEventsQueryRequest>.Failure(namedEventsError ?? "Invalid named_events/categories filters.");
            }

            if (!EventLogNamedEventsQueryShared.TryResolveTimeWindow(
                    arguments,
                    out var startUtc,
                    out var endUtc,
                    out var timePeriod,
                    out var timeError)) {
                return ToolRequestBindingResult<NamedEventsQueryRequest>.Failure(timeError ?? "Invalid time range.");
            }

            if (!TryReadPositiveInt32Array(arguments, "event_ids", out var eventIds, out var eventIdsError)) {
                return ToolRequestBindingResult<NamedEventsQueryRequest>.Failure(eventIdsError ?? "event_ids is invalid.");
            }

            var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");
            var maxThreads = reader.CappedInt32("max_threads", 4, 1, EventLogNamedEventsQueryShared.MaxThreadsCap);
            var maxEventsPerNamedEvent = TryReadOptionalPositiveInt32(arguments, "max_events_per_named_event", maxEvents);
            var includePayload = reader.Boolean("include_payload", defaultValue: true);

            var payloadKeys = reader.DistinctStringArray("payload_keys");
            if (payloadKeys.Count > EventLogNamedEventsQueryShared.MaxPayloadKeys) {
                return ToolRequestBindingResult<NamedEventsQueryRequest>.Failure(
                    $"payload_keys supports at most {EventLogNamedEventsQueryShared.MaxPayloadKeys} values.");
            }

            var payloadKeySet = payloadKeys.Count > 0
                ? new HashSet<string>(payloadKeys.Select(EventLogNamedEventsQueryShared.ToSnakeCase), StringComparer.OrdinalIgnoreCase)
                : null;

            var request = new NamedEventsQueryRequest(
                NamedEvents: namedEvents,
                EffectiveMachines: EventLogNamedEventsQueryShared.ResolveMachines(arguments, EventLogNamedEventsQueryShared.MaxMachines),
                StartUtc: startUtc,
                EndUtc: endUtc,
                TimePeriod: timePeriod,
                Categories: categories,
                LogNameFilter: reader.OptionalString("log_name"),
                EventIdSet: eventIds is null ? null : new HashSet<int>(eventIds),
                MaxEvents: maxEvents,
                MaxThreads: maxThreads,
                MaxEventsPerNamedEvent: maxEventsPerNamedEvent,
                IncludePayload: includePayload,
                PayloadKeySet: payloadKeySet);

            return ToolRequestBindingResult<NamedEventsQueryRequest>.Success(request);
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<NamedEventsQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var namedEvents = context.Request.NamedEvents;
        var categories = context.Request.Categories;
        var startUtc = context.Request.StartUtc;
        var endUtc = context.Request.EndUtc;
        var timePeriod = context.Request.TimePeriod;
        var logNameFilter = context.Request.LogNameFilter;
        var eventIdSet = context.Request.EventIdSet;
        var maxEvents = context.Request.MaxEvents;
        var maxThreads = context.Request.MaxThreads;
        var maxEventsPerNamedEvent = context.Request.MaxEventsPerNamedEvent;
        var includePayload = context.Request.IncludePayload;
        var payloadKeySet = context.Request.PayloadKeySet;
        var machines = context.Request.EffectiveMachines;
        var namedEventsList = namedEvents.ToList();

        var rows = new List<NamedEventsQueryRow>(Math.Min(maxEvents, 256));
        var perNamedEventCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filteredOut = 0;
        var truncated = false;

        try {
            await foreach (var item in SearchEvents.FindEventsByNamedEvents(
                               typeEventsList: namedEventsList,
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
            return ToolResultV2.Error("invalid_argument", ex.Message);
        } catch (Exception ex) {
            return ErrorFromException(
                ex,
                defaultMessage: "Named events query failed.",
                invalidOperationErrorCode: "query_failed");
        }

        var entityHandoff = EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);
        var chain = BuildChainContract(
            namedEvents: namedEvents,
            machines: machines,
            startUtc: startUtc,
            endUtc: endUtc,
            maxEvents: maxEvents,
            maxThreads: maxThreads,
            truncated: truncated,
            rows: rows,
            entityHandoff: entityHandoff);
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
            Events: rows,
            NextActions: chain.NextActions,
            Cursor: chain.Cursor,
            ResumeToken: chain.ResumeToken,
            FlowId: chain.FlowId,
            StepId: chain.StepId,
            Checkpoint: chain.Checkpoint,
            Handoff: chain.Handoff,
            Confidence: chain.Confidence);

        return ToolResultV2.OkAutoTableResponse(
            arguments: SanitizeProjectionArguments(context.Arguments, rows),
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

    private static bool TryReadPositiveInt32Array(
        JsonObject? arguments,
        string key,
        out List<int>? values,
        out string? error) {
        values = null;
        error = null;

        if (!TryGetArray(arguments, key, out var array)) {
            return true;
        }

        values = ToolArgs.TryReadPositiveInt32Array(array, key, out error);
        return string.IsNullOrWhiteSpace(error);
    }

    private static int? TryReadOptionalPositiveInt32(JsonObject? arguments, string key, int maxInclusive) {
        if (!TryGetInt64(arguments, key, out var value)) {
            return null;
        }

        return ToolArgs.ToPositiveInt32OrNull(value, maxInclusive);
    }

    private static bool TryGetArray(JsonObject? arguments, string key, out JsonArray? array) {
        array = null;
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            array = kv.Value.AsArray();
            return array is not null;
        }

        return false;
    }

    private static bool TryGetInt64(JsonObject? arguments, string key, out long? value) {
        value = null;
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            value = kv.Value.AsInt64();
            return value.HasValue;
        }

        return false;
    }

    private static ToolChainContractModel BuildChainContract(
        IReadOnlyList<NamedEvents> namedEvents,
        IReadOnlyList<string> machines,
        DateTime? startUtc,
        DateTime? endUtc,
        int maxEvents,
        int maxThreads,
        bool truncated,
        IReadOnlyList<NamedEventsQueryRow> rows,
        JsonObject entityHandoff) {
        var nextActions = new List<ToolNextActionModel> {
            ToolChainingHints.NextAction(
                tool: "ad_handoff_prepare",
                reason: "Normalize event identities into AD-ready lookup targets.",
                suggestedArguments: ToolChainingHints.Map(
                    ("entity_handoff_ref", "meta.entity_handoff"),
                    ("entity_handoff_contract", entityHandoff.GetString("contract") ?? "eventlog_entity_handoff"))),
            ToolChainingHints.NextAction(
                tool: "ad_scope_discovery",
                reason: "Resolve effective AD scope before identity/object follow-up queries.",
                suggestedArguments: ToolChainingHints.Map(("discovery_fallback", "current_domain")))
        };

        if (truncated) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "eventlog_named_events_query",
                reason: "This page is truncated; rerun with narrower time/filter constraints for complete coverage.",
                suggestedArguments: BuildSelfQueryArguments(
                    namedEvents: namedEvents,
                    machines: machines,
                    startUtc: startUtc,
                    endUtc: endUtc,
                    maxEvents: maxEvents,
                    maxThreads: maxThreads)));
        }

        var lastRecordId = rows.LastOrDefault()?.RecordId?.ToString() ?? string.Empty;
        var confidence = rows.Count == 0 ? 0.45d : 0.90d;
        if (truncated) {
            confidence -= 0.18d;
        }

        return ToolChainingHints.Create(
            nextActions: nextActions,
            cursor: ToolChainingHints.BuildToken(
                "eventlog_named_events_query",
                ("events", rows.Count.ToString()),
                ("truncated", truncated ? "1" : "0"),
                ("last_record_id", lastRecordId)),
            resumeToken: ToolChainingHints.BuildToken(
                "eventlog_named_events_query.resume",
                ("named_events", namedEvents.Count.ToString()),
                ("machines", machines.Count.ToString()),
                ("start", ToolTime.FormatUtc(startUtc) ?? string.Empty),
                ("end", ToolTime.FormatUtc(endUtc) ?? string.Empty)),
            handoff: BuildHandoffMap(entityHandoff),
            confidence: confidence,
            flowId: ToolChainingHints.BuildToken(
                "eventlog_named_events_query.flow",
                ("named_events", namedEvents.Count.ToString()),
                ("machines", machines.Count.ToString())),
            stepId: "named_events_page",
            checkpoint: ToolChainingHints.Map(
                ("rows", rows.Count),
                ("truncated", truncated),
                ("last_record_id", lastRecordId),
                ("max_events", maxEvents)));
    }

    private static IReadOnlyDictionary<string, string> BuildSelfQueryArguments(
        IReadOnlyList<NamedEvents> namedEvents,
        IReadOnlyList<string> machines,
        DateTime? startUtc,
        DateTime? endUtc,
        int maxEvents,
        int maxThreads) {
        return ToolChainingHints.Map(
            ("named_events", string.Join(",", namedEvents.Select(EventLogNamedEventsHelper.GetQueryName))),
            ("machine_names", string.Join(",", machines)),
            ("start_time_utc", ToolTime.FormatUtc(startUtc)),
            ("end_time_utc", ToolTime.FormatUtc(endUtc)),
            ("max_events", maxEvents),
            ("max_threads", maxThreads));
    }

    private static IReadOnlyDictionary<string, string> BuildHandoffMap(JsonObject entityHandoff) {
        var identityCandidates = entityHandoff.GetArray("identity_candidates");
        var computerCandidates = entityHandoff.GetArray("computer_candidates");

        var identityPreview = identityCandidates?
            .Take(5)
            .Select(static item => item.AsObject()?.GetString("value") ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? Array.Empty<string>();
        var computerPreview = computerCandidates?
            .Take(5)
            .Select(static item => item.AsObject()?.GetString("value") ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? Array.Empty<string>();

        return ToolChainingHints.Map(
            ("contract", entityHandoff.GetString("contract") ?? "eventlog_entity_handoff"),
            ("version", entityHandoff.GetInt64("version")?.ToString() ?? "1"),
            ("scanned_rows", entityHandoff.GetInt64("scanned_rows")?.ToString() ?? string.Empty),
            ("identity_candidates_total", entityHandoff.GetInt64("identity_candidates_total")?.ToString() ?? string.Empty),
            ("computer_candidates_total", entityHandoff.GetInt64("computer_candidates_total")?.ToString() ?? string.Empty),
            ("identity_candidates_preview", string.Join(";", identityPreview)),
            ("computer_candidates_preview", string.Join(";", computerPreview)));
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
