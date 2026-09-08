using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static partial class ReviewConfigLoader {
    private static void ApplyCopilot(JsonObject root, ReviewSettings settings) {
        var copilot = root.GetObject("copilot");
        if (copilot is null) return;
        RejectRetiredCopilotConfiguration(copilot);
        settings.CopilotModel = copilot.GetString("model") ?? settings.CopilotModel;
        settings.CopilotBaseUrl = copilot.GetString("baseUrl") ?? settings.CopilotBaseUrl;
        string? token = copilot.GetString("token"), tokenEnvironment = copilot.GetString("tokenEnv");
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(tokenEnvironment))
            throw new InvalidOperationException("Copilot configuration must select either token or tokenEnv, not both.");
        if (!string.IsNullOrWhiteSpace(token)) {
            settings.CopilotToken = token;
            settings.CopilotTokenEnvironmentVariable = null;
        } else if (!string.IsNullOrWhiteSpace(tokenEnvironment)) {
            settings.CopilotToken = null;
            settings.CopilotTokenEnvironmentVariable = tokenEnvironment.Trim();
        }
        settings.CopilotRequestTimeoutSeconds = ReadInt(copilot, "timeoutSeconds", settings.CopilotRequestTimeoutSeconds);
    }

    private static void ApplyAgentProfileCopilot(JsonObject obj, ReviewAgentProfileSettings profile) {
        RejectRetiredCopilotConfiguration(obj);
        profile.CopilotModel = obj.GetString("copilotModel") ?? obj.GetString("model") ?? profile.CopilotModel;
        profile.CopilotBaseUrl = obj.GetString("copilotBaseUrl") ?? obj.GetString("baseUrl") ?? profile.CopilotBaseUrl;
        profile.CopilotToken = obj.GetString("copilotToken") ?? obj.GetString("token") ?? profile.CopilotToken;
        profile.CopilotTokenEnvironmentVariable = obj.GetString("copilotTokenEnv") ?? obj.GetString("tokenEnv") ?? profile.CopilotTokenEnvironmentVariable;
        if (!string.IsNullOrWhiteSpace(profile.CopilotToken) && !string.IsNullOrWhiteSpace(profile.CopilotTokenEnvironmentVariable))
            throw new InvalidOperationException("A Copilot agent profile must select either token or tokenEnv, not both.");
        profile.CopilotRequestTimeoutSeconds = ReadNullablePositiveInt(obj, "copilotTimeoutSeconds") ?? ReadNullablePositiveInt(obj, "timeoutSeconds");
    }

    private static void RejectRetiredCopilotConfiguration(JsonObject obj) {
        foreach (string key in new[] { "transport", "copilotTransport", "cliPath", "copilotCliPath", "cliUrl", "copilotCliUrl",
            "launcher", "copilotLauncher", "autoInstall", "copilotAutoInstall", "autoInstallMethod", "copilotAutoInstallMethod",
            "autoInstallPrerelease", "copilotAutoInstallPrerelease", "directUrl", "copilotDirectUrl", "directToken",
            "directTokenEnv", "copilotDirectTokenEnv", "directHeaders", "copilotDirectHeaders", "directTimeoutSeconds", "copilotDirectTimeoutSeconds",
            "inheritEnvironment", "copilotInheritEnvironment", "envAllowlist", "copilotEnvAllowlist", "env", "copilotEnv",
            "workingDirectory", "copilotWorkingDirectory" }) {
            if (obj.TryGetValue(key, out _)) throw new InvalidOperationException(
                "Copilot runtime/direct-wrapper configuration has been retired. Use model, baseUrl, tokenEnv and timeoutSeconds for native Copilot.");
        }
    }
}
