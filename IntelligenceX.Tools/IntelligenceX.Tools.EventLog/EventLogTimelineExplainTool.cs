using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Correlation;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Suggests correlation/timeline settings and follow-up tools based on investigation goals and timeline shape.
/// </summary>
public sealed class EventLogTimelineExplainTool : EventLogToolBase, ITool {
    private static readonly string[] CorrelationKeyNames = NamedEventsTimelineQueryExecutor.AllowedCorrelationKeys.ToArray();
    private static readonly string[] CorrelationProfileNames = NamedEventsTimelineCorrelationProfiles.Names.ToArray();
    private static readonly string[] InvestigationGoalNames = BuildInvestigationGoalNames();
    private static readonly string[] KeyPriority = {
        "who",
        "object_affected",
        "computer",
        "action",
        "named_event",
        "event_id",
        "gathered_from",
        "gathered_log_name"
    };
    private static readonly string[] DefaultPayloadKeys = {
        "who",
        "object_affected",
        "computer",
        "action",
        "when"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_timeline_explain",
        "Suggest reusable timeline correlation settings and follow-up tools using investigation goal plus current timeline shape.",
        ToolSchema.Object(
                ("investigation_goal", ToolSchema.String("Optional investigation goal.").Enum(InvestigationGoalNames)),
                ("correlation_keys_present", ToolSchema.Array(ToolSchema.String("Correlation key observed in current timeline output.").Enum(CorrelationKeyNames), "Optional observed correlation keys from current timeline output.")),
                ("timeline_count", ToolSchema.Integer("Optional current timeline row count.")),
                ("groups_count", ToolSchema.Integer("Optional current correlation group count.")),
                ("filtered_uncorrelated", ToolSchema.Integer("Optional count of rows dropped due to missing correlation values.")),
                ("prefer_profile", ToolSchema.Boolean("When true (default), prefer a reusable correlation_profile over explicit correlation_keys when possible.")),
                ("include_ad_enrichment", ToolSchema.Boolean("When true (default), include AD enrichment follow-up tools.")),
                ("include_payload", ToolSchema.Boolean("When true, recommend payload capture and payload_keys.")))
            .NoAdditionalProperties());

    private sealed record ExplainRequest(
        string InvestigationGoal,
        IReadOnlyList<string> CorrelationKeysPresent,
        int TimelineCount,
        int GroupsCount,
        int FilteredUncorrelated,
        bool PreferProfile,
        bool IncludeAdEnrichment,
        bool IncludePayload);

    private sealed record TimelineQueryRecommendation(
        bool UseCorrelationProfile,
        string? CorrelationProfile,
        IReadOnlyList<string> CorrelationKeys,
        bool IncludeUncorrelated,
        int BucketMinutes,
        int MaxGroups,
        bool IncludePayload,
        IReadOnlyList<string> PayloadKeys);

    private sealed record TimelineExplainResult(
        string InvestigationGoal,
        TimelineQueryRecommendation TimelineQuery,
        IReadOnlyList<string> FollowUpTools,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogTimelineExplainTool"/> class.
    /// </summary>
    public EventLogTimelineExplainTool(EventLogToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ExplainRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var goalRaw = reader.OptionalString("investigation_goal");
            var goal = string.IsNullOrWhiteSpace(goalRaw)
                ? "generic"
                : EventLogNamedEventsQueryShared.ToSnakeCase(goalRaw);
            if (!InvestigationGoalNames.Contains(goal, StringComparer.OrdinalIgnoreCase)) {
                return ToolRequestBindingResult<ExplainRequest>.Failure(
                    $"investigation_goal ('{goalRaw}') is not recognized. Allowed values: {string.Join(", ", InvestigationGoalNames)}.");
            }

            var correlationKeysPresent = reader.DistinctStringArray("correlation_keys_present")
                .Select(EventLogNamedEventsQueryShared.ToSnakeCase)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var i = 0; i < correlationKeysPresent.Length; i++) {
                if (!CorrelationKeyNames.Contains(correlationKeysPresent[i], StringComparer.OrdinalIgnoreCase)) {
                    return ToolRequestBindingResult<ExplainRequest>.Failure(
                        $"correlation_keys_present[{i}] ('{correlationKeysPresent[i]}') is not recognized. Allowed values: {string.Join(", ", CorrelationKeyNames)}.");
                }
            }

            var timelineCount = reader.CappedInt32("timeline_count", 0, 0, 50_000);
            var groupsCount = reader.CappedInt32("groups_count", 0, 0, 20_000);
            var filteredUncorrelated = reader.CappedInt32("filtered_uncorrelated", 0, 0, 50_000);
            var preferProfile = reader.Boolean("prefer_profile", defaultValue: true);
            var includeAdEnrichment = reader.Boolean("include_ad_enrichment", defaultValue: true);
            var includePayload = reader.Boolean("include_payload", defaultValue: false);

            return ToolRequestBindingResult<ExplainRequest>.Success(new ExplainRequest(
                InvestigationGoal: goal,
                CorrelationKeysPresent: correlationKeysPresent,
                TimelineCount: timelineCount,
                GroupsCount: groupsCount,
                FilteredUncorrelated: filteredUncorrelated,
                PreferProfile: preferProfile,
                IncludeAdEnrichment: includeAdEnrichment,
                IncludePayload: includePayload));
        });
    }

    private static Task<string> ExecuteAsync(ToolPipelineContext<ExplainRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var notes = new List<string>();

        var recommendedProfile = ResolveProfileForGoal(request.InvestigationGoal);
        var profileKeyList = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(recommendedProfile)) {
            NamedEventsTimelineCorrelationProfiles.TryResolve(
                recommendedProfile,
                out _,
                out var profileKeys,
                out _);
            profileKeyList = (profileKeys ?? Array.Empty<string>()).ToArray();
        }

        var recommendedKeys = ResolveCorrelationKeys(
            goal: request.InvestigationGoal,
            profileKeys: profileKeyList,
            observedKeys: request.CorrelationKeysPresent,
            notes: notes);

        var useProfile = request.PreferProfile &&
                         !string.IsNullOrWhiteSpace(recommendedProfile) &&
                         profileKeyList.SequenceEqual(recommendedKeys, StringComparer.OrdinalIgnoreCase);

        var includeUncorrelated = ResolveIncludeUncorrelated(
            groupsCount: request.GroupsCount,
            filteredUncorrelated: request.FilteredUncorrelated);
        var bucketMinutes = ResolveBucketMinutes(request.TimelineCount);
        var maxGroups = ResolveMaxGroups(request.GroupsCount);

        var payloadKeys = request.IncludePayload
            ? DefaultPayloadKeys
            : Array.Empty<string>();
        if (!request.IncludePayload) {
            notes.Add("Set include_payload=true only when you need deeper entity extraction from payload fields.");
        }
        if (request.FilteredUncorrelated > 0 && !includeUncorrelated) {
            notes.Add("Filtered uncorrelated rows are high; disable include_uncorrelated only when you need tighter groups.");
        }

        var followUpTools = BuildFollowUpTools(includeAdEnrichment: request.IncludeAdEnrichment);
        if (!useProfile && !string.IsNullOrWhiteSpace(recommendedProfile)) {
            notes.Add("Use explicit correlation_keys for now; keep correlation_profile as a reusable target once key coverage improves.");
        }

        var recommendation = new TimelineQueryRecommendation(
            UseCorrelationProfile: useProfile,
            CorrelationProfile: recommendedProfile,
            CorrelationKeys: recommendedKeys,
            IncludeUncorrelated: includeUncorrelated,
            BucketMinutes: bucketMinutes,
            MaxGroups: maxGroups,
            IncludePayload: request.IncludePayload,
            PayloadKeys: payloadKeys);

        var result = new TimelineExplainResult(
            InvestigationGoal: request.InvestigationGoal,
            TimelineQuery: recommendation,
            FollowUpTools: followUpTools,
            Notes: notes);

        var summary = ToolMarkdown.SummaryText(
            title: "Timeline Guidance",
            "Use `timeline_query` recommendation to re-run `eventlog_timeline_query`, then continue with follow-up tools.");
        return Task.FromResult(ToolResultV2.OkModel(model: result, summaryMarkdown: summary));
    }

    private static string ResolveProfileForGoal(string goal) {
        return goal switch {
            "identity" => "identity",
            "actor_activity" => "actor_activity",
            "object_activity" => "object_activity",
            "host_activity" => "host_activity",
            "rule_activity" => "rule_activity",
            _ => "identity"
        };
    }

    private static IReadOnlyList<string> ResolveCorrelationKeys(
        string goal,
        IReadOnlyList<string> profileKeys,
        IReadOnlyList<string> observedKeys,
        IList<string> notes) {
        if (observedKeys.Count == 0) {
            return profileKeys.Count > 0
                ? profileKeys
                : NamedEventsTimelineQueryExecutor.DefaultCorrelationKeys.ToArray();
        }

        var observedSet = observedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inProfile = profileKeys
            .Where(observedSet.Contains)
            .ToArray();
        if (inProfile.Length >= 2) {
            return inProfile;
        }

        notes.Add("Observed key coverage is sparse for the selected goal; using explicit observed keys for this pass.");
        var prioritizedObserved = KeyPriority
            .Where(observedSet.Contains)
            .ToArray();
        if (prioritizedObserved.Length > 0) {
            return prioritizedObserved;
        }

        notes.Add($"No known observed keys were supplied for goal '{goal}'; using default correlation keys.");
        return NamedEventsTimelineQueryExecutor.DefaultCorrelationKeys.ToArray();
    }

    private static bool ResolveIncludeUncorrelated(int groupsCount, int filteredUncorrelated) {
        if (groupsCount == 0 && filteredUncorrelated > 0) {
            return true;
        }

        if (groupsCount >= 300 && filteredUncorrelated == 0) {
            return false;
        }

        return true;
    }

    private static int ResolveBucketMinutes(int timelineCount) {
        if (timelineCount > 1500) {
            return 60;
        }

        if (timelineCount > 600) {
            return 30;
        }

        if (timelineCount > 200) {
            return 15;
        }

        return 5;
    }

    private static int ResolveMaxGroups(int groupsCount) {
        if (groupsCount <= 0) {
            return 250;
        }

        return Math.Min(1000, Math.Max(50, groupsCount + 50));
    }

    private static IReadOnlyList<string> BuildFollowUpTools(bool includeAdEnrichment) {
        var tools = new List<string> {
            "eventlog_timeline_query",
            "eventlog_named_events_query",
            "eventlog_evtx_query"
        };
        if (includeAdEnrichment) {
            tools.Add("ad_environment_discover");
            tools.Add("ad_search");
        }
        return tools;
    }

    private static string[] BuildInvestigationGoalNames() {
        var goals = new List<string> { "generic" };
        goals.AddRange(NamedEventsTimelineCorrelationProfiles.Names);
        return goals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
