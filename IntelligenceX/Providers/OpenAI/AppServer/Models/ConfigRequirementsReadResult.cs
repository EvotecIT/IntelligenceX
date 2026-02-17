using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the response containing server-side configuration constraints.
/// </summary>
public sealed class ConfigRequirementsReadResult {
    /// <summary>
    /// Initializes a new requirements read result.
    /// </summary>
    public ConfigRequirementsReadResult(ConfigRequirements? requirements, JsonObject raw, JsonObject? additional) {
        Requirements = requirements;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the configuration requirements returned by app-server.
    /// </summary>
    public ConfigRequirements? Requirements { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a requirements result from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed result.</returns>
    public static ConfigRequirementsReadResult FromJson(JsonObject obj) {
        var requirementsObj = obj.GetObject("requirements");
        var requirements = requirementsObj is null ? null : ConfigRequirements.FromJson(requirementsObj);
        var additional = obj.ExtractAdditional("requirements");
        return new ConfigRequirementsReadResult(requirements, obj, additional);
    }
}

/// <summary>
/// Describes allowed values for selected configuration keys.
/// </summary>
public sealed class ConfigRequirements {
    /// <summary>
    /// Initializes a new configuration requirements model.
    /// </summary>
    public ConfigRequirements(IReadOnlyList<string>? allowedApprovalPolicies, IReadOnlyList<string>? allowedSandboxModes,
        JsonObject raw, JsonObject? additional) {
        AllowedApprovalPolicies = allowedApprovalPolicies;
        AllowedSandboxModes = allowedSandboxModes;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets allowed values for approval policy settings.
    /// </summary>
    public IReadOnlyList<string>? AllowedApprovalPolicies { get; }
    /// <summary>
    /// Gets allowed values for sandbox mode settings.
    /// </summary>
    public IReadOnlyList<string>? AllowedSandboxModes { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses requirements from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed requirements.</returns>
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
                items.Add(value!);
            }
        }
        return items;
    }
}
