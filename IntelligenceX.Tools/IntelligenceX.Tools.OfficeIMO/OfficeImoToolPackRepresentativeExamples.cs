using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.OfficeIMO;

internal static class OfficeImoToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["officeimo_read"] = new[] {
                "read a local Word, Excel, PowerPoint, Markdown, or PDF document and return normalized content for analysis"
            }
        };
}
