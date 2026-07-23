using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Describes the provider identity that desktop prompts may report to the assistant.
/// </summary>
internal sealed record DesktopRuntimeIdentity(string TransportLabel, string ModelLabel);

/// <summary>
/// Resolves provider facts from the layer that currently owns runtime authority.
/// </summary>
internal static class DesktopRuntimeIdentityResolver {
    /// <summary>
    /// Uses app settings for app-owned overrides and service facts for load-only profiles.
    /// An explicit per-conversation model remains authoritative in either mode.
    /// </summary>
    public static DesktopRuntimeIdentity Resolve(
        bool appRuntimeOverridesActive,
        string? appTransport,
        string? requestModel,
        SessionPolicyDto? servicePolicy) {
        var serviceIdentity = servicePolicy?.RuntimeIdentity;
        var transportSource = appRuntimeOverridesActive
            ? appTransport
            : serviceIdentity?.Transport;
        var modelSource = requestModel;
        if (!appRuntimeOverridesActive && string.IsNullOrWhiteSpace(modelSource)) {
            modelSource = serviceIdentity?.Model;
        }

        var modelLabel = (modelSource ?? string.Empty).Trim();
        if (modelLabel.Length == 0) {
            modelLabel = appRuntimeOverridesActive
                ? "(provider default)"
                : "(service runtime model unavailable)";
        }

        return new DesktopRuntimeIdentity(
            NormalizeTransportLabel(transportSource),
            modelLabel);
    }

    private static string NormalizeTransportLabel(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "native" => "native",
            "appserver" or "app-server" => "appserver",
            "compatible-http" or "compatiblehttp" or "http" => "compatible-http",
            "copilot-cli" or "copilotcli" => "copilot-cli",
            _ => "unknown"
        };
    }
}
