using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists Active Directory site links with optional filters and schedule expansion (read-only).
/// </summary>
public sealed class AdSiteLinksTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, SiteLinkOptionFlags> SiteLinkOptionsByName =
        new Dictionary<string, SiteLinkOptionFlags>(StringComparer.OrdinalIgnoreCase) {
            ["use_notify"] = SiteLinkOptionFlags.UseNotify,
            ["two_way_sync"] = SiteLinkOptionFlags.TwoWaySync,
            ["disable_compression"] = SiteLinkOptionFlags.DisableCompression
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_site_links",
        "List Active Directory site links with cost/options/schedule details or return aggregate summary (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name (defaults to current forest).")),
                ("summary", ToolSchema.Boolean("When true, returns aggregate site link counts/cost stats.")),
                ("has_schedule", ToolSchema.Boolean("When true, includes only links that have replication schedules.")),
                ("options_all", ToolSchema.Array(ToolSchema.String().Enum("use_notify", "two_way_sync", "disable_compression"), "Optional option flags that must all be present on a site link.")),
                ("expand_schedule", ToolSchema.Boolean("When true, emits one row per link/day with allowed hours (raw mode only).")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdSiteLinksResult(
        string? ForestName,
        bool HasSchedule,
        IReadOnlyList<string> OptionsAll,
        bool ExpandSchedule,
        int Scanned,
        bool Truncated,
        IReadOnlyList<SiteLinkInfoEx> SiteLinks,
        IReadOnlyList<SiteLinkScheduleRow> ScheduleRows);

    private sealed record AdSiteLinksSummaryResult(
        string? ForestName,
        SiteLinksSummary Summary);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSiteLinksTool"/> class.
    /// </summary>
    public AdSiteLinksTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var summary = ToolArgs.GetBoolean(arguments, "summary", defaultValue: false);

        if (summary) {
            SiteLinksSummary summaryModel;
            try {
                summaryModel = TopologyService.GetSiteLinksSummary(forestName);
            } catch (Exception ex) {
                return Task.FromResult(ErrorFromException(
                    ex,
                    defaultMessage: "Site links summary query failed.",
                    invalidOperationErrorCode: "query_failed"));
            }

            var summaryResult = new AdSiteLinksSummaryResult(
                ForestName: forestName,
                Summary: summaryModel);

            return Task.FromResult(BuildAutoTableResponse(
                arguments: arguments,
                model: summaryResult,
                sourceRows: new[] { summaryModel },
                viewRowsPath: "summary_view",
                title: "Active Directory: Site Links Summary (preview)",
                maxTop: MaxViewTop,
                baseTruncated: false,
                scanned: summaryModel.Total,
                metaMutate: meta => {
                    meta.Add("mode", "summary");
                    if (!string.IsNullOrWhiteSpace(forestName)) {
                        meta.Add("forest_name", forestName);
                    }
                }));
        }

        var hasSchedule = ToolArgs.GetBoolean(arguments, "has_schedule", defaultValue: false);
        var expandSchedule = ToolArgs.GetBoolean(arguments, "expand_schedule", defaultValue: false);
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        if (!TryParseRequiredOptions(arguments, out var requiredOptions, out var optionNames, out var optionError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", optionError ?? "Invalid options_all value."));
        }

        IReadOnlyList<SiteLinkInfoEx> links;
        try {
            links = TopologyService.GetSiteLinks(
                forestName: forestName,
                hasScheduleOnly: hasSchedule,
                requiredOptions: requiredOptions);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "Site links query failed.",
                invalidOperationErrorCode: "query_failed"));
        }

        if (expandSchedule) {
            var (scheduleRows, scannedScheduleRows, truncatedScheduleRows) = ExpandScheduleRowsCapped(
                links,
                maxResults,
                cancellationToken);

            var scheduleResult = new AdSiteLinksResult(
                ForestName: forestName,
                HasSchedule: hasSchedule,
                OptionsAll: optionNames,
                ExpandSchedule: true,
                Scanned: scannedScheduleRows,
                Truncated: truncatedScheduleRows,
                SiteLinks: links,
                ScheduleRows: scheduleRows);

            return Task.FromResult(BuildAutoTableResponse(
                arguments: arguments,
                model: scheduleResult,
                sourceRows: scheduleRows,
                viewRowsPath: "schedule_view",
                title: "Active Directory: Site Link Schedules (preview)",
                maxTop: MaxViewTop,
                baseTruncated: truncatedScheduleRows,
                scanned: scannedScheduleRows,
                metaMutate: meta => {
                    meta.Add("mode", "schedule");
                    AddMaxResultsMeta(meta, maxResults);
                    meta.Add("has_schedule", hasSchedule);
                    meta.Add("expand_schedule", true);
                    meta.Add("options_all", ToolJson.ToJsonArray(optionNames));
                    if (!string.IsNullOrWhiteSpace(forestName)) {
                        meta.Add("forest_name", forestName);
                    }
                }));
        }

        var rows = CapRows(links, maxResults, out var scanned, out var truncated);

        var result = new AdSiteLinksResult(
            ForestName: forestName,
            HasSchedule: hasSchedule,
            OptionsAll: optionNames,
            ExpandSchedule: false,
            Scanned: scanned,
            Truncated: truncated,
            SiteLinks: rows,
            ScheduleRows: Array.Empty<SiteLinkScheduleRow>());

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "site_links_view",
            title: "Active Directory: Site Links (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("has_schedule", hasSchedule);
                meta.Add("expand_schedule", false);
                meta.Add("options_all", ToolJson.ToJsonArray(optionNames));
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            }));
    }

    private static bool TryParseRequiredOptions(
        JsonObject? arguments,
        out SiteLinkOptionFlags[] requiredOptions,
        out IReadOnlyList<string> optionNames,
        out string? error) {
        var optionValues = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("options_all"));
        if (optionValues.Count == 0) {
            requiredOptions = Array.Empty<SiteLinkOptionFlags>();
            optionNames = Array.Empty<string>();
            error = null;
            return true;
        }

        var parsed = new List<SiteLinkOptionFlags>(optionValues.Count);
        var names = new List<string>(optionValues.Count);
        foreach (var optionValue in optionValues) {
            var normalized = NormalizeOptionName(optionValue);
            if (!SiteLinkOptionsByName.TryGetValue(normalized, out var parsedOption)) {
                requiredOptions = Array.Empty<SiteLinkOptionFlags>();
                optionNames = Array.Empty<string>();
                error = $"options_all contains unsupported value '{optionValue}'. Supported values: {string.Join(", ", SiteLinkOptionsByName.Keys.OrderBy(static x => x, StringComparer.Ordinal))}.";
                return false;
            }

            parsed.Add(parsedOption);
            names.Add(normalized);
        }

        requiredOptions = parsed.ToArray();
        optionNames = names;
        error = null;
        return true;
    }

    private static string NormalizeOptionName(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return value.Trim()
            .ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static (IReadOnlyList<SiteLinkScheduleRow> Rows, int Scanned, bool Truncated) ExpandScheduleRowsCapped(
        IReadOnlyList<SiteLinkInfoEx> links,
        int maxResults,
        CancellationToken cancellationToken) {
        var rows = new List<SiteLinkScheduleRow>(Math.Min(Math.Max(maxResults, 1), 512));
        var scanned = 0;
        var truncated = false;

        foreach (var link in links) {
            if (!link.HasSchedule || link.AllowedHoursGrid is null || link.AllowedHoursGrid.Count == 0) {
                continue;
            }

            for (var day = 0; day < link.AllowedHoursGrid.Count; day++) {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                if (rows.Count >= maxResults) {
                    truncated = true;
                    continue;
                }

                List<int>? allowedHours = null;
                var dayGrid = link.AllowedHoursGrid[day];
                for (var hour = 0; hour < dayGrid.Count; hour++) {
                    if (dayGrid[hour]) {
                        allowedHours ??= new List<int>(24);
                        allowedHours.Add(hour);
                    }
                }

                rows.Add(new SiteLinkScheduleRow {
                    Name = link.Name,
                    Day = DayOfWeekName(day),
                    AllowedHours = allowedHours ?? new List<int>()
                });
            }
        }

        return (rows, scanned, truncated);
    }

    private static string DayOfWeekName(int dayIndex) {
        return dayIndex switch {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => dayIndex.ToString()
        };
    }
}
