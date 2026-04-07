using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Chat.App.Theming;

/// <summary>
/// Central registry for chat-shell theme preset CSS variables.
/// </summary>
internal static class ThemeRegistry {
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private static readonly IReadOnlyCollection<string> PresetNamesCache;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Presets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase) {
            ["emerald"] = new Dictionary<string, string> {
                ["--ix-accent"] = "#34d399",
                ["--ix-accent-hover"] = "#6ee7b7",
                ["--ix-accent-gradient"] = "linear-gradient(135deg, #34d399, #059669)",
                ["--ix-bg-primary"] = "#0b1f1a",
                ["--ix-bg-secondary"] = "#103126",
                ["--ix-bg-elevated"] = "#154035",
                ["--ix-text-secondary"] = "#a7dcc9",
                ["--ix-text-muted"] = "#75b9a2",
                ["--ix-border-subtle"] = "rgba(60, 124, 103, 0.5)",
                ["--ix-input-border"] = "#3f806a",
                ["--ix-menu-bg"] = "rgba(15, 48, 39, 0.98)",
                ["--ix-menu-border"] = "#3f806a",
                ["--ix-bubble-assistant-bg"] = "rgba(16, 80, 60, 0.6)",
                ["--ix-bubble-assistant-border"] = "rgba(52, 211, 153, 0.3)",
                ["--ix-bubble-user-bg"] = "rgba(14, 70, 55, 0.6)",
                ["--ix-bubble-user-border"] = "rgba(52, 211, 153, 0.25)",
                ["--ix-status-ok"] = "#34d399",
                ["--ix-input-focus"] = "#34d399",
                ["--ix-user-avatar-bg"] = "#10b981",
                ["--ix-dropdown-bg"] = "#123d33",
                ["--ix-select-item-hover"] = "rgba(52, 211, 153, 0.24)",
                ["--ix-send-btn-text"] = "#07251d",
                ["--ix-send-btn-border"] = "rgba(52, 211, 153, 0.45)",
                ["--ix-send-btn-shadow"] = "0 8px 18px rgba(52, 211, 153, 0.24)",
                ["--ix-sidebar-bg"] = "linear-gradient(180deg, rgba(12, 45, 36, 0.85) 0%, rgba(8, 30, 25, 0.78) 100%)",
                ["--ix-sidebar-item-bg"] = "rgba(10, 42, 33, 0.58)",
                ["--ix-sidebar-item-hover"] = "rgba(52, 211, 153, 0.16)",
                ["--ix-sidebar-item-active"] = "rgba(52, 211, 153, 0.24)",
                ["--ix-sidebar-item-active-border"] = "rgba(52, 211, 153, 0.72)",
                ["--ix-scrollbar-thumb"] = "rgba(52, 211, 153, 0.4)",
                ["--ix-scrollbar-thumb-hover"] = "rgba(52, 211, 153, 0.6)"
            },
            ["rose"] = new Dictionary<string, string> {
                ["--ix-accent"] = "#f472b6",
                ["--ix-accent-hover"] = "#f9a8d4",
                ["--ix-accent-gradient"] = "linear-gradient(135deg, #f472b6, #db2777)",
                ["--ix-bg-primary"] = "#1b0b15",
                ["--ix-bg-secondary"] = "#2b1123",
                ["--ix-bg-elevated"] = "#3b1831",
                ["--ix-text-secondary"] = "#efc1da",
                ["--ix-text-muted"] = "#d18eb6",
                ["--ix-border-subtle"] = "rgba(129, 61, 101, 0.55)",
                ["--ix-input-border"] = "#88507a",
                ["--ix-menu-bg"] = "rgba(49, 18, 40, 0.98)",
                ["--ix-menu-border"] = "#88507a",
                ["--ix-bubble-assistant-bg"] = "rgba(80, 20, 55, 0.6)",
                ["--ix-bubble-assistant-border"] = "rgba(244, 114, 182, 0.3)",
                ["--ix-bubble-user-bg"] = "rgba(70, 16, 48, 0.6)",
                ["--ix-bubble-user-border"] = "rgba(244, 114, 182, 0.25)",
                ["--ix-status-ok"] = "#f472b6",
                ["--ix-input-focus"] = "#f472b6",
                ["--ix-user-avatar-bg"] = "#ec4899",
                ["--ix-dropdown-bg"] = "#3a1730",
                ["--ix-select-item-hover"] = "rgba(244, 114, 182, 0.24)",
                ["--ix-send-btn-text"] = "#2b0c1d",
                ["--ix-send-btn-border"] = "rgba(244, 114, 182, 0.45)",
                ["--ix-send-btn-shadow"] = "0 8px 18px rgba(244, 114, 182, 0.24)",
                ["--ix-sidebar-bg"] = "linear-gradient(180deg, rgba(46, 16, 39, 0.85) 0%, rgba(27, 11, 21, 0.78) 100%)",
                ["--ix-sidebar-item-bg"] = "rgba(58, 21, 48, 0.58)",
                ["--ix-sidebar-item-hover"] = "rgba(244, 114, 182, 0.16)",
                ["--ix-sidebar-item-active"] = "rgba(244, 114, 182, 0.24)",
                ["--ix-sidebar-item-active-border"] = "rgba(244, 114, 182, 0.72)",
                ["--ix-scrollbar-thumb"] = "rgba(244, 114, 182, 0.4)",
                ["--ix-scrollbar-thumb-hover"] = "rgba(244, 114, 182, 0.6)"
            },
            ["cobalt"] = new Dictionary<string, string> {
                ["--ix-accent"] = "#60a5fa",
                ["--ix-accent-hover"] = "#93c5fd",
                ["--ix-accent-gradient"] = "linear-gradient(135deg, #60a5fa, #2563eb)",
                ["--ix-bg-primary"] = "#08162d",
                ["--ix-bg-secondary"] = "#102a4d",
                ["--ix-bg-elevated"] = "#153864",
                ["--ix-text-secondary"] = "#b5d4ff",
                ["--ix-text-muted"] = "#82a8da",
                ["--ix-border-subtle"] = "rgba(74, 116, 184, 0.55)",
                ["--ix-input-border"] = "#436ea8",
                ["--ix-menu-bg"] = "rgba(15, 37, 73, 0.98)",
                ["--ix-menu-border"] = "#436ea8",
                ["--ix-bubble-assistant-bg"] = "rgba(24, 66, 120, 0.62)",
                ["--ix-bubble-assistant-border"] = "rgba(96, 165, 250, 0.34)",
                ["--ix-bubble-user-bg"] = "rgba(20, 57, 103, 0.66)",
                ["--ix-bubble-user-border"] = "rgba(96, 165, 250, 0.3)",
                ["--ix-status-ok"] = "#60a5fa",
                ["--ix-input-focus"] = "#60a5fa",
                ["--ix-user-avatar-bg"] = "#3b82f6",
                ["--ix-dropdown-bg"] = "#173764",
                ["--ix-select-item-hover"] = "rgba(96, 165, 250, 0.24)",
                ["--ix-send-btn-text"] = "#061b36",
                ["--ix-send-btn-border"] = "rgba(96, 165, 250, 0.45)",
                ["--ix-send-btn-shadow"] = "0 8px 18px rgba(96, 165, 250, 0.24)",
                ["--ix-sidebar-bg"] = "linear-gradient(180deg, rgba(13, 42, 82, 0.85) 0%, rgba(8, 23, 45, 0.78) 100%)",
                ["--ix-sidebar-item-bg"] = "rgba(18, 50, 93, 0.58)",
                ["--ix-sidebar-item-hover"] = "rgba(96, 165, 250, 0.16)",
                ["--ix-sidebar-item-active"] = "rgba(96, 165, 250, 0.24)",
                ["--ix-sidebar-item-active-border"] = "rgba(96, 165, 250, 0.72)",
                ["--ix-scrollbar-thumb"] = "rgba(96, 165, 250, 0.42)",
                ["--ix-scrollbar-thumb-hover"] = "rgba(96, 165, 250, 0.62)"
            },
            ["amber"] = new Dictionary<string, string> {
                ["--ix-accent"] = "#fbbf24",
                ["--ix-accent-hover"] = "#fcd34d",
                ["--ix-accent-gradient"] = "linear-gradient(135deg, #fbbf24, #f59e0b)",
                ["--ix-bg-primary"] = "#1f1405",
                ["--ix-bg-secondary"] = "#35240a",
                ["--ix-bg-elevated"] = "#473112",
                ["--ix-text-secondary"] = "#f3d7a0",
                ["--ix-text-muted"] = "#c9a86a",
                ["--ix-border-subtle"] = "rgba(138, 106, 52, 0.56)",
                ["--ix-input-border"] = "#936d30",
                ["--ix-menu-bg"] = "rgba(53, 36, 10, 0.98)",
                ["--ix-menu-border"] = "#936d30",
                ["--ix-bubble-assistant-bg"] = "rgba(92, 63, 21, 0.64)",
                ["--ix-bubble-assistant-border"] = "rgba(251, 191, 36, 0.35)",
                ["--ix-bubble-user-bg"] = "rgba(73, 51, 18, 0.66)",
                ["--ix-bubble-user-border"] = "rgba(251, 191, 36, 0.32)",
                ["--ix-status-ok"] = "#fbbf24",
                ["--ix-input-focus"] = "#fbbf24",
                ["--ix-user-avatar-bg"] = "#f59e0b",
                ["--ix-dropdown-bg"] = "#3a280b",
                ["--ix-select-item-hover"] = "rgba(251, 191, 36, 0.24)",
                ["--ix-send-btn-text"] = "#2f1c03",
                ["--ix-send-btn-border"] = "rgba(251, 191, 36, 0.45)",
                ["--ix-send-btn-shadow"] = "0 8px 18px rgba(251, 191, 36, 0.24)",
                ["--ix-sidebar-bg"] = "linear-gradient(180deg, rgba(58, 39, 11, 0.85) 0%, rgba(31, 20, 5, 0.78) 100%)",
                ["--ix-sidebar-item-bg"] = "rgba(60, 42, 14, 0.58)",
                ["--ix-sidebar-item-hover"] = "rgba(251, 191, 36, 0.16)",
                ["--ix-sidebar-item-active"] = "rgba(251, 191, 36, 0.24)",
                ["--ix-sidebar-item-active-border"] = "rgba(251, 191, 36, 0.72)",
                ["--ix-scrollbar-thumb"] = "rgba(251, 191, 36, 0.42)",
                ["--ix-scrollbar-thumb-hover"] = "rgba(251, 191, 36, 0.62)"
            },
            ["graphite"] = new Dictionary<string, string> {
                ["--ix-accent"] = "#d1d5db",
                ["--ix-accent-hover"] = "#e5e7eb",
                ["--ix-accent-gradient"] = "linear-gradient(135deg, #d1d5db, #6b7280)",
                ["--ix-bg-primary"] = "#0d1015",
                ["--ix-bg-secondary"] = "#151a21",
                ["--ix-bg-elevated"] = "#1f2630",
                ["--ix-text-secondary"] = "#d2d8e2",
                ["--ix-text-muted"] = "#98a2b3",
                ["--ix-border-subtle"] = "rgba(124, 132, 145, 0.48)",
                ["--ix-input-border"] = "#7b8797",
                ["--ix-menu-bg"] = "rgba(23, 28, 36, 0.98)",
                ["--ix-menu-border"] = "#7b8797",
                ["--ix-bubble-assistant-bg"] = "rgba(39, 45, 55, 0.72)",
                ["--ix-bubble-assistant-border"] = "rgba(171, 181, 195, 0.3)",
                ["--ix-bubble-user-bg"] = "rgba(47, 54, 65, 0.78)",
                ["--ix-bubble-user-border"] = "rgba(171, 181, 195, 0.28)",
                ["--ix-status-ok"] = "#cbd5e1",
                ["--ix-input-focus"] = "#d1d5db",
                ["--ix-user-avatar-bg"] = "#6b7280",
                ["--ix-dropdown-bg"] = "#202732",
                ["--ix-select-item-hover"] = "rgba(209, 213, 219, 0.18)",
                ["--ix-send-btn-text"] = "#0f1319",
                ["--ix-send-btn-border"] = "rgba(209, 213, 219, 0.42)",
                ["--ix-send-btn-shadow"] = "0 8px 18px rgba(156, 163, 175, 0.24)",
                ["--ix-sidebar-bg"] = "linear-gradient(180deg, rgba(24, 29, 36, 0.92) 0%, rgba(12, 16, 21, 0.88) 100%)",
                ["--ix-sidebar-item-bg"] = "rgba(32, 38, 46, 0.66)",
                ["--ix-sidebar-item-hover"] = "rgba(209, 213, 219, 0.12)",
                ["--ix-sidebar-item-active"] = "rgba(209, 213, 219, 0.18)",
                ["--ix-sidebar-item-active-border"] = "rgba(209, 213, 219, 0.62)",
                ["--ix-scrollbar-thumb"] = "rgba(209, 213, 219, 0.34)",
                ["--ix-scrollbar-thumb-hover"] = "rgba(209, 213, 219, 0.5)"
            }
        };

    static ThemeRegistry() {
        PresetNamesCache = Presets.Keys
            .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Known non-default theme preset names.
    /// </summary>
    public static IReadOnlyCollection<string> PresetNames => PresetNamesCache;

    /// <summary>
    /// Attempts to resolve CSS variables for a preset.
    /// </summary>
    /// <param name="presetName">Theme preset name.</param>
    /// <param name="variables">Resolved CSS variable set.</param>
    /// <returns><c>true</c> if preset exists; otherwise <c>false</c>.</returns>
    public static bool TryGetVariables(string? presetName, out IReadOnlyDictionary<string, string> variables) {
        if (string.IsNullOrWhiteSpace(presetName)) {
            variables = Empty;
            return false;
        }

        if (Presets.TryGetValue(presetName.Trim(), out var found)) {
            variables = found;
            return true;
        }

        variables = Empty;
        return false;
    }

}
