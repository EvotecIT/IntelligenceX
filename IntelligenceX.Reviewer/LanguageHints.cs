using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class LanguageHints {
    private const int MaxLanguages = 4;

    private sealed class Hint {
        public Hint(string id, string name, string text) {
            Id = id;
            Name = name;
            Text = text;
        }

        public string Id { get; }
        public string Name { get; }
        public string Text { get; }
    }

    private static readonly IReadOnlyDictionary<string, Hint> HintsById =
        new Dictionary<string, Hint>(StringComparer.OrdinalIgnoreCase) {
            ["csharp"] = new Hint("csharp", "C#", "Nullability, async/await usage, disposal, and exception handling."),
            ["ts"] = new Hint("ts", "TypeScript/JavaScript", "Type safety, null/undefined handling, async error paths."),
            ["python"] = new Hint("python", "Python", "Type hints, resource cleanup, async vs sync correctness."),
            ["go"] = new Hint("go", "Go", "Error handling, context propagation, goroutine leaks."),
            ["java"] = new Hint("java", "Java/Kotlin", "Null safety, exception handling, resource closing."),
            ["cpp"] = new Hint("cpp", "C/C++", "Memory ownership, bounds checks, null pointer safety."),
            ["rust"] = new Hint("rust", "Rust", "Ownership/borrowing, error handling, unsafe usage."),
            ["powershell"] = new Hint("powershell", "PowerShell", "ErrorAction usage, pipeline behavior, quoting."),
            ["sql"] = new Hint("sql", "SQL", "Query correctness, injection risks, indexes.")
        };

    private static readonly IReadOnlyDictionary<string, string> ExtensionMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [".cs"] = "csharp",
            [".csx"] = "csharp",
            [".csproj"] = "csharp",
            [".sln"] = "csharp",
            [".ts"] = "ts",
            [".tsx"] = "ts",
            [".mts"] = "ts",
            [".cts"] = "ts",
            [".js"] = "ts",
            [".jsx"] = "ts",
            [".mjs"] = "ts",
            [".cjs"] = "ts",
            [".py"] = "python",
            [".go"] = "go",
            [".java"] = "java",
            [".kt"] = "java",
            [".kts"] = "java",
            [".c"] = "cpp",
            [".h"] = "cpp",
            [".cc"] = "cpp",
            [".cpp"] = "cpp",
            [".hpp"] = "cpp",
            [".rs"] = "rust",
            [".ps1"] = "powershell",
            [".psm1"] = "powershell",
            [".sql"] = "sql"
        };

    public static string Build(IReadOnlyList<PullRequestFile> files, bool enabled) {
        if (!enabled || files.Count == 0) {
            return string.Empty;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            var id = ResolveLanguageId(file.Filename);
            if (string.IsNullOrWhiteSpace(id)) {
                continue;
            }
            counts[id!] = counts.TryGetValue(id!, out var count) ? count + 1 : 1;
        }

        if (counts.Count == 0) {
            return string.Empty;
        }

        var ordered = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxLanguages)
            .Select(pair => HintsById[pair.Key])
            .ToList();

        if (ordered.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Language hints:");
        foreach (var hint in ordered) {
            sb.AppendLine($"- {hint.Name}: {hint.Text}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string? ResolveLanguageId(string filename) {
        if (string.IsNullOrWhiteSpace(filename)) {
            return null;
        }

        var name = Path.GetFileName(filename.Replace('\\', '/'));
        var ext = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(ext)) {
            return null;
        }

        return ExtensionMap.TryGetValue(ext, out var id) && HintsById.ContainsKey(id) ? id : null;
    }
}
