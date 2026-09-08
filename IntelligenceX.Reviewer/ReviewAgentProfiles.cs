using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal bool TryGetAgentProfile(string? id, out ReviewAgentProfileSettings profile) {
        profile = new ReviewAgentProfileSettings();
        if (string.IsNullOrWhiteSpace(id) || AgentProfiles.Count == 0) {
            return false;
        }
        return AgentProfiles.TryGetValue(id.Trim(), out profile!);
    }

    internal void ApplyAgentProfile(string? id) {
        if (string.IsNullOrWhiteSpace(id)) {
            return;
        }

        var profile = RequireAgentProfile(id, "review.agentProfile");
        ApplyAgentProfile(profile, captureBaseline: true);
    }

    internal void ApplySelectedAgentProfile() {
        if (!string.IsNullOrWhiteSpace(AgentProfile)) {
            ApplyAgentProfile(AgentProfile);
        }
    }

    internal ReviewAgentProfileSettings RequireAgentProfile(string? id, string source) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new InvalidOperationException($"{source} did not provide an agent profile id.");
        }

        if (TryGetAgentProfile(id, out var profile)) {
            return profile;
        }

        throw new InvalidOperationException(
            $"Unknown review agent profile '{id.Trim()}' from {source}. Define it under review.agentProfiles or remove the profile override.");
    }

    internal void ApplyAgentProfile(ReviewAgentProfileSettings profile) =>
        ApplyAgentProfile(profile, captureBaseline: true);

    private void ApplyAgentProfile(ReviewAgentProfileSettings profile, bool captureBaseline) {
        if (captureBaseline) {
            CaptureOrRebaseAgentProfileBaseline();
        }
        AgentProfile = profile.Id;
        var provider = profile.ResolveProvider($"review.agentProfiles.{profile.Id}.authenticator");
        if (provider.HasValue) {
            Provider = provider.Value;
        }
        var effectiveProvider = provider ?? Provider;
        if (!string.IsNullOrWhiteSpace(profile.Model)) {
            Model = profile.Model.Trim();
            if (effectiveProvider == ReviewProvider.Copilot) CopilotModel = Model;
        }
        if (profile.ReasoningEffort.HasValue) {
            ReasoningEffort = profile.ReasoningEffort;
        }
        if (profile.OpenAITransport.HasValue) {
            OpenAITransport = profile.OpenAITransport.Value;
        }
        OpenAiAccountId = UseIfSet(profile.OpenAiAccountId, OpenAiAccountId);

        CopilotModel = UseIfSet(profile.CopilotModel, CopilotModel);
        CopilotBaseUrl = UseIfSet(profile.CopilotBaseUrl, CopilotBaseUrl);
        if (!string.IsNullOrWhiteSpace(profile.CopilotToken) && !string.IsNullOrWhiteSpace(profile.CopilotTokenEnvironmentVariable))
            throw new InvalidOperationException("A Copilot agent profile must select either token or tokenEnv, not both.");
        if (!string.IsNullOrWhiteSpace(profile.CopilotToken)) {
            CopilotToken = profile.CopilotToken.Trim();
            CopilotTokenEnvironmentVariable = null;
        } else if (!string.IsNullOrWhiteSpace(profile.CopilotTokenEnvironmentVariable)) {
            CopilotToken = null;
            CopilotTokenEnvironmentVariable = profile.CopilotTokenEnvironmentVariable.Trim();
        }
        if (profile.CopilotRequestTimeoutSeconds.HasValue && profile.CopilotRequestTimeoutSeconds.Value > 0) {
            CopilotRequestTimeoutSeconds = profile.CopilotRequestTimeoutSeconds.Value;
        }

        OpenAICompatibleBaseUrl = UseIfSet(profile.OpenAICompatibleBaseUrl, OpenAICompatibleBaseUrl);
        OpenAICompatibleApiKeyEnv = UseIfSet(profile.OpenAICompatibleApiKeyEnv, OpenAICompatibleApiKeyEnv);
        if (profile.OpenAICompatibleTimeoutSeconds.HasValue && profile.OpenAICompatibleTimeoutSeconds.Value > 0) {
            OpenAICompatibleTimeoutSeconds = profile.OpenAICompatibleTimeoutSeconds.Value;
        }

        AnthropicApiKeyEnv = UseIfSet(profile.AnthropicApiKeyEnv, AnthropicApiKeyEnv);
        AnthropicBaseUrl = UseIfSet(profile.AnthropicBaseUrl, AnthropicBaseUrl)!;
        if (profile.AnthropicTimeoutSeconds.HasValue && profile.AnthropicTimeoutSeconds.Value > 0) {
            AnthropicTimeoutSeconds = profile.AnthropicTimeoutSeconds.Value;
        }
    }

    internal void RebaseToAgentProfileBaseline() {
        if (AgentProfileBaseline is null) {
            return;
        }

        RestoreProfileOwnedState(AgentProfileBaseline);
    }

    internal void RefreshAgentProfileBaseline() {
        var selectedProfile = AgentProfile;
        var clone = CloneWithoutAgentProfileBaseline();
        clone.AgentProfile = null;
        clone.AgentProfileBaseline = null;
        AgentProfileBaseline = clone;
        AgentProfile = selectedProfile;
    }

    private void CaptureOrRebaseAgentProfileBaseline() {
        if (AgentProfileBaseline is null) {
            CaptureAgentProfileBaseline();
            return;
        }

        RestoreProfileOwnedState(AgentProfileBaseline);
    }

    private void CaptureAgentProfileBaseline() {
        if (AgentProfileBaseline is not null) {
            return;
        }

        RefreshAgentProfileBaseline();
    }

    private ReviewSettings CloneWithoutAgentProfileBaseline() {
        var clone = (ReviewSettings)MemberwiseClone();
        clone.AgentProfileBaseline = null;
        return clone;
    }

    private void RestoreProfileOwnedState(ReviewSettings source) {
        Provider = source.Provider;
        Model = source.Model;
        ModelExplicitlyConfigured = source.ModelExplicitlyConfigured;
        ReasoningEffort = source.ReasoningEffort;
        OpenAITransport = source.OpenAITransport;
        OpenAiAccountId = source.OpenAiAccountId;
        CopilotModel = source.CopilotModel;
        CopilotBaseUrl = source.CopilotBaseUrl;
        CopilotToken = source.CopilotToken;
        CopilotTokenEnvironmentVariable = source.CopilotTokenEnvironmentVariable;
        CopilotRequestTimeoutSeconds = source.CopilotRequestTimeoutSeconds;
        OpenAICompatibleBaseUrl = source.OpenAICompatibleBaseUrl;
        OpenAICompatibleApiKeyEnv = source.OpenAICompatibleApiKeyEnv;
        OpenAICompatibleTimeoutSeconds = source.OpenAICompatibleTimeoutSeconds;
        AnthropicApiKeyEnv = source.AnthropicApiKeyEnv;
        AnthropicBaseUrl = source.AnthropicBaseUrl;
        AnthropicTimeoutSeconds = source.AnthropicTimeoutSeconds;
    }

    private static string? UseIfSet(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static IReadOnlyDictionary<string, string> NormalizeStringMap(
        IReadOnlyDictionary<string, string> values) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in values) {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) {
                continue;
            }
            result[entry.Key] = entry.Value;
        }
        return result;
    }
}
