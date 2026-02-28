using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

internal static class SystemRenderHintBuilders {
    internal static JsonValue? BuildWarningListHints(int warningCount, int priority = 300) {
        if (warningCount <= 0) {
            return null;
        }

        var hints = new JsonArray()
            .Add(ToolOutputHints.RenderTable(
                    "warnings",
                    new ToolColumn("value", "Warning", "string"))
                .Add("priority", priority));
        return JsonValue.From(hints);
    }

    internal static JsonValue? BuildSmbPostureHints(
        int warningCount,
        int nullSessionShareCount,
        int nullSessionPipeCount) {
        var hints = new JsonArray();

        if (warningCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "warnings",
                    new ToolColumn("value", "Warning", "string"))
                .Add("priority", 400));
        }

        if (nullSessionShareCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "configuration/null_session_shares",
                    new ToolColumn("value", "Null session share", "string"))
                .Add("priority", 300));
        }

        if (nullSessionPipeCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "configuration/null_session_pipes",
                    new ToolColumn("value", "Null session pipe", "string"))
                .Add("priority", 200));
        }

        if (hints.Count == 0) {
            return null;
        }

        return JsonValue.From(hints);
    }
}
