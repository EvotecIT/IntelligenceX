using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents skill metadata returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var skills = await client.ListSkillsAsync();
/// foreach (var group in skills.Groups) {
///     foreach (var skill in group.Skills) {
///         Console.WriteLine(skill.Name);
///     }
/// }
/// </code>
/// </example>
public sealed class SkillListResult {
    public SkillListResult(IReadOnlyList<SkillGroup> groups, JsonObject raw, JsonObject? additional) {
        Groups = groups;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Skill groups returned by the service.</summary>
    public IReadOnlyList<SkillGroup> Groups { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses skills from JSON.</summary>
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
/// Represents a group of skills for a working directory.
/// </summary>
public sealed class SkillGroup {
    public SkillGroup(string? cwd, IReadOnlyList<SkillInfo> skills, IReadOnlyList<string> errors,
        JsonObject raw, JsonObject? additional) {
        Cwd = cwd;
        Skills = skills;
        Errors = errors;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Working directory for the group (if available).</summary>
    public string? Cwd { get; }
    /// <summary>Skills discovered for this group.</summary>
    public IReadOnlyList<SkillInfo> Skills { get; }
    /// <summary>Errors encountered while loading skills.</summary>
    public IReadOnlyList<string> Errors { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a skill group from JSON.</summary>
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
/// Describes a single skill.
/// </summary>
public sealed class SkillInfo {
    public SkillInfo(string name, string? description, bool enabled, SkillInterfaceInfo? @interface,
        JsonObject raw, JsonObject? additional) {
        Name = name;
        Description = description;
        Enabled = enabled;
        Interface = @interface;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Skill name.</summary>
    public string Name { get; }
    /// <summary>Skill description (if available).</summary>
    public string? Description { get; }
    /// <summary>True when the skill is enabled.</summary>
    public bool Enabled { get; }
    /// <summary>UI metadata for the skill (if available).</summary>
    public SkillInterfaceInfo? Interface { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a skill from JSON.</summary>
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
/// UI metadata for a skill.
/// </summary>
public sealed class SkillInterfaceInfo {
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

    /// <summary>Display name (if provided).</summary>
    public string? DisplayName { get; }
    /// <summary>Short description (if provided).</summary>
    public string? ShortDescription { get; }
    /// <summary>Small icon URL (if provided).</summary>
    public string? IconSmall { get; }
    /// <summary>Large icon URL (if provided).</summary>
    public string? IconLarge { get; }
    /// <summary>Brand color (if provided).</summary>
    public string? BrandColor { get; }
    /// <summary>Default prompt (if provided).</summary>
    public string? DefaultPrompt { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses UI metadata from JSON.</summary>
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
