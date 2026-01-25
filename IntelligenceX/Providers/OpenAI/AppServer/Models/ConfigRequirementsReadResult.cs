using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ConfigRequirementsReadResult {
    public ConfigRequirementsReadResult(ConfigRequirements? requirements, JsonObject raw, JsonObject? additional) {
        Requirements = requirements;
        Raw = raw;
        Additional = additional;
    }

    public ConfigRequirements? Requirements { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ConfigRequirementsReadResult FromJson(JsonObject obj) {
        var requirementsObj = obj.GetObject("requirements");
        var requirements = requirementsObj is null ? null : ConfigRequirements.FromJson(requirementsObj);
        var additional = obj.ExtractAdditional("requirements");
        return new ConfigRequirementsReadResult(requirements, obj, additional);
    }
}

public sealed class ConfigRequirements {
    public ConfigRequirements(IReadOnlyList<string>? allowedApprovalPolicies, IReadOnlyList<string>? allowedSandboxModes,
        JsonObject raw, JsonObject? additional) {
        AllowedApprovalPolicies = allowedApprovalPolicies;
        AllowedSandboxModes = allowedSandboxModes;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<string>? AllowedApprovalPolicies { get; }
    public IReadOnlyList<string>? AllowedSandboxModes { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ConfigRequirements FromJson(JsonObject obj) {
        var approvalPolicies = ReadStringArray(obj, "allowedApprovalPolicies", "allowed_approval_policies");
        var sandboxModes = ReadStringArray(obj, "allowedSandboxModes", "allowed_sandbox_modes");
        var additional = obj.ExtractAdditional(
            "allowedApprovalPolicies", "allowed_approval_policies",
            "allowedSandboxModes", "allowed_sandbox_modes");
        return new ConfigRequirements(approvalPolicies, sandboxModes, obj, additional);
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonObject obj, string primary, string fallback) {
        var array = obj.GetArray(primary) ?? obj.GetArray(fallback);
        if (array is null) {
            return null;
        }
        var items = new List<string>();
        foreach (var item in array) {
            var value = item.AsString();
            if (!string.IsNullOrWhiteSpace(value)) {
                items.Add(value);
            }
        }
        return items;
    }
}
