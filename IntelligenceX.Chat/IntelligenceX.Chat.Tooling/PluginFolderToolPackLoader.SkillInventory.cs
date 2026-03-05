using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Chat.Tooling;

internal static partial class PluginFolderToolPackLoader {
    private const string SkillManifestFileName = "SKILL.md";

    private static string[] ResolvePluginSkillDirectories(string rootPath, PluginManifest? manifest) {
        if (manifest?.SkillDirectories is null || manifest.SkillDirectories.Length == 0) {
            return Array.Empty<string>();
        }

        var normalizedRootPath = NormalizePath(rootPath) ?? string.Empty;
        var directories = new List<string>();
        for (var i = 0; i < manifest.SkillDirectories.Length; i++) {
            var candidate = (manifest.SkillDirectories[i] ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                continue;
            }

            var path = candidate;
            if (normalizedRootPath.Length > 0 && !Path.IsPathRooted(path)) {
                path = Path.Combine(normalizedRootPath, path);
            }

            var normalizedPath = NormalizePath(path);
            if (!string.IsNullOrWhiteSpace(normalizedPath)) {
                directories.Add(normalizedPath);
            }
        }

        return directories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolvePluginSkillIds(string rootPath, PluginManifest? manifest) {
        var skillDirectories = ResolvePluginSkillDirectories(rootPath, manifest);
        if (skillDirectories.Length == 0) {
            return Array.Empty<string>();
        }

        var skillIds = new List<string>();
        foreach (var skillDirectory in skillDirectories) {
            if (!Directory.Exists(skillDirectory)) {
                continue;
            }

            IEnumerable<string> skillFiles;
            try {
                skillFiles = Directory.EnumerateFiles(skillDirectory, SkillManifestFileName, SearchOption.AllDirectories);
            } catch {
                continue;
            }

            foreach (var skillFile in skillFiles) {
                var skillId = ResolvePluginSkillId(skillDirectory, skillFile);
                if (skillId.Length > 0) {
                    skillIds.Add(skillId);
                }
            }
        }

        return skillIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static skillId => skillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolvePluginSkillId(string skillDirectory, string skillFile) {
        if (string.IsNullOrWhiteSpace(skillDirectory) || string.IsNullOrWhiteSpace(skillFile)) {
            return string.Empty;
        }

        string relativeDirectory;
        try {
            relativeDirectory = Path.GetRelativePath(skillDirectory, Path.GetDirectoryName(skillFile) ?? skillDirectory);
        } catch {
            relativeDirectory = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory == ".") {
            return NormalizePluginSkillId(Path.GetFileName(skillDirectory));
        }

        var segments = relativeDirectory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return NormalizePluginSkillId(string.Join(".", segments));
    }

    private static string NormalizePluginSkillId(string? skillId) {
        var normalized = (skillId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 128) {
            return string.Empty;
        }

        if (normalized.Any(static ch => char.IsWhiteSpace(ch) || char.IsControl(ch))) {
            return string.Empty;
        }

        return normalized;
    }
}
