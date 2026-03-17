using System.Windows.Media;

namespace IntelligenceX.Tray.Services;

/// <summary>
/// Static provider metadata hardcoded from the internal provider catalog.
/// </summary>
public static class ProviderMetadata {
    public static readonly IReadOnlyDictionary<string, ProviderInfo> Providers =
        new Dictionary<string, ProviderInfo>(StringComparer.OrdinalIgnoreCase) {
            ["codex"] = new("codex", "Codex", "Codex", "IconCodex", 0,
                ParseColor("#98a8ff"), ParseColor("#6268f1"), ParseColor("#2f2a93")),
            ["claude"] = new("claude", "Claude", "Claude", "IconClaude", 1,
                ParseColor("#f3ba73"), ParseColor("#fb8c1d"), ParseColor("#c65102")),
            ["lmstudio"] = new("lmstudio", "LM Studio", "LM", "IconLmStudio", 2,
                ParseColor("#7fd3df"), ParseColor("#2f92a3"), ParseColor("#125c67")),
            ["copilot"] = new("copilot", "GitHub Copilot", "Copilot", "IconCopilot", 5,
                ParseColor("#8cb8ff"), ParseColor("#4a7fe3"), ParseColor("#1d4fbf")),
            ["ix"] = new("ix", "IntelligenceX", "IX", "IconIx", 4,
                ParseColor("#9be9a8"), ParseColor("#40c463"), ParseColor("#216e39")),
            ["chatgpt"] = new("chatgpt", "ChatGPT", "GPT", "IconChatGpt", 6,
                ParseColor("#9be9a8"), ParseColor("#40c463"), ParseColor("#216e39")),
            ["ollama"] = new("ollama", "Ollama", "Ollama", "IconOllama", 7,
                ParseColor("#9be9a8"), ParseColor("#40c463"), ParseColor("#216e39")),
            ["github"] = new("github", "GitHub", "GitHub", "IconGitHub", 3,
                ParseColor("#9be9a8"), ParseColor("#40c463"), ParseColor("#216e39")),
        };

    public static ProviderInfo Resolve(string? providerId) {
        if (!string.IsNullOrWhiteSpace(providerId) && Providers.TryGetValue(providerId, out var info)) {
            return info;
        }

        return new ProviderInfo(
            providerId ?? "unknown", providerId ?? "Unknown", providerId ?? "Unknown", "", 99,
            ParseColor("#9be9a8"), ParseColor("#40c463"), ParseColor("#216e39"));
    }

    private static Color ParseColor(string hex) {
        return (Color)ColorConverter.ConvertFromString(hex);
    }
}

public sealed record ProviderInfo(
    string Id,
    string DisplayName,
    string ShortName,
    string Icon,
    int SortOrder,
    Color InputColor,
    Color OutputColor,
    Color TotalColor);
