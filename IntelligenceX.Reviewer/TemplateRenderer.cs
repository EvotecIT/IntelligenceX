using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal static class TemplateRenderer {
    public static string Render(string template, IReadOnlyDictionary<string, string> tokens) {
        var output = template;
        foreach (var pair in tokens) {
            output = output.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
        }
        return output;
    }
}
