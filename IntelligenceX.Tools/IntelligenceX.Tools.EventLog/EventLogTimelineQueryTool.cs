using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using EventViewerX.Reports.Correlation;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Builds reusable event timelines and correlation groups from EventViewerX named-event detections.
/// </summary>
public sealed class EventLogTimelineQueryTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly string[] CorrelationKeyNames = NamedEventsTimelineQueryExecutor.AllowedCorrelationKeys
        .ToArray();
    private static readonly string[] CorrelationProfileNames = NamedEventsTimelineCorrelationProfiles.Names
        .ToArray();

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_timeline_query",
        "Correlate named-event detections into ordered timeline rows, group summaries, and time buckets (no hardcoded incident type assumptions).",
        ToolSchema.Object(
                ("named_events", ToolSchema.Array(ToolSchema.String("Named event identifier (enum_name or query_name from eventlog_named_events_catalog)."), "Named events to execute.")),
                ("categories", ToolSchema.Array(ToolSchema.String("Named-event category from eventlog_named_events_catalog.").Enum(EventLogNamedEventsQueryShared.CategoryNames), "Optional category list used to expand named_events.")),
                ("machine_name", ToolSchema.String("Optional single remote machine name/FQDN. Omit for local machine.")),
                ("machine_names", ToolSchema.Array(ToolSchema.String(), "Optional remote machine names/FQDNs. Combined with machine_name.")),
                ("time_period", ToolSchema.String("Optional relative time window. Mutually exclusive with start_time_utc/end_time_utc.").Enum(EventLogNamedEventsQueryShared.TimePeriodNames)),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("log_name", ToolSchema.String("Optional exact log-name filter applied to normalized rows.")),
                ("event_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional Event ID filter applied after rule evaluation.")),
                ("max_events_per_named_event", ToolSchema.Integer("Optional per-rule cap to prevent one named event from dominating results.")),
                ("max_events", ToolSchema.Integer("Maximum events to return (capped).")),
                ("max_threads", ToolSchema.Integer("Maximum query concurrency (capped).")),
                ("correlation_profile", ToolSchema.String("Optional correlation profile preset. Cannot be combined with correlation_keys.").Enum(CorrelationProfileNames)),
                ("correlation_keys", ToolSchema.Array(ToolSchema.String("Correlation field key.").Enum(CorrelationKeyNames), "Optional correlation dimensions. Defaults to who/object_affected/computer.")),
                ("include_uncorrelated", ToolSchema.Boolean("When false, events missing all correlation key values are excluded from timeline/groups.")),
                ("max_groups", ToolSchema.Integer("Maximum correlation groups to return (capped).")),
                ("bucket_minutes", ToolSchema.Integer("Bucket size in minutes for timeline density view (default 15, capped).")),
                ("include_payload", ToolSchema.Boolean("When true, include payload object in timeline rows (default false).")),
                ("payload_keys", ToolSchema.Array(ToolSchema.String("Payload field name in snake_case."), "Optional payload key allowlist when include_payload=true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "eventlog",
        tags: new[] {
            "timeline",
            "correlation"
        });

    private sealed record TimelineRequest(
        NamedEventsTimelineQueryRequest QueryRequest,
        IReadOnlyList<string>? Categories,
        string? CorrelationProfile);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogTimelineQueryTool"/> class.
    /// </summary>
    public EventLogTimelineQueryTool(EventLogToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<TimelineRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var rawNamedEvents = reader.DistinctStringArray("named_events");
            var rawCategories = reader.DistinctStringArray("categories");
            if (!EventLogNamedEventsQueryShared.TryResolveNamedEvents(
                    rawNamedEvents,
                    rawCategories,
                    out var namedEvents,
                    out var categories,
                    out var namedEventsError)) {
                return ToolRequestBindingResult<TimelineRequest>.Failure(namedEventsError ?? "Invalid named_events/categories filters.");
            }

            if (!EventLogNamedEventsQueryShared.TryResolveTimeWindow(
                    arguments,
                    out var startUtc,
                    out var endUtc,
                    out var timePeriod,
                    out var timeError)) {
                return ToolRequestBindingResult<TimelineRequest>.Failure(timeError ?? "Invalid time range.");
            }

            var logNameFilter = reader.OptionalString("log_name");
            if (!TryReadPositiveInt32Array(arguments, "event_ids", out var eventIds, out var eventIdsError)) {
                return ToolRequestBindingResult<TimelineRequest>.Failure(eventIdsError ?? "event_ids is invalid.");
            }

            var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");
            var maxThreads = reader.CappedInt32("max_threads", 4, 1, EventLogNamedEventsQueryShared.MaxThreadsCap);
            var maxEventsPerNamedEvent = TryReadOptionalPositiveInt32(arguments, "max_events_per_named_event", maxEvents);

            var includeUncorrelated = reader.Boolean("include_uncorrelated", defaultValue: true);
            var maxGroups = reader.CappedInt32("max_groups", 250, 1, 2000);
            var bucketMinutes = reader.CappedInt32("bucket_minutes", 15, 1, 1440);

            var includePayload = reader.Boolean("include_payload", defaultValue: false);
            var payloadKeys = reader.DistinctStringArray("payload_keys");
            if (payloadKeys.Count > EventLogNamedEventsQueryShared.MaxPayloadKeys) {
                return ToolRequestBindingResult<TimelineRequest>.Failure(
                    $"payload_keys supports at most {EventLogNamedEventsQueryShared.MaxPayloadKeys} values.");
            }

            IReadOnlyList<string>? normalizedPayloadKeys = null;
            if (payloadKeys.Count > 0) {
                normalizedPayloadKeys = payloadKeys
                    .Select(EventLogNamedEventsQueryShared.ToSnakeCase)
                    .Where(static key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var rawCorrelationKeys = reader.DistinctStringArray("correlation_keys");
            var rawCorrelationProfile = reader.OptionalString("correlation_profile");
            if (rawCorrelationKeys.Count > 0 && !string.IsNullOrWhiteSpace(rawCorrelationProfile)) {
                return ToolRequestBindingResult<TimelineRequest>.Failure("correlation_profile cannot be combined with correlation_keys.");
            }

            if (!NamedEventsTimelineCorrelationProfiles.TryResolve(
                    rawCorrelationProfile,
                    out var correlationProfile,
                    out var profileCorrelationKeys,
                    out var correlationProfileError)) {
                return ToolRequestBindingResult<TimelineRequest>.Failure(correlationProfileError ?? "Invalid correlation_profile.");
            }

            IReadOnlyList<string>? correlationKeys = rawCorrelationKeys.Count == 0
                ? profileCorrelationKeys
                : rawCorrelationKeys;

            var machines = EventLogNamedEventsQueryShared.ResolveMachines(arguments, EventLogNamedEventsQueryShared.MaxMachines);

            var queryRequest = new NamedEventsTimelineQueryRequest {
                NamedEvents = namedEvents,
                MachineNames = machines,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                TimePeriod = timePeriod,
                LogName = logNameFilter,
                EventIds = eventIds,
                MaxEvents = maxEvents,
                MaxThreads = maxThreads,
                MaxEventsPerNamedEvent = maxEventsPerNamedEvent,
                CorrelationKeys = correlationKeys,
                IncludeUncorrelated = includeUncorrelated,
                MaxGroups = maxGroups,
                BucketMinutes = bucketMinutes,
                IncludePayload = includePayload,
                PayloadKeys = normalizedPayloadKeys
            };

            return ToolRequestBindingResult<TimelineRequest>.Success(
                new TimelineRequest(queryRequest, categories, correlationProfile));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<TimelineRequest> context, CancellationToken cancellationToken) {
        var request = context.Request.QueryRequest;
        var categories = context.Request.Categories;

        var (result, failure) = await NamedEventsTimelineQueryExecutor.TryBuildAsync(request, cancellationToken);
        if (failure is not null) {
            return ErrorFromTimelineFailure(failure);
        }

        if (result is null) {
            return ToolResultV2.Error("query_failed", "Timeline query failed.");
        }

        var entityHandoff = EventLogEntityHandoff.BuildFromRows(
            rows: result.Timeline,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        return ToolResultV2.OkAutoTableResponse(
            arguments: SanitizeProjectionArguments(context.Arguments, result.Timeline),
            model: result,
            sourceRows: result.Timeline,
            viewRowsPath: "timeline_view",
            title: "Event timeline (preview)",
            baseTruncated: result.Truncated,
            scanned: result.Timeline.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("requested_named_events_count", request.NamedEvents?.Count ?? 0);
                meta.Add("max_events", result.MaxEvents);
                meta.Add("max_threads", result.MaxThreads);
                meta.Add("max_groups", request.MaxGroups);
                meta.Add("bucket_minutes", result.BucketMinutes);
                meta.Add("include_uncorrelated", result.IncludeUncorrelated);
                meta.Add("include_payload", request.IncludePayload);
                meta.Add("correlation_keys", ToolJson.ToJsonArray(result.CorrelationKeys));
                meta.Add("groups_count", result.CorrelationGroups.Count);
                meta.Add("groups_total", result.GroupsTotal);
                meta.Add("buckets_count", result.Buckets.Count);
                meta.Add("filtered_out", result.FilteredOut);
                meta.Add("filtered_uncorrelated", result.FilteredUncorrelated);
                if (request.MaxEventsPerNamedEvent.HasValue) {
                    meta.Add("max_events_per_named_event", request.MaxEventsPerNamedEvent.Value);
                }
                if (!string.IsNullOrWhiteSpace(request.LogName)) {
                    meta.Add("log_name", request.LogName);
                }
                if (request.EventIds is { Count: > 0 }) {
                    meta.Add("event_ids", ToolJson.ToJsonArray(request.EventIds.OrderBy(static value => value)));
                }
                if (categories is { Count: > 0 }) {
                    meta.Add("categories", ToolJson.ToJsonArray(categories));
                }
                if (request.PayloadKeys is { Count: > 0 }) {
                    meta.Add("payload_keys", ToolJson.ToJsonArray(request.PayloadKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
                }
                if (result.EffectiveMachines.Count > 0) {
                    meta.Add("machines_count", result.EffectiveMachines.Count);
                }
                if (result.StartTimeUtc.HasValue) {
                    meta.Add("start_time_utc", ToolTime.FormatUtc(result.StartTimeUtc));
                }
                if (result.EndTimeUtc.HasValue) {
                    meta.Add("end_time_utc", ToolTime.FormatUtc(result.EndTimeUtc));
                }
                if (request.TimePeriod.HasValue) {
                    meta.Add("time_period", EventLogNamedEventsQueryShared.ToSnakeCase(request.TimePeriod.Value.ToString()));
                }
                if (!string.IsNullOrWhiteSpace(context.Request.CorrelationProfile)) {
                    meta.Add("correlation_profile", context.Request.CorrelationProfile);
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

    private static string ErrorFromTimelineFailure(NamedEventsTimelineQueryFailure failure) {
        return failure.Kind switch {
            NamedEventsTimelineQueryFailureKind.InvalidArgument => ToolResultV2.Error("invalid_argument", failure.Message),
            NamedEventsTimelineQueryFailureKind.QueryFailed => ToolResultV2.Error("query_failed", failure.Message),
            _ => ToolResultV2.Error("execution_error", failure.Message)
        };
    }
}
