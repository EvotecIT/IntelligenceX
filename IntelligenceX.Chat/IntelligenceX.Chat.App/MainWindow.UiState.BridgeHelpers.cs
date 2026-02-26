using System;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    internal static bool IsBridgeCompatiblePreset(string? compatiblePreset) {
        var normalized = (compatiblePreset ?? string.Empty).Trim();
        return string.Equals(normalized, "anthropic-bridge", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "gemini-bridge", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBridgeAuthFailureWarning(string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < BridgeAuthFailureWarningTokens.Length; i++) {
            if (normalized.Contains(BridgeAuthFailureWarningTokens[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    internal static string ResolveBridgeSessionState(bool applyInFlight, string? warning, int discoveredModels) {
        if (applyInFlight) {
            return "connecting";
        }

        if (IsBridgeAuthFailureWarning(warning)) {
            return "auth-failed";
        }

        return discoveredModels > 0 ? "ready" : "connecting";
    }

    internal static string ResolveBridgeSessionDetail(
        string? state,
        string? bridgeAccountIdentity,
        string? warning) {
        var normalizedState = (state ?? string.Empty).Trim();
        var normalizedIdentity = (bridgeAccountIdentity ?? string.Empty).Trim();
        var normalizedWarning = (warning ?? string.Empty).Trim();

        if (string.Equals(normalizedState, "ready", StringComparison.OrdinalIgnoreCase)) {
            return normalizedIdentity.Length == 0
                ? "Bridge session ready."
                : "Bridge session ready for " + normalizedIdentity + ".";
        }

        if (string.Equals(normalizedState, "auth-failed", StringComparison.OrdinalIgnoreCase)) {
            return normalizedWarning.Length == 0
                ? "Bridge authentication failed. Update login/email + secret/token and apply again."
                : normalizedWarning;
        }

        return normalizedIdentity.Length == 0
            ? "Connecting to bridge runtime..."
            : "Connecting to bridge runtime for " + normalizedIdentity + "...";
    }

    private static string ResolveBridgeAccountIdentity(
        string? openAIAccountId,
        string? openAIAuthMode,
        string? openAIBasicUsername) {
        var normalizedAccountId = (openAIAccountId ?? string.Empty).Trim();
        if (normalizedAccountId.Length > 0) {
            return normalizedAccountId;
        }

        var normalizedAuthMode = (openAIAuthMode ?? string.Empty).Trim();
        if (!string.Equals(normalizedAuthMode, "basic", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return (openAIBasicUsername ?? string.Empty).Trim();
    }

    private (int TrackedAccounts, int AccountsWithRetrySignals) GetRuntimeUsageCapabilityCounts() {
        lock (_turnDiagnosticsSync) {
            if (_accountUsageByKey.Count == 0) {
                return (0, 0);
            }

            var tracked = 0;
            var retrySignals = 0;
            foreach (var snapshot in _accountUsageByKey.Values) {
                tracked++;
                if (snapshot.UsageLimitRetryAfterUtc.HasValue
                    || snapshot.RateLimitWindowResetUtc.HasValue
                    || snapshot.RateLimitReached == true) {
                    retrySignals++;
                }
            }

            return (tracked, retrySignals);
        }
    }

    private static string ResolveRuntimeProviderLabelForState(
        string transport,
        string compatiblePreset,
        bool copilotConnected,
        string baseUrl) {
        if (string.Equals(transport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return "GitHub Copilot subscription runtime";
        }

        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return "ChatGPT runtime (OpenAI native)";
        }

        if (string.Equals(compatiblePreset, "lmstudio", StringComparison.OrdinalIgnoreCase)) {
            return "LM Studio runtime";
        }

        if (string.Equals(compatiblePreset, "ollama", StringComparison.OrdinalIgnoreCase)) {
            return "Ollama runtime";
        }

        if (string.Equals(compatiblePreset, "openai", StringComparison.OrdinalIgnoreCase)) {
            return "OpenAI API runtime";
        }

        if (string.Equals(compatiblePreset, "azure-openai", StringComparison.OrdinalIgnoreCase)) {
            return "Azure OpenAI runtime";
        }

        if (string.Equals(compatiblePreset, "anthropic-bridge", StringComparison.OrdinalIgnoreCase)) {
            return "Anthropic subscription bridge runtime";
        }

        if (string.Equals(compatiblePreset, "gemini-bridge", StringComparison.OrdinalIgnoreCase)) {
            return "Gemini subscription bridge runtime";
        }

        if (copilotConnected) {
            return "GitHub Copilot runtime";
        }

        return baseUrl.Length == 0
            ? "Compatible HTTP runtime"
            : "Compatible HTTP runtime (" + DescribeRuntimeFromBaseUrl(baseUrl) + ")";
    }

    internal static bool ShouldShowToolsLoading(
        bool isConnected,
        bool hasSessionPolicy,
        int startupFlowState,
        bool startupMetadataSyncQueued) {
        if (!isConnected || hasSessionPolicy) {
            return false;
        }

        return startupMetadataSyncQueued || startupFlowState == StartupFlowStateRunning;
    }
}
