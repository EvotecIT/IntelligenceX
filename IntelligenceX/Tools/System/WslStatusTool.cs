using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Tools;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Tool that reports Windows Subsystem for Linux (WSL) distribution status.
/// </summary>
public sealed class WslStatusTool : ITool {
    private const int ProcessTimeoutMs = 5000;
    private static readonly ToolDefinition DefinitionValue = new(
        "wsl_status",
        "Report Windows Subsystem for Linux (WSL) distribution status.",
        new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("name", new JsonObject().Add("type", "string").Add("description", "Optional distribution name.")))
            .Add("additionalProperties", false));

    /// <inheritdoc />
    public ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return Task.Run(() => Execute(arguments, cancellationToken), cancellationToken);
    }

    private static string Execute(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
            return "WSL is only available on Windows.";
        }

        var nameFilter = arguments?.GetString("name");
        var output = RunProcess("wsl.exe", "-l -v", cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) {
            return "WSL returned no output.";
        }

        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) {
            return output.Trim();
        }

        var builder = new StringBuilder();
        for (var i = 1; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.Length == 0) {
                continue;
            }
            line = line.TrimStart('*').Trim();
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) {
                continue;
            }
            var name = parts[0];
            var state = parts.Length > 1 ? parts[1] : "Unknown";
            var version = parts.Length > 2 ? parts[2] : string.Empty;

            if (!string.IsNullOrWhiteSpace(nameFilter)) {
                if (!string.Equals(name, nameFilter, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                return string.IsNullOrWhiteSpace(version)
                    ? $"{name}: {state}"
                    : $"{name}: {state} (v{version})";
            }

            if (builder.Length == 0) {
                builder.AppendLine("WSL distributions:");
            }
            builder.Append("- ").Append(name).Append(": ").Append(state);
            if (!string.IsNullOrWhiteSpace(version)) {
                builder.Append(" (v").Append(version).Append(')');
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(nameFilter)) {
            return $"WSL distribution '{nameFilter}' not found.";
        }

        return builder.Length == 0 ? output.Trim() : builder.ToString().TrimEnd();
    }

    private static string RunProcess(string fileName, string arguments, CancellationToken cancellationToken) {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi);
        if (process is null) {
            return $"Failed to start {fileName}.";
        }

        using (process) {
            if (!process.WaitForExit(ProcessTimeoutMs)) {
                try {
                    if (!process.HasExited) {
                        process.Kill();
                    }
                } catch (Exception ex) {
                    return $"{fileName} timed out and could not be terminated: {ex.Message}";
                }
                return $"{fileName} timed out.";
            }

            cancellationToken.ThrowIfCancellationRequested();
            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output)) {
                output = process.StandardError.ReadToEnd();
            }
            return output;
        }
    }
}
