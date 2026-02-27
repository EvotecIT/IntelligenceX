using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Formats structured tool-run envelopes into transcript markdown.
/// </summary>
internal static partial class ToolRunMarkdownFormatter {
    private const string DataViewPayloadFenceLanguage = "ix-dataview";
    private const string DataViewPayloadKind = "ix_tool_dataview_v1";
    private const string ChartFenceLanguage = "ix-chart";
    private const string NetworkFenceLanguage = "ix-network";

    /// <summary>
    /// Builds markdown for tool calls and outputs.
    /// </summary>
    /// <param name="tools">Tool run payload.</param>
    /// <param name="resolveToolDisplayName">Display-name resolver callback.</param>
    /// <returns>Markdown summary for transcript.</returns>
    public static string Format(ToolRunDto tools, Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);

        var markdown = new MarkdownComposer()
            .Paragraph("**Tool outputs:**")
            .BlankLine();

        var namesByCallId = BuildToolNamesByCallId(tools, resolveToolDisplayName);

        foreach (var output in tools.Outputs) {
            var toolLabel = ResolveToolLabel(namesByCallId, output.CallId);
            var hasError = !string.IsNullOrWhiteSpace(output.Error) || !string.IsNullOrWhiteSpace(output.ErrorCode) || output.Ok == false;

            markdown.Heading(toolLabel, 4);
            if (hasError) {
                AppendFailureDescriptor(markdown, output);
            }

            var renderHintFences = BuildRenderHintFences(output);
            AppendCodeFences(markdown, renderHintFences);

            var summary = NormalizeSummaryMarkdown(output.SummaryMarkdown, toolLabel);
            if (ShouldIncludeSummary(summary, hasError)) {
                markdown.Raw(summary);
            } else if (!hasError && renderHintFences.Count == 0) {
                markdown.Paragraph("completed");
            }

            markdown.BlankLine();
        }

        return markdown.Build();
    }

    /// <summary>
    /// Builds markdown containing first-party visual fences only.
    /// </summary>
    /// <param name="tools">Tool run payload.</param>
    /// <param name="resolveToolDisplayName">Display-name resolver callback.</param>
    /// <returns>Markdown containing visual fences or empty when none are available.</returns>
    public static string FormatVisualsOnly(ToolRunDto tools, Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);

        var markdown = new MarkdownComposer()
            .Paragraph("**Tool visuals:**")
            .BlankLine();

        var namesByCallId = BuildToolNamesByCallId(tools, resolveToolDisplayName);
        var visualGroups = BuildVisualFenceGroupsByCallId(tools.Outputs);
        if (visualGroups.Count == 0) {
            return string.Empty;
        }

        foreach (var visualGroup in visualGroups) {
            var toolLabel = ResolveToolLabel(namesByCallId, visualGroup.CallId);
            markdown.Heading(toolLabel, 4);
            AppendCodeFences(markdown, visualGroup.Fences);
            markdown.BlankLine();
        }

        return markdown.Build();
    }

    private static Dictionary<string, string> BuildToolNamesByCallId(
        ToolRunDto tools,
        Func<string?, string> resolveToolDisplayName) {
        var namesByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in tools.Calls) {
            namesByCallId[call.CallId] = resolveToolDisplayName(call.Name);
        }

        return namesByCallId;
    }

    private static string ResolveToolLabel(IReadOnlyDictionary<string, string> namesByCallId, string? callId) {
        if (callId is not null
            && namesByCallId.TryGetValue(callId, out var name)
            && !string.IsNullOrWhiteSpace(name)) {
            return name;
        }

        return "Call " + callId;
    }

    private static void AppendCodeFences(
        MarkdownComposer markdown,
        IReadOnlyList<(string Language, string Content)> fences) {
        for (var i = 0; i < fences.Count; i++) {
            var fence = fences[i];
            markdown.CodeFence(fence.Language, fence.Content);
        }
    }

    private static List<VisualFenceGroup> BuildVisualFenceGroupsByCallId(IReadOnlyList<ToolOutputDto> outputs) {
        var groups = new List<VisualFenceGroup>();
        var groupsByCallId = new Dictionary<string, VisualFenceGroup>(StringComparer.Ordinal);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var visualFences = ExtractFirstPartyVisualFences(BuildRenderHintFences(output));
            if (visualFences.Count == 0) {
                continue;
            }

            var groupKey = NormalizeCallId(output.CallId);
            if (!groupsByCallId.TryGetValue(groupKey, out var group)) {
                group = new VisualFenceGroup(groupKey);
                groupsByCallId[groupKey] = group;
                groups.Add(group);
            }

            for (var j = 0; j < visualFences.Count; j++) {
                var fence = visualFences[j];
                var dedupeKey = BuildRenderHintDeduplicationKey(fence.Language, fence.Content);
                if (group.SeenFences.Add(dedupeKey)) {
                    group.Fences.Add(fence);
                }
            }
        }

        return groups;
    }

    private static string NormalizeCallId(string? callId) {
        return callId ?? string.Empty;
    }

    private sealed class VisualFenceGroup {
        public VisualFenceGroup(string callId) {
            CallId = callId;
        }

        public string CallId { get; }
        public List<(string Language, string Content)> Fences { get; } = new();
        public HashSet<string> SeenFences { get; } = new(StringComparer.Ordinal);
    }
}
