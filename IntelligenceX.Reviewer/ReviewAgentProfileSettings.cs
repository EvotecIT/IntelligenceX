using System;
using System.Collections.Generic;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewAgentProfileSettings {
    public string Id { get; set; } = string.Empty;
    public ReviewProvider? Provider { get; set; }
    public string? Model { get; set; }
    public string? Authenticator { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public OpenAITransportKind? OpenAITransport { get; set; }
    public string? OpenAiAccountId { get; set; }
    public string? CopilotModel { get; set; }
    public string? CopilotBaseUrl { get; set; }
    public string? CopilotToken { get; set; }
    public string? CopilotTokenEnvironmentVariable { get; set; }
    public int? CopilotRequestTimeoutSeconds { get; set; }
    public string? OpenAICompatibleBaseUrl { get; set; }
    public string? OpenAICompatibleApiKeyEnv { get; set; }
    public int? OpenAICompatibleTimeoutSeconds { get; set; }
    public string? AnthropicApiKeyEnv { get; set; }
    public string? AnthropicBaseUrl { get; set; }
    public int? AnthropicTimeoutSeconds { get; set; }

    public ReviewProvider? ResolveProvider(string? source = null) {
        if (Provider.HasValue) {
            return Provider.Value;
        }
        if (string.IsNullOrWhiteSpace(Authenticator)) {
            return null;
        }

        var normalized = Authenticator.Trim().ToLowerInvariant();
        return normalized switch {
            "copilot" or "copilot-native" or "github-copilot" => ReviewProvider.Copilot,
            "chatgpt" or "openai" or "codex" or "openai-codex" => ReviewProvider.OpenAI,
            "claude" or "anthropic" => ReviewProvider.Claude,
            "openai-compatible" or "openai-api" or "ollama" or "openrouter" => ReviewProvider.OpenAICompatible,
            _ => throw new InvalidOperationException(
                $"Unknown review agent profile authenticator '{Authenticator.Trim()}'" +
                (string.IsNullOrWhiteSpace(source) ? "." : $" from {source}."))
        };
    }
}
