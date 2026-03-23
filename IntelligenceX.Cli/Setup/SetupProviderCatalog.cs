using IntelligenceX.OpenAI;

namespace IntelligenceX.Cli.Setup;

internal static class SetupProviderCatalog {
    internal readonly record struct SetupProviderModelProfile(
        string ProfileId,
        string ProfileLabel,
        string ModelId,
        string DisplayLabel,
        string Summary,
        bool IsRecommendedDefault = false);

    public const string OpenAiProvider = "openai";
    public const string ClaudeProvider = "claude";
    public const string CopilotProvider = "copilot";

    public const string OpenAiSecretName = "INTELLIGENCEX_AUTH_B64";
    public const string ClaudeSecretName = "ANTHROPIC_API_KEY";

    public const string DefaultClaudeModel = "claude-opus-4-1";
    public const string CustomProfileId = "__custom__";

    private static readonly IReadOnlyList<SetupProviderModelProfile> OpenAiRecommendedModelProfiles = new[] {
        new SetupProviderModelProfile("openai-default-review", "OpenAI default review", OpenAIModelCatalog.DefaultModel, OpenAIModelCatalog.DefaultModel, "Best default quality for reviewer runs.", true),
        new SetupProviderModelProfile("openai-fast-review", "OpenAI fast review", $"{OpenAIModelCatalog.DefaultModel}/fast", $"{OpenAIModelCatalog.DefaultModel}/fast", "Lower-latency default when you want faster PR turnaround."),
        new SetupProviderModelProfile("openai-budget-review", "OpenAI budget review", "gpt-5-mini", "gpt-5-mini", "Cheaper review pass with solid quality for routine repos."),
        new SetupProviderModelProfile("openai-nano-check", "OpenAI nano check", "gpt-5-nano", "gpt-5-nano", "Smallest budget option for lightweight checks or experimentation.")
    };

    private static readonly IReadOnlyList<SetupProviderModelProfile> ClaudeRecommendedModelProfiles = new[] {
        new SetupProviderModelProfile("claude-deep-review", "Claude deep review", DefaultClaudeModel, DefaultClaudeModel, "Best Claude review quality for deep code review passes.", true),
        new SetupProviderModelProfile("claude-balanced-review", "Claude balanced review", "claude-sonnet-4-5", "claude-sonnet-4-5", "Balanced Claude option when you want strong reviews with lower cost than Opus."),
        new SetupProviderModelProfile("claude-fast-review", "Claude fast review", "claude-haiku-4-5", "claude-haiku-4-5", "Fastest Claude option for lighter checks and quicker iteration.")
    };

    public static string GetCanonicalProviderId(string? provider) {
        if (IsOpenAiProvider(provider)) {
            return OpenAiProvider;
        }

        if (IsClaudeProvider(provider)) {
            return ClaudeProvider;
        }

        if (string.Equals(provider, CopilotProvider, StringComparison.OrdinalIgnoreCase)) {
            return CopilotProvider;
        }

        return string.IsNullOrWhiteSpace(provider) ? OpenAiProvider : provider.Trim().ToLowerInvariant();
    }

    public static bool IsOpenAiProvider(string? provider) {
        if (string.IsNullOrWhiteSpace(provider)) {
            return false;
        }

        return provider.Trim().ToLowerInvariant() is "openai" or "codex" or "chatgpt" or "openai-codex";
    }

    public static bool IsClaudeProvider(string? provider) {
        if (string.IsNullOrWhiteSpace(provider)) {
            return false;
        }

        return provider.Trim().ToLowerInvariant() is "claude" or "anthropic";
    }

    public static bool RequiresManagedSecret(string? provider) {
        return IsOpenAiProvider(provider) || IsClaudeProvider(provider);
    }

    public static bool SupportsOrgSecret(string? provider) {
        return IsOpenAiProvider(provider);
    }

    public static bool SupportsOpenAiAccountRouting(string? provider) {
        return IsOpenAiProvider(provider);
    }

    public static string? GetSecretName(string? provider) {
        if (IsOpenAiProvider(provider)) {
            return OpenAiSecretName;
        }

        if (IsClaudeProvider(provider)) {
            return ClaudeSecretName;
        }

        return null;
    }

    public static string GetDefaultModel(string? provider) {
        if (IsClaudeProvider(provider)) {
            return DefaultClaudeModel;
        }

        return OpenAIModelCatalog.DefaultModel;
    }

    public static IReadOnlyList<string> GetRecommendedModels(string? provider) {
        return GetRecommendedModelProfiles(provider)
            .Select(profile => profile.ModelId)
            .ToArray();
    }

    public static IReadOnlyList<SetupProviderModelProfile> GetRecommendedModelProfiles(string? provider) {
        if (IsClaudeProvider(provider)) {
            return ClaudeRecommendedModelProfiles;
        }

        if (IsOpenAiProvider(provider)) {
            return OpenAiRecommendedModelProfiles;
        }

        return Array.Empty<SetupProviderModelProfile>();
    }

    public static SetupProviderModelProfile? TryGetRecommendedModelProfile(string? provider, string? model) {
        if (string.IsNullOrWhiteSpace(model)) {
            return null;
        }

        foreach (var profile in GetRecommendedModelProfiles(provider)) {
            if (string.Equals(profile.ModelId, model.Trim(), StringComparison.OrdinalIgnoreCase)) {
                return profile;
            }
        }

        return null;
    }

    public static SetupProviderModelProfile? TryGetRecommendedModelProfileById(string? provider, string? profileId) {
        if (string.IsNullOrWhiteSpace(profileId) ||
            string.Equals(profileId, CustomProfileId, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        foreach (var profile in GetRecommendedModelProfiles(provider)) {
            if (string.Equals(profile.ProfileId, profileId.Trim(), StringComparison.OrdinalIgnoreCase)) {
                return profile;
            }
        }

        return null;
    }

    public static string GetProviderDisplayName(string? provider) {
        if (IsClaudeProvider(provider)) {
            return "Claude";
        }

        if (IsOpenAiProvider(provider)) {
            return "ChatGPT / OpenAI";
        }

        if (string.Equals(provider, CopilotProvider, StringComparison.OrdinalIgnoreCase)) {
            return "GitHub Copilot";
        }

        return string.IsNullOrWhiteSpace(provider) ? "provider" : provider.Trim();
    }

    public static string? GetProviderSetupSummary(string? provider) {
        if (IsClaudeProvider(provider)) {
            return "Claude uses an Anthropic API key and reviewer usage is tracked separately from local Claude session logs.";
        }

        if (IsOpenAiProvider(provider)) {
            return "OpenAI uses your ChatGPT/OpenAI auth bundle and can optionally route reviewer runs across OpenAI accounts.";
        }

        if (string.Equals(provider, CopilotProvider, StringComparison.OrdinalIgnoreCase)) {
            return "Copilot setup relies on GitHub Copilot CLI instead of a managed provider secret in setup.";
        }

        return null;
    }
}
