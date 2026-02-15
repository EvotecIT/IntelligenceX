using System;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static void ApplyOpenAiEnvironment(ReviewSettings settings) {
        var transport = GetInput("openai_transport", "OPENAI_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(transport)) {
            settings.OpenAITransport = ParseTransport(transport);
        }
        var openAiAccountId = GetInput("openai_account_id", "REVIEW_OPENAI_ACCOUNT_ID", "INTELLIGENCEX_OPENAI_ACCOUNT_ID");
        if (!string.IsNullOrWhiteSpace(openAiAccountId)) {
            settings.OpenAiAccountId = openAiAccountId;
        }
        var openAiAccountIds = GetInput("openai_account_ids", "REVIEW_OPENAI_ACCOUNT_IDS", "INTELLIGENCEX_OPENAI_ACCOUNT_IDS");
        if (!string.IsNullOrWhiteSpace(openAiAccountIds)) {
            settings.OpenAiAccountIds = NormalizeAccountIdList(ParseList(openAiAccountIds));
        }
        var openAiAccountRotation = GetInput("openai_account_rotation", "REVIEW_OPENAI_ACCOUNT_ROTATION");
        if (!string.IsNullOrWhiteSpace(openAiAccountRotation)) {
            settings.OpenAiAccountRotation =
                NormalizeOpenAiAccountRotation(openAiAccountRotation, settings.OpenAiAccountRotation);
        }
        var openAiAccountFailover = GetInput("openai_account_failover", "REVIEW_OPENAI_ACCOUNT_FAILOVER");
        if (!string.IsNullOrWhiteSpace(openAiAccountFailover)) {
            settings.OpenAiAccountFailover = ParseBoolean(openAiAccountFailover, settings.OpenAiAccountFailover);
        }
    }

    private static void ApplyOpenAiCompatibleEnvironment(ReviewSettings settings) {
        var openAiCompatibleBaseUrl = GetInput("openai_compatible_base_url", "OPENAI_COMPATIBLE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleBaseUrl)) {
            settings.OpenAICompatibleBaseUrl = openAiCompatibleBaseUrl;
        }
        var openAiCompatibleApiKeyEnv = GetInput("openai_compatible_api_key_env", "OPENAI_COMPATIBLE_API_KEY_ENV");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleApiKeyEnv)) {
            settings.OpenAICompatibleApiKeyEnv = openAiCompatibleApiKeyEnv;
        }
        var openAiCompatibleApiKey = GetInput("openai_compatible_api_key", "OPENAI_COMPATIBLE_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleApiKey)) {
            settings.OpenAICompatibleApiKey = openAiCompatibleApiKey;
        }
        var openAiCompatibleTimeout = GetInput("openai_compatible_timeout_seconds", "OPENAI_COMPATIBLE_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleTimeout)) {
            settings.OpenAICompatibleTimeoutSeconds =
                ParsePositiveInt(openAiCompatibleTimeout, settings.OpenAICompatibleTimeoutSeconds);
        }
        var openAiCompatibleAllowInsecure = GetInput(
            "openai_compatible_allow_insecure_http",
            "OPENAI_COMPATIBLE_ALLOW_INSECURE_HTTP");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleAllowInsecure)) {
            settings.OpenAICompatibleAllowInsecureHttp =
                ParseBoolean(openAiCompatibleAllowInsecure, settings.OpenAICompatibleAllowInsecureHttp);
        }
        var openAiCompatibleDropAuthOnRedirect = GetInput(
            "openai_compatible_drop_authorization_on_redirect",
            "OPENAI_COMPATIBLE_DROP_AUTHORIZATION_ON_REDIRECT");
        if (!string.IsNullOrWhiteSpace(openAiCompatibleDropAuthOnRedirect)) {
            settings.OpenAICompatibleDropAuthorizationOnRedirect =
                ParseBoolean(openAiCompatibleDropAuthOnRedirect, settings.OpenAICompatibleDropAuthorizationOnRedirect);
        }
    }
}

