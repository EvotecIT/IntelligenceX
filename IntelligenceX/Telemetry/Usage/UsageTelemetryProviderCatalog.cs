using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Telemetry.Usage.Copilot;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;
using IntelligenceX.Telemetry.Usage.LmStudio;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Central metadata catalog for usage telemetry providers.
/// </summary>
internal static class UsageTelemetryProviderCatalog {
    private static readonly UsageTelemetryProviderAppearance DefaultAppearance =
        new("#9be9a8", "#40c463", "#216e39", "#cfe8d2", Array.Empty<string>());

    private static readonly UsageTelemetryProviderDefinition[] Definitions = {
        new(
            providerId: "codex",
            displayTitle: "Codex",
            sectionTitle: "Codex",
            sortOrder: 0,
            appearance: new UsageTelemetryProviderAppearance(
                "#98a8ff",
                "#6268f1",
                "#2f2a93",
                "#bcc5ff",
                new[] { "#cfd6ff", "#98a8ff", "#6268f1", "#2f2a93" }),
            aliases: new[] { "openai-codex", "chatgpt-codex" },
            pathMatcher: static normalizedPath =>
                normalizedPath.IndexOf(".codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.DirectorySeparatorChar + "sessions", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.AltDirectorySeparatorChar + "sessions", StringComparison.OrdinalIgnoreCase) >= 0,
            providerDescriptorFactory: static () => new CodexUsageTelemetryProviderDescriptor(),
            rootDiscoveryFactory: static () => new CodexDefaultSourceRootDiscovery()),
        new(
            providerId: "claude",
            displayTitle: "Claude",
            sectionTitle: "Claude Code",
            sortOrder: 1,
            appearance: new UsageTelemetryProviderAppearance(
                "#f3ba73",
                "#fb8c1d",
                "#c65102",
                "#e9c89e",
                new[] { "#f5d8b0", "#f3ba73", "#fb8c1d", "#c65102" }),
            aliases: new[] { "anthropic-claude", "claude-code" },
            pathMatcher: static normalizedPath =>
                normalizedPath.IndexOf(".claude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.DirectorySeparatorChar + "projects", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.AltDirectorySeparatorChar + "projects", StringComparison.OrdinalIgnoreCase) >= 0,
            providerDescriptorFactory: static () => new ClaudeUsageTelemetryProviderDescriptor(),
            rootDiscoveryFactory: static () => new ClaudeDefaultSourceRootDiscovery()),
        new(
            providerId: LmStudioConversationImport.StableProviderId,
            displayTitle: "LM Studio",
            sectionTitle: "LM Studio",
            sortOrder: 2,
            appearance: new UsageTelemetryProviderAppearance(
                "#7fd3df",
                "#2f92a3",
                "#125c67",
                "#caeef3",
                new[] { "#caeef3", "#7fd3df", "#2f92a3", "#125c67" }),
            aliases: new[] { "lm-studio", "lm studio" },
            pathMatcher: static normalizedPath =>
                normalizedPath.IndexOf(".lmstudio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.DirectorySeparatorChar + "conversations", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.AltDirectorySeparatorChar + "conversations", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.EndsWith(".conversation.json", StringComparison.OrdinalIgnoreCase),
            providerDescriptorFactory: static () => new LmStudioUsageTelemetryProviderDescriptor(),
            rootDiscoveryFactory: static () => new LmStudioDefaultSourceRootDiscovery()),
        new(
            providerId: "github",
            displayTitle: "GitHub",
            sectionTitle: "GitHub",
            sortOrder: 3,
            appearance: new UsageTelemetryProviderAppearance(
                "#9be9a8",
                "#40c463",
                "#216e39",
                "#cfe8d2",
                Array.Empty<string>())),
        new(
            providerId: "ix",
            displayTitle: "IntelligenceX",
            sectionTitle: "IntelligenceX",
            sortOrder: 4,
            appearance: DefaultAppearance),
        new(
            providerId: "copilot",
            displayTitle: "GitHub Copilot",
            sectionTitle: "GitHub Copilot",
            sortOrder: 5,
            appearance: new UsageTelemetryProviderAppearance(
                "#8cb8ff",
                "#4a7fe3",
                "#1d4fbf",
                "#c9dafc",
                new[] { "#dce8ff", "#8cb8ff", "#4a7fe3", "#1d4fbf" }),
            aliases: new[] { "copilot-cli", "github-copilot", "githubcopilot" },
            pathMatcher: static normalizedPath =>
                normalizedPath.IndexOf(".copilot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.DirectorySeparatorChar + "session-state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf(Path.AltDirectorySeparatorChar + "session-state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.EndsWith("events.jsonl", StringComparison.OrdinalIgnoreCase),
            providerDescriptorFactory: static () => new CopilotUsageTelemetryProviderDescriptor(),
            rootDiscoveryFactory: static () => new CopilotDefaultSourceRootDiscovery()),
        new(
            providerId: "chatgpt",
            displayTitle: "ChatGPT",
            sectionTitle: "ChatGPT",
            sortOrder: 6,
            appearance: DefaultAppearance),
        new(
            providerId: "ollama",
            displayTitle: "Ollama",
            sectionTitle: "Ollama",
            sortOrder: 7,
            appearance: DefaultAppearance)
    };

    private static readonly IReadOnlyDictionary<string, UsageTelemetryProviderDefinition> DefinitionsByAlias =
        BuildDefinitionsByAlias();

    public static UsageTelemetryProviderRegistry CreateProviderRegistry() {
        return new UsageTelemetryProviderRegistry(Definitions
            .Where(static definition => definition.ProviderDescriptorFactory is not null)
            .Select(definition => definition.ProviderDescriptorFactory!())
            .ToArray());
    }

    public static IReadOnlyList<IUsageTelemetryRootDiscovery> CreateRootDiscoveries() {
        return Definitions
            .Where(static definition => definition.RootDiscoveryFactory is not null)
            .Select(definition => definition.RootDiscoveryFactory!())
            .ToArray();
    }

    public static int ResolveSortOrder(string? providerId) {
        return TryResolve(providerId, out var definition)
            ? definition.SortOrder
            : 10;
    }

    public static string ResolveDisplayTitle(string? providerId) {
        if (TryResolve(providerId, out var definition)) {
            return definition.DisplayTitle;
        }

        return string.IsNullOrWhiteSpace(providerId) ? "Unknown" : providerId!.Trim();
    }

    public static string ResolveSectionTitle(string? providerId) {
        if (TryResolve(providerId, out var definition)) {
            return definition.SectionTitle;
        }

        return string.IsNullOrWhiteSpace(providerId) ? "Unknown" : providerId!.Trim();
    }

    public static UsageTelemetryProviderAppearance ResolveAppearance(string? providerId) {
        return TryResolve(providerId, out var definition)
            ? definition.Appearance
            : DefaultAppearance;
    }

    public static string? ResolveCanonicalProviderId(string? providerId) {
        return TryResolve(providerId, out var definition)
            ? definition.ProviderId
            : NormalizeOptional(providerId);
    }

    public static bool IsProvider(string? providerId, string canonicalProviderId) {
        if (string.IsNullOrWhiteSpace(canonicalProviderId)) {
            return false;
        }

        return TryResolve(providerId, out var definition) &&
               string.Equals(definition.ProviderId, canonicalProviderId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string? InferProviderIdFromPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var normalizedPath = UsageTelemetryIdentity.NormalizePath(path);
        foreach (var definition in Definitions) {
            if (definition.PathMatcher is not null && definition.PathMatcher(normalizedPath)) {
                return definition.ProviderId;
            }
        }

        return null;
    }

    private static bool TryResolve(string? providerId, out UsageTelemetryProviderDefinition definition) {
        var normalized = NormalizeOptional(providerId);
        if (normalized is not null && DefinitionsByAlias.TryGetValue(normalized, out var resolvedDefinition)) {
            definition = resolvedDefinition;
            return true;
        }

        definition = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, UsageTelemetryProviderDefinition> BuildDefinitionsByAlias() {
        var map = new Dictionary<string, UsageTelemetryProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Definitions) {
            map[definition.ProviderId] = definition;
            foreach (var alias in definition.Aliases) {
                var normalizedAlias = NormalizeOptional(alias);
                if (normalizedAlias is not null) {
                    map[normalizedAlias] = definition;
                }
            }
        }

        return map;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

internal sealed class UsageTelemetryProviderDefinition {
    public UsageTelemetryProviderDefinition(
        string providerId,
        string displayTitle,
        string sectionTitle,
        int sortOrder,
        UsageTelemetryProviderAppearance appearance,
        IReadOnlyList<string>? aliases = null,
        Func<string, bool>? pathMatcher = null,
        Func<IUsageTelemetryProviderDescriptor>? providerDescriptorFactory = null,
        Func<IUsageTelemetryRootDiscovery>? rootDiscoveryFactory = null) {
        ProviderId = providerId;
        DisplayTitle = displayTitle;
        SectionTitle = sectionTitle;
        SortOrder = sortOrder;
        Appearance = appearance;
        Aliases = aliases ?? Array.Empty<string>();
        PathMatcher = pathMatcher;
        ProviderDescriptorFactory = providerDescriptorFactory;
        RootDiscoveryFactory = rootDiscoveryFactory;
    }

    public string ProviderId { get; }
    public string DisplayTitle { get; }
    public string SectionTitle { get; }
    public int SortOrder { get; }
    public UsageTelemetryProviderAppearance Appearance { get; }
    public IReadOnlyList<string> Aliases { get; }
    public Func<string, bool>? PathMatcher { get; }
    public Func<IUsageTelemetryProviderDescriptor>? ProviderDescriptorFactory { get; }
    public Func<IUsageTelemetryRootDiscovery>? RootDiscoveryFactory { get; }
}

internal sealed record UsageTelemetryProviderAppearance(
    string Input,
    string Output,
    string Total,
    string Other,
    IReadOnlyList<string> IntensityColors);
