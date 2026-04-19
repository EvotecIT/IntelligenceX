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
    public CopilotTransportKind? CopilotTransport { get; set; }
    public string? CopilotModel { get; set; }
    public string? CopilotLauncher { get; set; }
    public string? CopilotCliPath { get; set; }
    public string? CopilotCliUrl { get; set; }
    public string? CopilotWorkingDirectory { get; set; }
    public bool? CopilotAutoInstall { get; set; }
    public string? CopilotAutoInstallMethod { get; set; }
    public bool? CopilotAutoInstallPrerelease { get; set; }
    public bool? CopilotInheritEnvironment { get; set; }
    public IReadOnlyList<string>? CopilotEnvAllowlist { get; set; }
    public IReadOnlyDictionary<string, string>? CopilotEnv { get; set; }
    public string? CopilotDirectUrl { get; set; }
    public string? CopilotDirectTokenEnv { get; set; }
    public int? CopilotDirectTimeoutSeconds { get; set; }
    public IReadOnlyDictionary<string, string>? CopilotDirectHeaders { get; set; }
    public string? OpenAICompatibleBaseUrl { get; set; }
    public string? OpenAICompatibleApiKeyEnv { get; set; }
    public int? OpenAICompatibleTimeoutSeconds { get; set; }
    public string? AnthropicApiKeyEnv { get; set; }
    public string? AnthropicBaseUrl { get; set; }
    public int? AnthropicTimeoutSeconds { get; set; }

    public ReviewProvider? ResolveProvider() {
        if (Provider.HasValue) {
            return Provider.Value;
        }
        if (string.IsNullOrWhiteSpace(Authenticator)) {
            return null;
        }

        var normalized = Authenticator.Trim().ToLowerInvariant();
        return normalized switch {
            "copilot" or "copilot-cli" or "github-copilot" => ReviewProvider.Copilot,
            "chatgpt" or "openai" or "codex" or "openai-codex" => ReviewProvider.OpenAI,
            "claude" or "anthropic" => ReviewProvider.Claude,
            "openai-compatible" or "openai-api" or "ollama" or "openrouter" => ReviewProvider.OpenAICompatible,
            _ => null
        };
    }

    public ReviewAgentProfileSettings Clone() {
        return (ReviewAgentProfileSettings)MemberwiseClone();
    }
}
