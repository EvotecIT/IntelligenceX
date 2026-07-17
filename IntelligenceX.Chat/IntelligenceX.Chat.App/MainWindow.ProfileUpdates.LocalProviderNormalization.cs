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
using IntelligenceX.Chat.App.Launch;
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
        return CompatibleProviderEndpointPolicy.DetectPreset(baseUrl);
    }

    internal static string? ResolveChatRequestModelOverride(string? transport, string? baseUrl, string? configuredModel,
        IReadOnlyList<ModelInfoDto>? availableModels) =>
        ChatRequestModelResolver.Resolve(transport, baseUrl, configuredModel, availableModels);

    private static bool IsLocalCompatibleRuntimePreset(string preset) {
        return CompatibleProviderEndpointPolicy.IsLocalRuntimePreset(preset);
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

    private static string NormalizeLocalProviderImageGenerationQuality(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => "auto",
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderImageGenerationSize(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => "auto",
            "1024x1024" => "1024x1024",
            "1024x1536" => "1024x1536",
            "1536x1024" => "1536x1024",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderImageGenerationOutputFormat(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "jpeg" => "jpeg",
            "jpg" => "jpeg",
            "png" => "png",
            "webp" => "webp",
            _ => string.Empty
        };
    }

    private static int? NormalizeLocalProviderImageGenerationOutputCompression(int? value) {
        if (!value.HasValue) {
            return null;
        }

        return value.Value >= 0 && value.Value <= 100 ? value.Value : null;
    }

    private static int? NormalizeLocalProviderImageGenerationOutputCompression(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? NormalizeLocalProviderImageGenerationOutputCompression(parsed)
            : null;
    }

    private static string NormalizeLocalProviderImageGenerationBackground(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => "auto",
            "transparent" => "transparent",
            "opaque" => "opaque",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderImageGenerationOutputDirectory(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
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
        return ChatRequestOptionsFactory.ResolveParallelToolMode(overrideParallelTools);
    }

}
