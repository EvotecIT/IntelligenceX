using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Shell-neutral facts used to describe the runtime that will execute the current turn.
/// </summary>
internal sealed record DesktopRuntimeCapabilityContext(
    string TransportLabel,
    string ModelLabel,
    string DetailedToolAvailability,
    string CompactToolAvailability,
    string DetailedExecutionLocality,
    string CompactExecutionLocality,
    IReadOnlyList<string> DetailLines);

/// <summary>
/// Owns runtime self-report line formatting for every desktop shell.
/// </summary>
internal static class DesktopRuntimeCapabilityContextBuilder
{
    public static bool ShouldIncludeExecutionLocality(
        bool compactSelfReport,
        RuntimeSelfReportDetectionSource detectionSource)
    {
        return !compactSelfReport || detectionSource != RuntimeSelfReportDetectionSource.LexicalFallback;
    }

    public static IReadOnlyList<string> Build(
        DesktopRuntimeCapabilityContext context,
        bool compactSelfReport,
        RuntimeSelfReportDetectionSource detectionSource)
    {
        ArgumentNullException.ThrowIfNull(context);
        var lines = new List<string>();
        if (compactSelfReport)
        {
            lines.Add("Active runtime for this turn: " + context.TransportLabel + ", model " + context.ModelLabel + ".");
            lines.Add("Tooling status for this turn: " + context.CompactToolAvailability);
            if (ShouldIncludeExecutionLocality(compactSelfReport, detectionSource))
            {
                lines.Add("Execution locality for enabled tools: " + context.CompactExecutionLocality);
            }
            return lines;
        }

        lines.Add("Runtime transport: " + context.TransportLabel + ", active model for this turn: " + context.ModelLabel);
        lines.Add("Tool availability for this turn: " + context.DetailedToolAvailability);
        lines.Add("Execution locality for enabled tools: " + context.DetailedExecutionLocality);
        for (var index = 0; index < context.DetailLines.Count; index++)
        {
            var line = (context.DetailLines[index] ?? string.Empty).Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
        lines.Add("Assistant rule: when asked about current runtime/model/tools, answer from these runtime lines and do not infer unavailable capabilities.");
        return lines;
    }
}