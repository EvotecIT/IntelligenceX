using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies UI activity and timeline label mapping for tool-round lifecycle statuses.
/// </summary>
public sealed class MainWindowActivityTimelineMappingTests {
    private static readonly MethodInfo FormatActivityTextMethod = typeof(MainWindow).GetMethod(
                                                                      "FormatActivityText",
                                                                      BindingFlags.NonPublic | BindingFlags.Instance)
                                                                  ?? throw new InvalidOperationException("FormatActivityText not found.");

    private static readonly MethodInfo BuildActivityTimelineLabelMethod = typeof(MainWindow).GetMethod(
                                                                              "BuildActivityTimelineLabel",
                                                                              BindingFlags.NonPublic | BindingFlags.Instance)
                                                                          ?? throw new InvalidOperationException("BuildActivityTimelineLabel not found.");

    /// <summary>
    /// Ensures tool-round statuses map to stable, user-facing activity text and timeline labels.
    /// </summary>
    [Theory]
    [InlineData("tool_round_started", "Starting tool round...", "round start")]
    [InlineData("tool_round_completed", "Tool round complete", "round complete")]
    [InlineData("tool_round_limit_reached", "Tool round limit reached for this turn", "round limit")]
    [InlineData("tool_round_cap_applied", "Applied safe tool-round cap for this turn", "round cap")]
    [InlineData("background_work_queued", "Queued background follow-up work...", "background queued")]
    [InlineData("background_work_ready", "Background follow-up work is ready...", "background ready")]
    [InlineData("background_work_running", "Background follow-up work started...", "background running")]
    [InlineData("background_work_completed", "Background follow-up work completed", "background completed")]
    public void ToolRoundStatuses_MapToExpectedActivityAndTimelineLabels(string status, string expectedActivity, string expectedTimeline) {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var message = CreateStatus(status);

        var activityText = InvokeFormatActivityText(window, message);
        var timelineLabel = InvokeBuildActivityTimelineLabel(window, message, activityText);

        Assert.Equal(expectedActivity, activityText);
        Assert.Equal(expectedTimeline, timelineLabel);
    }

    /// <summary>
    /// Ensures timeline labels remain stable even when service messages override activity text.
    /// </summary>
    [Fact]
    public void BuildActivityTimelineLabel_UsesStableRoundLabelWhenCustomStatusMessageOverridesActivityText() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var message = CreateStatus("tool_round_started", "Custom round startup text");

        var activityText = InvokeFormatActivityText(window, message);
        var timelineLabel = InvokeBuildActivityTimelineLabel(window, message, activityText);

        Assert.Equal("Custom round startup text", activityText);
        Assert.Equal("round start", timelineLabel);
    }

    /// <summary>
    /// Ensures routing metadata labels include strategy and selected/total counts for live diagnostics.
    /// </summary>
    [Fact]
    public void BuildActivityTimelineLabel_UsesRoutingMetaStrategyAndCountsWhenPayloadIsStructured() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var message = CreateStatus(
            status: ChatStatusCodes.RoutingMeta,
            message: "{\"strategy\":\"weighted_subset\",\"selectedToolCount\":3,\"totalToolCount\":17}");

        var activityText = InvokeFormatActivityText(window, message);
        var timelineLabel = InvokeBuildActivityTimelineLabel(window, message, activityText);

        Assert.Equal("Routing strategy weighted subset (3/17 tools)", activityText);
        Assert.Equal("route weighted subset (3/17)", timelineLabel);
    }

    /// <summary>
    /// Ensures prompt-exposure routing metadata surfaces the top ordered tools in activity text and timeline labels.
    /// </summary>
    [Fact]
    public void BuildActivityTimelineLabel_UsesPromptExposureTopToolsWhenPayloadIncludesThem() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var message = CreateStatus(
            status: ChatStatusCodes.RoutingMeta,
            message: """
                     {"strategy":"prompt_review","selectedToolCount":3,"totalToolCount":3,"promptExposure":{"reordered":true,"topToolNames":["eventlog_live_query","eventlog_connectivity_probe","system_pack_info"]}}
                     """);

        var activityText = InvokeFormatActivityText(window, message);
        var timelineLabel = InvokeBuildActivityTimelineLabel(window, message, activityText);

        Assert.Equal("Routing strategy prompt review (3/3 tools) -> eventlog_live_query, eventlog_connectivity_probe, +1", activityText);
        Assert.Contains("route prompt review (3/3)", timelineLabel, StringComparison.Ordinal);
        Assert.Contains("eventlog_live", timelineLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures malformed routing metadata falls back to a stable generic timeline label.
    /// </summary>
    [Fact]
    public void BuildActivityTimelineLabel_UsesRoutingMetaFallbackWhenPayloadIsMalformed() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var message = CreateStatus(
            status: ChatStatusCodes.RoutingMeta,
            message: "not-json");

        var activityText = InvokeFormatActivityText(window, message);
        var timelineLabel = InvokeBuildActivityTimelineLabel(window, message, activityText);

        Assert.Equal("Routing strategy updated...", activityText);
        Assert.Equal("route strategy", timelineLabel);
    }

    private static ChatStatusMessage CreateStatus(string status, string? message = null) {
        return new ChatStatusMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "req-activity-rounds",
            ThreadId = "thread-activity-rounds",
            Status = status,
            Message = message
        };
    }

    private static string InvokeFormatActivityText(MainWindow window, ChatStatusMessage status) {
        try {
            var result = FormatActivityTextMethod.Invoke(window, new object?[] { status });
            return Assert.IsType<string>(result);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static string InvokeBuildActivityTimelineLabel(MainWindow window, ChatStatusMessage status, string activityText) {
        try {
            var result = BuildActivityTimelineLabelMethod.Invoke(window, new object?[] { status, activityText });
            return Assert.IsType<string>(result);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }
}
