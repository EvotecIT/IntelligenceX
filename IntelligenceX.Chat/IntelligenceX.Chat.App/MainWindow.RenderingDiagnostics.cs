using System;
using System.IO;
using System.Reflection;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static void StartupLogRendererDiagnostics() {
        try {
            var rendererAssembly = typeof(MarkdownRenderer).Assembly;
            StartupLog.Write("Renderer.MarkdownRenderer assembly=" + BuildAssemblyIdentity(rendererAssembly));

            var officeImoMarkdownAssembly = Type.GetType(
                "OfficeIMO.Markdown.MarkdownInputNormalizer, OfficeIMO.Markdown",
                throwOnError: false)?.Assembly;
            if (officeImoMarkdownAssembly is not null) {
                StartupLog.Write("Renderer.OfficeIMOMarkdown assembly=" + BuildAssemblyIdentity(officeImoMarkdownAssembly));
            } else {
                StartupLog.Write("Renderer.OfficeIMOMarkdown assembly=unavailable");
            }
        } catch (Exception ex) {
            StartupLog.Write("Renderer diagnostics failed: " + ex.Message);
        }
    }

    private static string BuildAssemblyIdentity(Assembly assembly) {
        var name = assembly.GetName();
        var version = name.Version?.ToString() ?? "unknown";
        var location = string.Empty;
        try {
            location = assembly.Location ?? string.Empty;
        } catch {
            location = string.Empty;
        }

        var path = string.IsNullOrWhiteSpace(location)
            ? "(dynamic)"
            : Path.GetFullPath(location);
        return $"{name.Name} version={version} path={path}";
    }
}
