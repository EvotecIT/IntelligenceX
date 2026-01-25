using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class SkillListResult {
    public SkillListResult(IReadOnlyList<SkillGroup> groups, JsonObject raw, JsonObject? additional) {
        Groups = groups;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<SkillGroup> Groups { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class SkillGroup {
    public SkillGroup(string? cwd, IReadOnlyList<SkillInfo> skills, IReadOnlyList<string> errors,
        JsonObject raw, JsonObject? additional) {
        Cwd = cwd;
        Skills = skills;
        Errors = errors;
        Raw = raw;
        Additional = additional;
    }

    public string? Cwd { get; }
    public IReadOnlyList<SkillInfo> Skills { get; }
    public IReadOnlyList<string> Errors { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

    public string Name { get; }
    public string? Description { get; }
    public bool Enabled { get; }
    public SkillInterfaceInfo? Interface { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

    public string? DisplayName { get; }
    public string? ShortDescription { get; }
    public string? IconSmall { get; }
    public string? IconLarge { get; }
    public string? BrandColor { get; }
    public string? DefaultPrompt { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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
