using System;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App;

internal static class UiShellAssets {
    private const string TemplateFile = "ShellTemplate.html";
    private const string CssFile = "Shell.css";
    private const string CssSplitPattern = "Shell.*.css";
    private const string JsFile = "Shell.js";
    private const string JsSplitPattern = "Shell.*.js";

    private static readonly object Lock = new();
    private static string? _cachedHtml;

    public static string Load() {
        lock (Lock) {
            if (!string.IsNullOrWhiteSpace(_cachedHtml)) {
                return _cachedHtml!;
            }

            var uiDir = Path.Combine(AppContext.BaseDirectory, "Ui");
            var template = ReadTextOrEmpty(Path.Combine(uiDir, TemplateFile));
            var css = ReadShellCss(uiDir);
            var js = ReadShellJavaScript(uiDir);

            if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(css) || string.IsNullOrWhiteSpace(js)) {
                _cachedHtml = BuildFallbackHtml();
                return _cachedHtml;
            }

            _cachedHtml = template
                .Replace("/*{{IXCHAT_CSS}}*/", css, StringComparison.Ordinal)
                .Replace("<!--{{IXCHAT_THEME_OPTIONS}}-->", ThemeContract.BuildThemeOptionTagsHtml(), StringComparison.Ordinal)
                .Replace("//{{IXCHAT_JS}}", js, StringComparison.Ordinal);
            return _cachedHtml;
        }
    }

    private static string ReadTextOrEmpty(string path) {
        try {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private static string ReadShellJavaScript(string uiDir) {
        try {
            if (Directory.Exists(uiDir)) {
                var parts = Directory.EnumerateFiles(uiDir, JsSplitPattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(ReadTextOrEmpty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                if (parts.Length > 0) {
                    return string.Join(Environment.NewLine, parts);
                }
            }
        } catch {
            // Fall through to legacy single-file loader.
        }

        return ReadTextOrEmpty(Path.Combine(uiDir, JsFile));
    }

    private static string ReadShellCss(string uiDir) {
        try {
            if (Directory.Exists(uiDir)) {
                var parts = Directory.EnumerateFiles(uiDir, CssSplitPattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(ReadTextOrEmpty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                if (parts.Length > 0) {
                    return string.Join(Environment.NewLine, parts);
                }
            }
        } catch {
            // Fall through to legacy single-file loader.
        }

        return ReadTextOrEmpty(Path.Combine(uiDir, CssFile));
    }

    private static string BuildFallbackHtml() {
        return """
               <!doctype html>
               <html>
               <head>
                 <meta charset="utf-8" />
                 <meta name="viewport" content="width=device-width, initial-scale=1" />
                 <style>
                   body { margin:0; background:#0a1929; color:#e9f4ff; font-family:Segoe UI, sans-serif; }
                   .box { padding:24px; }
                 </style>
               </head>
               <body>
                 <div class="box">UI assets are missing. Rebuild the app to restore ShellTemplate/CSS/JS files.</div>
               </body>
               </html>
               """;
    }
}
