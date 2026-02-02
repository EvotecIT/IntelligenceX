using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a list of skill groups.
/// </summary>
public sealed class SkillListResult {
    /// <summary>
    /// Initializes a new skill list result.
    /// </summary>
    public SkillListResult(IReadOnlyList<SkillGroup> groups, JsonObject raw, JsonObject? additional) {
        Groups = groups;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the skill groups.
    /// </summary>
    public IReadOnlyList<SkillGroup> Groups { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a skill list from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed skill list result.</returns>
    public static SkillListResult FromJson(JsonObject obj) {
        var groups = new List<SkillGroup>();
        var data = obj.GetArray("data") ?? obj.GetArray("items");
        if (data is not null) {
            foreach (var entry in data) {
                var groupObj = entry.AsObject();
                if (groupObj is null) {
                    continue;
                }
                groups.Add(SkillGroup.FromJson(groupObj));
            }
        }
        var additional = obj.ExtractAdditional("data", "items");
        return new SkillListResult(groups, obj, additional);
    }
}

/// <summary>
/// Represents a skill group for a working directory.
/// </summary>
public sealed class SkillGroup {
    /// <summary>
    /// Initializes a new skill group.
    /// </summary>
    public SkillGroup(string? cwd, IReadOnlyList<SkillInfo> skills, IReadOnlyList<string> errors,
        JsonObject raw, JsonObject? additional) {
        Cwd = cwd;
        Skills = skills;
        Errors = errors;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the working directory associated with the group.
    /// </summary>
    public string? Cwd { get; }
    /// <summary>
    /// Gets the skills in the group.
    /// </summary>
    public IReadOnlyList<SkillInfo> Skills { get; }
    /// <summary>
    /// Gets any errors for the group.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a skill group from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed skill group.</returns>
    public static SkillGroup FromJson(JsonObject obj) {
        var cwd = obj.GetString("cwd");
        var skills = new List<SkillInfo>();
        var skillsArray = obj.GetArray("skills");
        if (skillsArray is not null) {
            foreach (var item in skillsArray) {
                var skillObj = item.AsObject();
                if (skillObj is null) {
                    continue;
                }
                skills.Add(SkillInfo.FromJson(skillObj));
            }
        }

        var errors = new List<string>();
        var errorArray = obj.GetArray("errors");
        if (errorArray is not null) {
            foreach (var item in errorArray) {
                var text = item.AsString() ?? item.ToString();
                if (!string.IsNullOrWhiteSpace(text)) {
                    errors.Add(text);
                }
            }
        }

        var additional = obj.ExtractAdditional("cwd", "skills", "errors");
        return new SkillGroup(cwd, skills, errors, obj, additional);
    }
}

/// <summary>
/// Represents a single skill entry.
/// </summary>
public sealed class SkillInfo {
    /// <summary>
    /// Initializes a new skill info entry.
    /// </summary>
    public SkillInfo(string name, string? description, bool enabled, SkillInterfaceInfo? @interface,
        JsonObject raw, JsonObject? additional) {
        Name = name;
        Description = description;
        Enabled = enabled;
        Interface = @interface;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the skill name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the skill description.
    /// </summary>
    public string? Description { get; }
    /// <summary>
    /// Gets a value indicating whether the skill is enabled.
    /// </summary>
    public bool Enabled { get; }
    /// <summary>
    /// Gets the optional interface metadata.
    /// </summary>
    public SkillInterfaceInfo? Interface { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses skill info from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed skill info.</returns>
    public static SkillInfo FromJson(JsonObject obj) {
        var name = obj.GetString("name") ?? string.Empty;
        var description = obj.GetString("description");
        var enabled = obj.GetBoolean("enabled");
        var interfaceObj = obj.GetObject("interface");
        var @interface = interfaceObj is null ? null : SkillInterfaceInfo.FromJson(interfaceObj);
        var additional = obj.ExtractAdditional("name", "description", "enabled", "interface");
        return new SkillInfo(name, description, enabled, @interface, obj, additional);
    }
}

/// <summary>
/// Represents UI metadata for a skill.
/// </summary>
public sealed class SkillInterfaceInfo {
    /// <summary>
    /// Initializes a new skill interface info entry.
    /// </summary>
    public SkillInterfaceInfo(string? displayName, string? shortDescription, string? iconSmall, string? iconLarge,
        string? brandColor, string? defaultPrompt, JsonObject raw, JsonObject? additional) {
        DisplayName = displayName;
        ShortDescription = shortDescription;
        IconSmall = iconSmall;
        IconLarge = iconLarge;
        BrandColor = brandColor;
        DefaultPrompt = defaultPrompt;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string? DisplayName { get; }
    /// <summary>
    /// Gets the short description.
    /// </summary>
    public string? ShortDescription { get; }
    /// <summary>
    /// Gets the small icon URL.
    /// </summary>
    public string? IconSmall { get; }
    /// <summary>
    /// Gets the large icon URL.
    /// </summary>
    public string? IconLarge { get; }
    /// <summary>
    /// Gets the brand color.
    /// </summary>
    public string? BrandColor { get; }
    /// <summary>
    /// Gets the default prompt.
    /// </summary>
    public string? DefaultPrompt { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses skill interface metadata from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed interface info.</returns>
    public static SkillInterfaceInfo FromJson(JsonObject obj) {
        var displayName = obj.GetString("displayName");
        var shortDescription = obj.GetString("shortDescription");
        var iconSmall = obj.GetString("iconSmall");
        var iconLarge = obj.GetString("iconLarge");
        var brandColor = obj.GetString("brandColor");
        var defaultPrompt = obj.GetString("defaultPrompt");
        var additional = obj.ExtractAdditional(
            "displayName", "shortDescription", "iconSmall", "iconLarge", "brandColor", "defaultPrompt");
        return new SkillInterfaceInfo(displayName, shortDescription, iconSmall, iconLarge, brandColor, defaultPrompt, obj, additional);
    }
}
