using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private static int? ParseAutonomyInt(string? raw, int min, int max) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return null;
        }

        if (parsed < min || parsed > max) {
            return null;
        }

        return parsed;
    }

    internal static bool TryNormalizeLocalProviderTransport(string? value, out string transport) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "native":
                transport = TransportNative;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                transport = TransportCompatibleHttp;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                transport = TransportCopilotCli;
                return true;
            default:
                transport = TransportNative;
                return false;
        }
    }

    private static string NormalizeLocalProviderTransport(string? value) {
        return TryNormalizeLocalProviderTransport(value, out var normalized)
            ? normalized
            : TransportNative;
    }

    private static string? NormalizeLocalProviderBaseUrl(string? value, string transport, string? transportHint = null) {
        var normalized = (value ?? string.Empty).Trim();
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (normalized.Length == 0) {
            var hint = (transportHint ?? string.Empty).Trim().ToLowerInvariant();
            if (hint is "lmstudio" or "lm-studio") {
                return DefaultLmStudioBaseUrl;
            }
            return DefaultOllamaBaseUrl;
        }

        return normalized;
    }

    private static string DetectCompatibleProviderPreset(string? baseUrl) {
        var normalized = (baseUrl ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return "manual";
        }

        if (normalized.Contains("127.0.0.1:1234", StringComparison.Ordinal)
            || normalized.Contains("localhost:1234", StringComparison.Ordinal)) {
            return "lmstudio";
        }

        if (normalized.Contains("127.0.0.1:11434", StringComparison.Ordinal)
            || normalized.Contains("localhost:11434", StringComparison.Ordinal)) {
            return "ollama";
        }

        if (normalized.Contains("api.openai.com", StringComparison.Ordinal)) {
            return "openai";
        }

        if (normalized.Contains(".openai.azure.com", StringComparison.Ordinal)) {
            return "azure-openai";
        }

        if (normalized.Contains("anthropic", StringComparison.Ordinal)
            || normalized.Contains("claude", StringComparison.Ordinal)) {
            return "anthropic-bridge";
        }

        if (normalized.Contains("gemini", StringComparison.Ordinal)
            || normalized.Contains("googleapis.com", StringComparison.Ordinal)) {
            return "gemini-bridge";
        }

        return "manual";
    }

    internal static string? ResolveChatRequestModelOverride(string? transport, string? baseUrl, string? configuredModel,
        IReadOnlyList<ModelInfoDto>? availableModels) {
        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        var normalizedConfiguredModel = (configuredModel ?? string.Empty).Trim();
        var localCompatibleRuntime = string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            && IsLocalCompatibleRuntimePreset(DetectCompatibleProviderPreset(baseUrl));
        var supportsCatalogFallback =
            string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase);
        if (!supportsCatalogFallback) {
            return normalizedConfiguredModel.Length == 0 ? null : normalizedConfiguredModel;
        }

        var preferredModel = ResolvePreferredCatalogModel(availableModels);
        if (normalizedConfiguredModel.Length == 0) {
            return preferredModel.Length == 0 ? null : preferredModel;
        }

        if (CatalogContainsModel(availableModels, normalizedConfiguredModel)) {
            return normalizedConfiguredModel;
        }

        if (preferredModel.Length == 0) {
            if (localCompatibleRuntime && IsLikelyCloudHostedModelName(normalizedConfiguredModel)) {
                return null;
            }
            return normalizedConfiguredModel;
        }

        if (localCompatibleRuntime || IsLikelyCloudHostedModelName(normalizedConfiguredModel)) {
            return preferredModel;
        }

        return normalizedConfiguredModel;
    }

    private static bool IsLocalCompatibleRuntimePreset(string preset) {
        return string.Equals(preset, "lmstudio", StringComparison.OrdinalIgnoreCase)
               || string.Equals(preset, "ollama", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyCloudHostedModelName(string? modelName) {
        var normalized = (modelName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.StartsWith("gpt-", StringComparison.Ordinal)
               || string.Equals(normalized, "gpt5", StringComparison.Ordinal)
               || normalized.StartsWith("chatgpt", StringComparison.Ordinal)
               || normalized.StartsWith("o1", StringComparison.Ordinal)
               || normalized.StartsWith("o3", StringComparison.Ordinal)
               || normalized.StartsWith("o4", StringComparison.Ordinal);
    }

    private static bool CatalogContainsModel(IReadOnlyList<ModelInfoDto>? availableModels, string model) {
        if (availableModels is null || availableModels.Count == 0) {
            return false;
        }

        for (var i = 0; i < availableModels.Count; i++) {
            var entry = availableModels[i];
            var candidate = (entry.Model ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                continue;
            }

            if (string.Equals(candidate, model, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string ResolvePreferredCatalogModel(IReadOnlyList<ModelInfoDto>? availableModels) {
        if (availableModels is null || availableModels.Count == 0) {
            return string.Empty;
        }

        var first = string.Empty;
        for (var i = 0; i < availableModels.Count; i++) {
            var entry = availableModels[i];
            var model = (entry.Model ?? string.Empty).Trim();
            if (model.Length == 0) {
                continue;
            }

            if (first.Length == 0) {
                first = model;
            }

            if (entry.IsDefault == true) {
                return model;
            }
        }

        return first;
    }

    private static bool SupportsLocalProviderReasoningControls(string? transport, string? baseUrl) {
        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static string DescribeLocalProviderReasoningSupport(string? transport, string? baseUrl) {
        if (SupportsLocalProviderReasoningControls(transport, baseUrl)) {
            return "enabled (pass-through; provider may clamp unsupported values)";
        }

        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return "not exposed by Copilot subscription runtime";
        }

        return "not exposed by current runtime profile";
    }

    private static string NormalizeLocalProviderModel(string? value, string transport) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 0) {
            return normalized;
        }

        if (string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return DefaultLocalModel;
    }

    private static string? NormalizeLocalProviderApiKey(string? value, string transport) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeLocalProviderOpenAIAuthMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "basic" => "basic",
            "none" => "none",
            "off" => "none",
            "bearer" => "bearer",
            "api-key" => "bearer",
            "apikey" => "bearer",
            "token" => "bearer",
            _ => "bearer"
        };
    }

    private static string NormalizeLocalProviderOpenAIBasicUsername(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    private static string? NormalizeLocalProviderOpenAIBasicPassword(string? value, string transport) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeLocalProviderReasoningEffort(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "minimal" => "minimal",
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "xhigh" => "xhigh",
            "x-high" => "xhigh",
            "x_high" => "xhigh",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderReasoningSummary(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => "auto",
            "concise" => "concise",
            "detailed" => "detailed",
            "off" => "off",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderTextVerbosity(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => string.Empty
        };
    }

    private static double? NormalizeLocalProviderTemperature(double? value) {
        if (!value.HasValue) {
            return null;
        }

        var parsed = value.Value;
        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0d || parsed > 2d) {
            return null;
        }

        return parsed;
    }

    private static double? NormalizeLocalProviderTemperature(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return null;
        }

        return NormalizeLocalProviderTemperature(parsed);
    }

    private static bool? ParseAutonomyParallelMode(string? raw) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return null;
    }

    private static bool? ParseAutonomyParallelToolMode(string? raw) {
        var text = (raw ?? string.Empty).Trim();
        return text.ToLowerInvariant() switch {
            "auto" => null,
            "default" => null,
            "allow_parallel" => true,
            "allow-parallel" => true,
            "allowparallel" => true,
            "on" => true,
            "force_serial" => false,
            "force-serial" => false,
            "forceserial" => false,
            "serial" => false,
            "off" => false,
            _ => null
        };
    }

    private static string ResolveParallelToolMode(bool? overrideParallelTools) {
        return overrideParallelTools switch {
            true => ParallelToolModeAllowParallel,
            false => ParallelToolModeForceSerial,
            _ => ParallelToolModeAuto
        };
    }

    private static bool ResolveParallelToolsForRequest(string parallelToolMode, bool serviceDefaultParallelTools) {
        return parallelToolMode switch {
            ParallelToolModeAllowParallel => true,
            ParallelToolModeForceSerial => false,
            _ => serviceDefaultParallelTools
        };
    }

    private static int? NormalizePositiveTimeout(int? value) {
        if (!value.HasValue || value.Value <= 0) {
            return null;
        }

        return value.Value;
    }

}
