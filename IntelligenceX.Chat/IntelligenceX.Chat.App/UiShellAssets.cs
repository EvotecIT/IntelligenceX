using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.App;

internal static class UiShellAssets {
    private const string TemplateFile = "ShellTemplate.html";
    private const string CssFile = "Shell.css";
    private const string CssSplitPattern = "Shell.*.css";
    private const string JsFile = "Shell.js";
    private const string JsSplitPattern = "Shell.*.js";
    private const string DefaultLocalModelToken = "\"{{IXCHAT_DEFAULT_LOCAL_MODEL}}\"";
    private const int MaxDiagnosticsInFallback = 24;

    private static readonly string[] TemplateTokens = [
        "/*{{IXCHAT_CSS}}*/",
        "<!--{{IXCHAT_THEME_OPTIONS}}-->",
        "//{{IXCHAT_JS}}"
    ];

    private static readonly string[] JsManifest = [
        "Shell.10.core.js",
        "Shell.12.core.helpers.js",
        "Shell.15.core.tools.js",
        "Shell.18.core.tools.rendering.js",
        "Shell.18a.transcript.rendering.js",
        "Shell.16.core.datatables.js",
        "Shell.21.core.visuals.js",
        "Shell.17.core.dataview.js",
        "Shell.19.core.dataview.actions.js",
        "Shell.20.bindings.js",
        "Shell.22.bindings.wheel.js"
    ];

    private static readonly string[] CssManifest = [
        "Shell.10.base.css",
        "Shell.20.chat.css",
        "Shell.25.datatables.css",
        "Shell.27.dataview.css",
        "Shell.30.options.css"
    ];

    private static readonly string[] RequiredJavaScriptSymbols = [
        "(function() {",
        "function renderSidebarConversations(",
        "function packSourceKind(",
        "function packSourceLabel(",
        "function renderTools(",
        "function renderOptions(",
        "function initTranscriptDataTable(",
        "window.ixDisposeTranscriptVisuals = function(root) {",
        "window.ixRenderTranscriptVisuals = function(root) {",
        "function renderIxChartBlock(",
        "function renderIxNetworkBlock(",
        "function openDataView(",
        "window.ixCloseDataView = closeDataView;",
        "renderOptions();",
        "})();"
    ];

    private static readonly object Lock = new();
    private static string? _cachedHtml;

    internal static IReadOnlyList<string> JavaScriptManifest => JsManifest;
    internal static IReadOnlyList<string> StyleManifest => CssManifest;

    public static string Load() {
        lock (Lock) {
            if (!string.IsNullOrWhiteSpace(_cachedHtml)) {
                return _cachedHtml!;
            }

            var uiDir = Path.Combine(AppContext.BaseDirectory, "Ui");
            var diagnostics = new List<string>();
            var template = ReadTextOrEmpty(Path.Combine(uiDir, TemplateFile));
            var css = ReadShellCss(uiDir, diagnostics);
            var js = ReadShellJavaScript(uiDir, diagnostics);

            ValidateTemplate(template, diagnostics);
            ValidateJavaScriptContracts(js, diagnostics);
            ValidateCatalogBackedJavaScriptDefaults(js, diagnostics);

            if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(css) || string.IsNullOrWhiteSpace(js) || diagnostics.Count > 0) {
                // Do not cache fallback diagnostics HTML. A transient startup packaging issue
                // should recover automatically on the next Load() call once files are fixed.
                return BuildFallbackHtml(diagnostics);
            }

            _cachedHtml = template
                .Replace("/*{{IXCHAT_CSS}}*/", css, StringComparison.Ordinal)
                .Replace("<!--{{IXCHAT_THEME_OPTIONS}}-->", ThemeContract.BuildThemeOptionTagsHtml(), StringComparison.Ordinal)
                .Replace("//{{IXCHAT_JS}}", ApplyCatalogBackedJavaScriptDefaults(js), StringComparison.Ordinal);
            return _cachedHtml;
        }
    }

    private static string ApplyCatalogBackedJavaScriptDefaults(string js) {
        return js.Replace(
            DefaultLocalModelToken,
            JsonSerializer.Serialize(OpenAIModelCatalog.DefaultModel),
            StringComparison.Ordinal);
    }

    private static void ValidateCatalogBackedJavaScriptDefaults(string js, List<string> diagnostics) {
        var tokenCount = CountOccurrences(js, DefaultLocalModelToken);
        if (tokenCount == 0) {
            diagnostics.Add("Expected at least one default local model token in JavaScript, found none.");
        }
    }

    private static int CountOccurrences(string value, string token) {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) {
            return 0;
        }

        var count = 0;
        var startIndex = 0;
        while (true) {
            var index = value.IndexOf(token, startIndex, StringComparison.Ordinal);
            if (index < 0) {
                return count;
            }

            count++;
            startIndex = index + token.Length;
        }
    }

    private static string ReadTextOrEmpty(string path) {
        try {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private static string ReadShellJavaScript(string uiDir, List<string> diagnostics) {
        return ReadShellByManifest(
            uiDir,
            JsSplitPattern,
            JsManifest,
            JsFile,
            "JavaScript",
            diagnostics);
    }

    private static string ReadShellCss(string uiDir, List<string> diagnostics) {
        return ReadShellByManifest(
            uiDir,
            CssSplitPattern,
            CssManifest,
            CssFile,
            "CSS",
            diagnostics);
    }

    private static string ReadShellByManifest(
        string uiDir,
        string splitPattern,
        IReadOnlyList<string> manifest,
        string singleFileName,
        string label,
        List<string> diagnostics) {

        if (!Directory.Exists(uiDir)) {
            diagnostics.Add($"Missing UI directory: {uiDir}");
            return string.Empty;
        }

        try {
            var splitFiles = Directory.EnumerateFiles(uiDir, splitPattern, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (splitFiles.Length == 0) {
                var legacyPath = Path.Combine(uiDir, singleFileName);
                var legacyContent = ReadTextOrEmpty(legacyPath);
                if (string.IsNullOrWhiteSpace(legacyContent)) {
                    diagnostics.Add($"{label} asset missing. Expected split files ({splitPattern}) or legacy {singleFileName}.");
                }
                return legacyContent;
            }

            var missing = manifest
                .Where(name => !splitFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missing.Length > 0) {
                diagnostics.Add($"{label} manifest incomplete. Missing files: {string.Join(", ", missing)}");
                return string.Empty;
            }

            // Ignore unknown split chunks to stay forward-compatible with packaging variations.
            // We still compose only manifest files in explicit order for deterministic output.

            var parts = new List<string>(manifest.Count);
            foreach (var fileName in manifest) {
                var partPath = Path.Combine(uiDir, fileName);
                var part = ReadTextOrEmpty(partPath);
                if (string.IsNullOrWhiteSpace(part)) {
                    diagnostics.Add($"{label} manifest file is empty or unreadable: {fileName}");
                    return string.Empty;
                }
                parts.Add($"/* IXCHAT_PART:{fileName} */{Environment.NewLine}{part}");
            }

            return string.Join(Environment.NewLine, parts);
        } catch (Exception ex) {
            diagnostics.Add($"{label} asset composition failed: {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static void ValidateTemplate(string template, List<string> diagnostics) {
        if (string.IsNullOrWhiteSpace(template)) {
            diagnostics.Add($"Missing template file: {TemplateFile}.");
            return;
        }

        foreach (var token in TemplateTokens) {
            if (template.IndexOf(token, StringComparison.Ordinal) < 0) {
                diagnostics.Add($"Template token not found: {token}");
            }
        }
    }

    private static void ValidateJavaScriptContracts(string js, List<string> diagnostics) {
        if (string.IsNullOrWhiteSpace(js)) {
            return;
        }

        foreach (var symbol in RequiredJavaScriptSymbols) {
            if (js.IndexOf(symbol, StringComparison.Ordinal) < 0) {
                diagnostics.Add($"JavaScript contract missing required symbol: {symbol}");
            }
        }
    }

    private static string BuildFallbackHtml(IReadOnlyList<string> diagnostics) {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { margin:0; background:#0a1929; color:#e9f4ff; font-family:Segoe UI, sans-serif; }");
        sb.AppendLine("    .box { padding:24px; line-height:1.45; }");
        sb.AppendLine("    .title { font-weight:600; margin-bottom:12px; }");
        sb.AppendLine("    .hint { opacity:0.8; margin-bottom:12px; }");
        sb.AppendLine("    ul { margin:0; padding-left:20px; }");
        sb.AppendLine("    li { margin:6px 0; word-break:break-word; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"box\">");
        sb.AppendLine("    <div class=\"title\">UI shell assets are invalid. Chat UI startup was blocked intentionally.</div>");
        sb.AppendLine("    <div class=\"hint\">Fix the diagnostics below, then rebuild the app.</div>");

        if (diagnostics is { Count: > 0 }) {
            sb.AppendLine("    <ul>");
            foreach (var diagnostic in diagnostics.Take(MaxDiagnosticsInFallback)) {
                sb.Append("      <li>");
                sb.Append(WebUtility.HtmlEncode(diagnostic));
                sb.AppendLine("</li>");
            }
            if (diagnostics.Count > MaxDiagnosticsInFallback) {
                sb.AppendLine("      <li>... additional diagnostics omitted ...</li>");
            }
            sb.AppendLine("    </ul>");
        } else {
            sb.AppendLine("    <div>No diagnostics were captured.</div>");
        }

        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}
