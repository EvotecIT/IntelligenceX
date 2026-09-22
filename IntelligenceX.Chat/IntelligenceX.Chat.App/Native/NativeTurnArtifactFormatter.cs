using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App.Native;

/// <summary>Projects completed-turn tool receipts and generated-image references into persisted transcript text.</summary>
internal static class NativeTurnArtifactFormatter {
    internal static IReadOnlyList<string> Format(ChatResultMessage response) {
        var artifacts = new List<string>();
        if (response.Tools is { Outputs.Count: > 0 } tools) {
            artifacts.Add(ToolRunMarkdownFormatter.Format(tools,
                static name => string.IsNullOrWhiteSpace(name) ? "Tool" : name.Trim()));
        }

        if (response.Images is { Length: > 0 } images) {
            var references = images.Select(TryFormatImage).Where(static value => value is not null).ToArray();
            if (references.Length > 0) artifacts.Add("**Generated images:**\n\n" + string.Join("\n\n", references!));
        }
        return artifacts;
    }

    private static string? TryFormatImage(ChatImageOutputDto image) {
        var reference = (image.Url ?? string.Empty).Trim();
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) {
            var path = (image.Path ?? string.Empty).Trim();
            if (!Path.IsPathFullyQualified(path)) return null;
            uri = new Uri(path);
        }

        return "![Generated image](" + uri.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal) + ")";
    }
}
