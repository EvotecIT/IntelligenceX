using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Cli.Analysis;

internal static class SourceLanguageConventions {
    internal static readonly string[] CSharpSourceExtensions = {
        ".cs"
    };

    internal static readonly string[] PowerShellSourceExtensions = {
        ".ps1",
        ".psm1",
        ".psd1"
    };

    internal static readonly string[] JavaScriptSourceExtensions = {
        ".js",
        ".jsx",
        ".mjs",
        ".cjs",
        ".ts",
        ".tsx",
        ".mts",
        ".cts"
    };

    internal static readonly string[] PythonSourceExtensions = {
        ".py",
        ".pyi"
    };

    internal static readonly string[] ShellSourceExtensions = {
        ".sh",
        ".bash",
        ".zsh"
    };

    internal static readonly string[] YamlSourceExtensions = {
        ".yml",
        ".yaml"
    };

    internal static readonly string JavaScriptEslintExtensionsArg =
        string.Join(",", JavaScriptSourceExtensions);

    private static readonly Dictionary<string, string> LanguageByExtension = BuildLanguageByExtension();
    private static readonly HashSet<string> TrackedSourceExtensions = BuildTrackedSourceExtensions();

    internal static string ResolveLanguageFromPath(string? path) {
        var extension = NormalizeExtension(Path.GetExtension(path ?? string.Empty));
        if (string.IsNullOrWhiteSpace(extension)) {
            return "unknown";
        }

        return LanguageByExtension.TryGetValue(extension, out var language)
            ? language
            : "unknown";
    }

    internal static bool IsJavaScriptOrTypeScriptExtension(string? extension) {
        return HasExtension(JavaScriptSourceExtensions, extension);
    }

    internal static bool IsPythonExtension(string? extension) {
        return HasExtension(PythonSourceExtensions, extension);
    }

    internal static bool IsShellExtension(string? extension) {
        return HasExtension(ShellSourceExtensions, extension);
    }

    internal static bool IsYamlExtension(string? extension) {
        return HasExtension(YamlSourceExtensions, extension);
    }

    internal static bool IsTrackedSourceExtension(string? extension) {
        var normalized = NormalizeExtension(extension);
        return !string.IsNullOrWhiteSpace(normalized) && TrackedSourceExtensions.Contains(normalized);
    }

    private static bool HasExtension(IReadOnlyList<string> knownExtensions, string? extension) {
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(normalized) || knownExtensions is null || knownExtensions.Count == 0) {
            return false;
        }

        foreach (var known in knownExtensions) {
            if (normalized.Equals(known, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeExtension(string? extension) {
        if (string.IsNullOrWhiteSpace(extension)) {
            return string.Empty;
        }

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith(".", StringComparison.Ordinal)) {
            normalized = "." + normalized;
        }

        return normalized;
    }

    private static Dictionary<string, string> BuildLanguageByExtension() {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in CSharpSourceExtensions) {
            map[extension] = "csharp";
        }

        foreach (var extension in PowerShellSourceExtensions) {
            map[extension] = "powershell";
        }

        map[".js"] = "javascript";
        map[".jsx"] = "javascript";
        map[".mjs"] = "javascript";
        map[".cjs"] = "javascript";
        map[".ts"] = "typescript";
        map[".tsx"] = "typescript";
        map[".mts"] = "typescript";
        map[".cts"] = "typescript";

        foreach (var extension in PythonSourceExtensions) {
            map[extension] = "python";
        }

        foreach (var extension in ShellSourceExtensions) {
            map[extension] = "shell";
        }

        foreach (var extension in YamlSourceExtensions) {
            map[extension] = "yaml";
        }

        return map;
    }

    private static HashSet<string> BuildTrackedSourceExtensions() {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in CSharpSourceExtensions) {
            set.Add(extension);
        }
        foreach (var extension in PowerShellSourceExtensions) {
            set.Add(extension);
        }
        foreach (var extension in JavaScriptSourceExtensions) {
            set.Add(extension);
        }
        foreach (var extension in PythonSourceExtensions) {
            set.Add(extension);
        }
        foreach (var extension in ShellSourceExtensions) {
            set.Add(extension);
        }
        foreach (var extension in YamlSourceExtensions) {
            set.Add(extension);
        }

        return set;
    }
}
