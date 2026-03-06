using OfficeIMO.MarkdownRenderer;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralized markdown renderer options used by the desktop chat shell.
/// </summary>
internal static class ChatMarkdownOptions {
    /// <summary>
    /// Creates strict markdown options with Mermaid enabled for transcript visualization.
    /// </summary>
    public static MarkdownRendererOptions Create() {
        // Preset factory returns a fresh options object per call; this mutation is call-local.
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        options.Mermaid.Enabled = true;
        EnableOptionalIxFenceExtensions(options);
        return options;
    }

    private static void EnableOptionalIxFenceExtensions(MarkdownRendererOptions options) {
        var rendererCollectionProperty = typeof(MarkdownRendererOptions).GetProperty(
            "FencedCodeBlockRenderers",
            BindingFlags.Instance | BindingFlags.Public);
        if (rendererCollectionProperty?.GetValue(options) is not IList rendererCollection) {
            return;
        }

        var assembly = typeof(MarkdownRendererOptions).Assembly;
        var rendererType = assembly.GetType("OfficeIMO.MarkdownRenderer.MarkdownFencedCodeBlockRenderer", throwOnError: false);
        var rendererDelegateType = assembly.GetType("OfficeIMO.MarkdownRenderer.MarkdownFencedCodeBlockHtmlRenderer", throwOnError: false);
        if (rendererType == null || rendererDelegateType == null) {
            return;
        }

        rendererCollection.Add(CreateFencedCodeBlockRenderer(
            rendererType,
            rendererDelegateType,
            "IX chart",
            new[] { "ix-chart" },
            nameof(RenderIxChartHtml)));
        rendererCollection.Add(CreateFencedCodeBlockRenderer(
            rendererType,
            rendererDelegateType,
            "IX network",
            new[] { "ix-network" },
            nameof(RenderIxNetworkHtml)));
    }

    private static object CreateFencedCodeBlockRenderer(Type rendererType, Type rendererDelegateType, string name, string[] languages, string helperMethodName) {
        var invokeMethod = rendererDelegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("OfficeIMO fenced code block renderer delegate does not expose Invoke.");
        var delegateParameters = invokeMethod.GetParameters();
        if (delegateParameters.Length != 2) {
            throw new InvalidOperationException("Unexpected OfficeIMO fenced code block renderer delegate signature.");
        }

        var matchParameter = Expression.Parameter(delegateParameters[0].ParameterType, "match");
        var optionsParameter = Expression.Parameter(delegateParameters[1].ParameterType, "options");
        var helperMethod = typeof(ChatMarkdownOptions).GetMethod(helperMethodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing helper method {helperMethodName}.");
        var body = Expression.Call(helperMethod, Expression.Convert(matchParameter, typeof(object)));
        var rendererDelegate = Expression.Lambda(rendererDelegateType, body, matchParameter, optionsParameter).Compile();

        var constructor = rendererType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(c => {
                var parameters = c.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(string)
                    && typeof(IEnumerable<string>).IsAssignableFrom(parameters[1].ParameterType)
                    && parameters[2].ParameterType == rendererDelegateType;
            })
            ?? throw new InvalidOperationException("OfficeIMO fenced code block renderer constructor was not found.");

        return constructor.Invoke(new object[] { name, languages, rendererDelegate });
    }

    private static string RenderIxChartHtml(object match) {
        return BuildNativeFenceHtml(match, "omd-chart", "data-chart-hash", "data-chart-config-b64", "canvas");
    }

    private static string RenderIxNetworkHtml(object match) {
        return BuildNativeFenceHtml(match, "omd-network", "data-network-hash", "data-network-config-b64", "div");
    }

    private static string BuildNativeFenceHtml(object match, string cssClass, string hashAttribute, string configAttribute, string elementName) {
        var raw = GetFenceRawContent(match);
        var hash = ComputeShortHash(raw);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return $"<{elementName} class=\"{cssClass}\" {hashAttribute}=\"{hash}\" {configAttribute}=\"{System.Net.WebUtility.HtmlEncode(base64)}\"></{elementName}>";
    }

    private static string GetFenceRawContent(object match) {
        var property = match.GetType().GetProperty("RawContent", BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(match) as string ?? string.Empty;
    }

    private static string ComputeShortHash(string value) {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] hash;
#if NET8_0_OR_GREATER
        hash = SHA256.HashData(bytes);
#else
        using (var sha = SHA256.Create()) {
            hash = sha.ComputeHash(bytes);
        }
#endif
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8 && i < hash.Length; i++) {
            sb.Append(hash[i].ToString("x2"));
        }

        return sb.ToString();
    }
}
