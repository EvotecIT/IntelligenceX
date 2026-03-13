using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryHtmlTemplateBinder {
    public static string Bind(string template, IReadOnlyDictionary<string, string?> values) {
        if (template is null) {
            throw new ArgumentNullException(nameof(template));
        }
        if (values is null) {
            throw new ArgumentNullException(nameof(values));
        }

        var result = new StringBuilder(template);
        foreach (var pair in values) {
            result.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty);
        }

        return result.ToString();
    }
}
