using System;
using System.IO;
using System.Text.Json;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardConfigEditor {
    private const string Template = @"{
  ""review"": {
    ""provider"": ""openai"",
    ""openaiTransport"": ""native"",
    ""openaiModel"": ""gpt-5.2-codex"",
    ""profile"": ""balanced"",
    ""mode"": ""hybrid"",
    ""commentMode"": ""sticky"",
    ""includeIssueComments"": true,
    ""includeReviewComments"": true,
    ""includeRelatedPullRequests"": true,
    ""progressUpdates"": true
  }
}";

    public static string? EditInEditor() {
        var tempPath = CreateTempFile();
        try {
            File.WriteAllText(tempPath, Template);
            OpenEditor(tempPath);
            AnsiConsole.MarkupLine("[grey]Edit the file, save it, then press Enter to continue.[/]");
            Console.ReadLine();

            while (true) {
                var content = File.ReadAllText(tempPath);
                if (IsValidJson(content)) {
                    return content;
                }
                AnsiConsole.MarkupLine("[red]Config JSON is invalid.[/]");
                if (!AnsiConsole.Confirm("Re-open editor?", true)) {
                    return null;
                }
                OpenEditor(tempPath);
                AnsiConsole.MarkupLine("[grey]Edit the file, save it, then press Enter to continue.[/]");
                Console.ReadLine();
            }
        } finally {
            TryDelete(tempPath);
        }
    }

    public static bool IsValidJson(string? content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return false;
        }
        try {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        } catch {
            return false;
        }
    }

    private static string CreateTempFile() {
        var name = $"intelligencex-config-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json";
        return Path.Combine(Path.GetTempPath(), name);
    }

    private static void OpenEditor(string path) {
        var editor = Environment.GetEnvironmentVariable("VISUAL")
                     ?? Environment.GetEnvironmentVariable("EDITOR");
        if (string.IsNullOrWhiteSpace(editor)) {
            editor = OperatingSystem.IsWindows() ? "notepad" : "nano";
        }
        TryStartProcess(editor!, path);
    }

    private static void TryStartProcess(string editor, string path) {
        try {
            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = editor,
                Arguments = NeedsWaitFlag(editor) ? $"--wait \"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        } catch {
            // Best effort.
        }
    }

    private static bool NeedsWaitFlag(string editor) {
        var name = Path.GetFileName(editor).ToLowerInvariant();
        return name is "code" or "code.cmd" or "code.exe";
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Ignore cleanup failure.
        }
    }
}
