using System.Globalization;

namespace IntelligenceX.Chat.App;

internal static class UiBridgeScripts {
    public static string BuildWheelForwardScript(int delta) {
        return $"window.ixScrollTranscript && window.ixScrollTranscript({delta.ToString(CultureInfo.InvariantCulture)}, 'host');";
    }

    public static string BuildWheelDiagnosticRecordScript(int delta) {
        return $"window.ixWheelDiagRecord && window.ixWheelDiagRecord('host_pointer_wheel', {{delta:{delta.ToString(CultureInfo.InvariantCulture)}}});";
    }

    public static string BuildWheelGlobalDiagnosticRecordScript(int delta) {
        return $"window.ixWheelDiagRecord && window.ixWheelDiagRecord('host_global_wheel', {{delta:{delta.ToString(CultureInfo.InvariantCulture)}}});";
    }
}
