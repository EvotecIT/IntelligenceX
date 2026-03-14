using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebStaticAssets {
    private static readonly Dictionary<string, (byte[] content, string contentType)> Assets = LoadAssets();

    private static Dictionary<string, (byte[] content, string contentType)> LoadAssets() {
        var asm = typeof(WebStaticAssets).Assembly;
        return new Dictionary<string, (byte[], string)>(StringComparer.OrdinalIgnoreCase) {
            ["/index.html"] = (LoadResource(asm, "Setup.Web.index.html"), "text/html; charset=utf-8"),
            ["/app.js"] = (LoadCombinedUtf8Resources(asm,
                "Setup.Web.wizard.js",
                "Setup.Web.wizard.setup.js",
                "Setup.Web.wizard.formatting.js",
                "Setup.Web.wizard.flows.js"), "text/javascript; charset=utf-8"),
            ["/styles.css"] = (LoadResource(asm, "Setup.Web.wizard.css"), "text/css; charset=utf-8"),
        };
    }

    private static byte[] LoadResource(Assembly assembly, string name) {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] LoadCombinedUtf8Resources(Assembly assembly, params string[] names) {
        var content = new StringBuilder();
        foreach (var name in names) {
            if (content.Length > 0) {
                content.AppendLine();
                content.AppendLine();
            }

            content.Append(LoadUtf8Resource(assembly, name));
        }

        return Encoding.UTF8.GetBytes(content.ToString());
    }

    private static string LoadUtf8Resource(Assembly assembly, string name) {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static byte[]? TryGet(string path, out string contentType) {
        if (Assets.TryGetValue(path, out var entry)) {
            contentType = entry.contentType;
            return entry.content;
        }
        contentType = "application/octet-stream";
        return null;
    }
}
