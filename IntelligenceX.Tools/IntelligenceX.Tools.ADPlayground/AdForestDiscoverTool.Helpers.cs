using ADPlayground.Helpers;
using ADPlayground.Monitoring.Probes;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdForestDiscoverTool : ActiveDirectoryToolBase, ITool {
    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
    }

    private static DiscoveryStep ToDiscoveryStep(AdForestDiscoveryService.ForestDiscoveryStep step) {
        return new DiscoveryStep(
            step.Name,
            step.Ok,
            step.DurationMs,
            step.Error,
            step.ErrorType,
            step.Output);
    }

    private sealed class DiscoveryStep {
        public DiscoveryStep(
            string name,
            bool ok,
            int durationMs,
            string? error,
            string? errorType,
            object? output) {
            Name = name;
            Ok = ok;
            DurationMs = durationMs;
            Error = error;
            ErrorType = errorType;
            Output = output;
        }

        public string Name { get; }
        public bool Ok { get; }
        public int DurationMs { get; }
        public object? Output { get; }
        public string? Error { get; }
        public string? ErrorType { get; }
    }
}
