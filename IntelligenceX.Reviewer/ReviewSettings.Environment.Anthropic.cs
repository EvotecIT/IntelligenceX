using System;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static void ApplyAnthropicEnvironment(ReviewSettings settings) {
        var anthropicBaseUrl = GetInput("anthropic_base_url", "ANTHROPIC_BASE_URL");
        if (!string.IsNullOrWhiteSpace(anthropicBaseUrl)) {
            settings.AnthropicBaseUrl = anthropicBaseUrl;
        }

        var anthropicVersion = GetInput("anthropic_version", "ANTHROPIC_VERSION");
        if (!string.IsNullOrWhiteSpace(anthropicVersion)) {
            settings.AnthropicVersion = anthropicVersion;
        }

        var anthropicApiKeyEnv = GetInput("anthropic_api_key_env", "REVIEW_ANTHROPIC_API_KEY_ENV");
        if (!string.IsNullOrWhiteSpace(anthropicApiKeyEnv)) {
            settings.AnthropicApiKeyEnv = anthropicApiKeyEnv;
        }

        var anthropicApiKey = GetInput("anthropic_api_key", "REVIEW_ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(anthropicApiKey)) {
            settings.AnthropicApiKey = anthropicApiKey;
        }

        var anthropicTimeout = GetInput("anthropic_timeout_seconds", "ANTHROPIC_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(anthropicTimeout)) {
            settings.AnthropicTimeoutSeconds =
                ParsePositiveInt(anthropicTimeout, settings.AnthropicTimeoutSeconds);
        }

        var anthropicMaxTokens = GetInput("anthropic_max_tokens", "ANTHROPIC_MAX_TOKENS");
        if (!string.IsNullOrWhiteSpace(anthropicMaxTokens)) {
            settings.AnthropicMaxTokens =
                ParsePositiveInt(anthropicMaxTokens, settings.AnthropicMaxTokens);
        }
    }
}
