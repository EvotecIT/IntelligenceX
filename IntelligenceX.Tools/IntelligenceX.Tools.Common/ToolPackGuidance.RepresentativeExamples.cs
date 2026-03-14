using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

public static partial class ToolPackGuidance {
    internal static IReadOnlyList<string> NormalizeRepresentativeExamplesContract(IEnumerable<string>? examples) {
        return NormalizeValues(examples);
    }
}
